import asyncio
import json
import re
import time
import unicodedata
from typing import Any

from google.genai import types

from app.schemas import InterviewPerformanceReport
from app.services.chat import GEMINI_MODEL, GEMINI_TIMEOUT_SECONDS, client
from app.services.prompting import MAX_INTERVIEW_QUESTIONS

CATEGORY_ORDER = (
    ("content", "Content"),
    ("clarity", "Clarify"),
    ("language", "Language"),
    ("reflex", "Reflex"),
    ("structure", "Structure"),
)


def _clean_text(value: Any) -> str:
    return re.sub(r"\s+", " ", str(value or "")).strip()


def _clean_list(values: Any, fallback: list[str]) -> list[str]:
    if not isinstance(values, list):
        return fallback

    cleaned = [_clean_text(value) for value in values]
    cleaned = [value for value in cleaned if value]
    return cleaned or fallback


def _clamp_int(value: Any, minimum: int, maximum: int, fallback: int) -> int:
    if value is None:
        return fallback

    if isinstance(value, str):
        value = value.strip()
        if not value:
            return fallback

    try:
        numeric = float(value)
    except (TypeError, ValueError):
        return fallback

    numeric = int(round(numeric))
    return max(minimum, min(maximum, numeric))


def _normalize_language(language: str) -> str:
    return _clean_text(language) or "Vietnamese"


def _strip_accents(value: str) -> str:
    normalized = unicodedata.normalize("NFKD", value or "")
    stripped = "".join(char for char in normalized if not unicodedata.combining(char))
    return stripped.replace("đ", "d").replace("Đ", "D")


def extract_session_meta(entries: list[dict], session_id: str, session: Any | None = None) -> dict[str, str]:
    metadata = {
        "session_id": session_id,
        "job_title": getattr(session, "job_title", "") or "",
        "interview_type": getattr(session, "interview_type", "") or "",
        "language": _normalize_language(getattr(session, "language", "") or ""),
    }

    for entry in entries:
        if not metadata["job_title"] or metadata["job_title"] == "Unknown":
            metadata["job_title"] = _clean_text(entry.get("job_title")) or metadata["job_title"]
        if not metadata["interview_type"]:
            metadata["interview_type"] = _clean_text(entry.get("interview_type")) or metadata["interview_type"]
        if not metadata["language"] or metadata["language"] == "Vietnamese":
            metadata["language"] = _normalize_language(entry.get("language"))

    if not metadata["job_title"]:
        metadata["job_title"] = "Unknown"
    if not metadata["interview_type"]:
        metadata["interview_type"] = "Attitude Interview"
    if not metadata["language"]:
        metadata["language"] = "Vietnamese"

    return metadata


def build_transcript(entries: list[dict]) -> list[dict[str, str]]:
    transcript: list[dict[str, str]] = []
    for entry in entries:
        role = entry.get("role")
        if role not in {"user", "assistant"}:
            continue

        message = _clean_text(entry.get("message"))
        if not message:
            continue

        speaker = "Candidate" if role == "user" else "Interviewer"
        transcript.append({"speaker": speaker, "message": message})

    return transcript


def is_final_evaluation_message(message: str, language: str) -> bool:
    normalized = _clean_text(message).lower()
    if not normalized:
        return False

    normalized_plain = _strip_accents(normalized)

    if language.lower() == "vietnamese":
        return normalized_plain.startswith("ban dat:")

    return normalized.startswith("you scored:")


def _is_closing_message(message: str, language: str) -> bool:
    normalized = _clean_text(message).lower()
    if not normalized:
        return False

    normalized_plain = _strip_accents(normalized)
    if language.lower() == "vietnamese":
        vietnamese_markers = (
            "buoi phong van",
            "den day la ket thuc",
            "cam on ban",
            "cam on anh",
            "cam on chi",
            "se som lien he",
            "ket qua trong thoi gian toi",
        )
        return any(marker in normalized_plain for marker in vietnamese_markers)

    english_markers = (
        "interview has come to an end",
        "thank you for your time",
        "we will be in touch",
        "we'll be in touch",
        "the interview is now complete",
    )
    return any(marker in normalized for marker in english_markers)


def is_session_ready_for_report(entries: list[dict], language: str) -> bool:
    assistant_messages = [
        _clean_text(entry.get("message"))
        for entry in entries
        if entry.get("role") == "assistant" and _clean_text(entry.get("message"))
    ]
    user_turns = sum(1 for entry in entries if entry.get("role") == "user" and _clean_text(entry.get("message")))

    if assistant_messages and is_final_evaluation_message(assistant_messages[-1], language):
        return True

    if assistant_messages and _is_closing_message(assistant_messages[-1], language):
        return True

    return user_turns >= MAX_INTERVIEW_QUESTIONS and len(assistant_messages) >= MAX_INTERVIEW_QUESTIONS + 1


def _extract_json_payload(raw_text: str) -> dict[str, Any]:
    text = (raw_text or "").strip()
    if not text:
        raise ValueError("AI returned an empty report payload.")

    fenced_match = re.search(r"```(?:json)?\s*(\{.*\})\s*```", text, re.DOTALL)
    if fenced_match:
        text = fenced_match.group(1).strip()

    try:
        return json.loads(text)
    except json.JSONDecodeError:
        start = text.find("{")
        end = text.rfind("}")
        if start == -1 or end == -1 or end <= start:
            raise ValueError("AI did not return valid JSON.")
        return json.loads(text[start : end + 1])


def _normalize_categories(raw_categories: Any) -> list[dict[str, Any]]:
    raw_lookup: dict[str, dict[str, Any]] = {}
    if isinstance(raw_categories, list):
        for item in raw_categories:
            if not isinstance(item, dict):
                continue

            raw_key = _clean_text(item.get("key")).lower()
            raw_label = _clean_text(item.get("label")).lower()

            for key, label in CATEGORY_ORDER:
                if raw_key == key or raw_label == label.lower():
                    raw_lookup[key] = item
                    break

    categories: list[dict[str, Any]] = []
    for key, label in CATEGORY_ORDER:
        source = raw_lookup.get(key, {})
        categories.append(
            {
                "key": key,
                "label": label,
                "score": _clamp_int(source.get("score"), 0, 10, 5),
                "baseline": _clamp_int(source.get("baseline"), 0, 10, 5),
                "comment": _clean_text(source.get("comment")) or f"{label} needs a clearer coaching note.",
            }
        )

    return categories


def normalize_report_payload(
    session_id: str,
    metadata: dict[str, str],
    transcript_turns: int,
    payload: dict[str, Any],
) -> dict[str, Any]:
    overall_score = _clamp_int(payload.get("overall_score"), 0, 100, 70)
    if overall_score <= 10:
        overall_score *= 10

    headline = _clean_text(payload.get("headline")) or "Solid foundation shown across the interview."
    overall_summary = _clean_text(payload.get("overall_summary")) or "The candidate showed a workable baseline with room to become more precise and persuasive."

    return {
        "session_id": session_id,
        "job_title": metadata["job_title"],
        "interview_type": metadata["interview_type"],
        "language": metadata["language"],
        "overall_score": overall_score,
        "headline": headline,
        "overall_summary": overall_summary,
        "actionable_tips": _clean_list(
            payload.get("actionable_tips"),
            [
                "Use one concrete example per answer to make the message more credible.",
                "Tighten the opening of each answer before adding extra detail.",
                "Close answers with impact, outcome, or lesson learned.",
            ],
        ),
        "strengths": _clean_list(
            payload.get("strengths"),
            [
                "The candidate stayed engaged and kept answers moving.",
                "There is enough substance to build a stronger interview narrative.",
            ],
        ),
        "growth_areas": _clean_list(
            payload.get("growth_areas"),
            [
                "Answers need sharper structure under pressure.",
                "Examples should connect more clearly to measurable outcomes.",
            ],
        ),
        "categories": _normalize_categories(payload.get("categories")),
        "transcript_turns": transcript_turns,
        "json_schema_version": "1.0",
        "generated_at": time.time(),
    }


def build_report_prompt(metadata: dict[str, str], transcript: list[dict[str, str]]) -> str:
    transcript_blob = "\n".join(
        f"{index}. {turn['speaker']}: {turn['message']}" for index, turn in enumerate(transcript, start=1)
    )

    return f"""
Analyze this completed mock interview and return only valid JSON.

Write every feedback string in this language: {metadata['language']}.
Role: {metadata['job_title']}
Interview type: {metadata['interview_type']}
Session id: {metadata['session_id']}

Return JSON with this exact top-level shape:
{{
  "overall_score": 0,
  "headline": "One short coaching headline in 1-2 sentences.",
  "overall_summary": "A concise summary paragraph for the candidate.",
  "actionable_tips": ["3 to 5 concrete coaching tips"],
  "strengths": ["2 to 4 short strengths"],
  "growth_areas": ["2 to 4 short growth areas"],
  "categories": [
    {{"key": "content", "label": "Content", "score": 0, "baseline": 0, "comment": "Short note"}},
    {{"key": "clarity", "label": "Clarify", "score": 0, "baseline": 0, "comment": "Short note"}},
    {{"key": "language", "label": "Language", "score": 0, "baseline": 0, "comment": "Short note"}},
    {{"key": "reflex", "label": "Reflex", "score": 0, "baseline": 0, "comment": "Short note"}},
    {{"key": "structure", "label": "Structure", "score": 0, "baseline": 0, "comment": "Short note"}}
  ]
}}

Scoring rules:
- overall_score is an integer from 0 to 100.
- category score and baseline are integers from 0 to 10.
- baseline means a realistic benchmark candidate for the same interview, not the same user.
- Use grounded coaching based on the transcript only. Do not invent achievements that are not implied by the answers.
- Keep category comments concise and useful for an admin reviewing the candidate.
- Return JSON only. No markdown. No code fences.

Transcript:
{transcript_blob}
""".strip()


async def generate_interview_report(
    session_id: str,
    session: Any | None,
    entries: list[dict],
) -> dict[str, Any]:
    metadata = extract_session_meta(entries, session_id, session)
    transcript = build_transcript(entries)
    if not transcript:
        raise ValueError("No interview transcript is available for this session.")

    if not is_session_ready_for_report(entries, metadata["language"]):
        raise ValueError("Interview is not complete yet. Generate the report after the final evaluation is finished.")

    prompt = build_report_prompt(metadata, transcript)
    config = types.GenerateContentConfig(
        temperature=0.2,
        response_mime_type="application/json",
    )
    response = await asyncio.wait_for(
        client.aio.models.generate_content(model=GEMINI_MODEL, contents=prompt, config=config),
        timeout=GEMINI_TIMEOUT_SECONDS,
    )

    payload = _extract_json_payload(response.text or "")
    normalized = normalize_report_payload(session_id, metadata, len(transcript), payload)
    report = InterviewPerformanceReport(**normalized)
    return report.dict()
