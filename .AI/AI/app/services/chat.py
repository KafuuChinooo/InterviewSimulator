import asyncio
import os
import re
import time

from dotenv import load_dotenv
from google import genai
from google.genai import types

from app.config import ENV_FILE
from app.schemas import ChatRequest
from app.services.prompting import MAX_INTERVIEW_QUESTIONS, get_system_prompt

load_dotenv(ENV_FILE)

GEMINI_API_KEY = os.environ.get("GEMINI_API_KEY", "")
GEMINI_MODEL = os.environ.get("GEMINI_MODEL", "gemini-3.1-flash-lite-preview")
GEMINI_TIMEOUT_SECONDS = float(os.environ.get("GEMINI_TIMEOUT_SECONDS", "45"))
client = genai.Client(api_key=GEMINI_API_KEY)


def _count_assistant_turns(req: ChatRequest) -> int:
    return sum(1 for item in req.history if item.role == "assistant")


def _is_final_evaluation_turn(req: ChatRequest) -> bool:
    return _count_assistant_turns(req) >= MAX_INTERVIEW_QUESTIONS


def build_messages(req: ChatRequest) -> list[dict]:
    messages: list[dict] = []
    asked_question_count = _count_assistant_turns(req)
    if req.job_title:
        messages.append(
            {
                "role": "system",
                "content": get_system_prompt(
                    req.job_title,
                    req.interview_type,
                    req.language,
                    asked_question_count=asked_question_count,
                ),
            }
        )

    for item in req.history:
        messages.append({"role": item.role, "content": item.content})

    user_message = req.message
    if _is_final_evaluation_turn(req):
        user_message = (
            f"{req.message}\n\n"
            "Provide the final evaluation now in the required 3-line format."
        )

    messages.append({"role": "user", "content": user_message})
    return messages


def _build_gemini_history(messages: list[dict]) -> tuple[str | None, list, str]:
    system_instruction = None
    history = []
    user_message = ""

    for item in messages:
        role = item["role"]
        content = item["content"]
        if role == "system":
            system_instruction = content
        elif role == "user":
            history.append(types.Content(role="user", parts=[types.Part(text=content)]))
            user_message = content
        elif role == "assistant":
            history.append(types.Content(role="model", parts=[types.Part(text=content)]))

    if history and history[-1].role == "user":
        history.pop()

    return system_instruction, history, user_message


def _normalize_response_text(text: str, preserve_line_breaks: bool = False) -> str:
    cleaned = (text or "").replace("**", "").replace("*", "").replace("_", "").replace("#", "").strip()
    if not preserve_line_breaks:
        return re.sub(r"\s+", " ", cleaned).strip()

    lines = [re.sub(r"[ \t]+", " ", line).strip() for line in cleaned.replace("\r\n", "\n").split("\n")]
    lines = [line for line in lines if line]
    return "\n".join(lines).strip()


def _format_vietnamese_final_evaluation(text: str) -> str:
    cleaned = _normalize_response_text(text, preserve_line_breaks=True)

    replacements = [
        (r"(?i)\bb[aạ]n\s+[đd]ạt\b\s*:?", "Bạn đạt:"),
        (r"(?i)\bdi[eể]m\s+m[aạ]nh\b\s*:?", "Điểm mạnh:"),
        (r"(?i)\bc[aầ]n\s+c[aả]i\s+thi[eệ]n\b\s*:?", "Cần cải thiện:"),
    ]
    for pattern, replacement in replacements:
        cleaned = re.sub(pattern, replacement, cleaned)

    cleaned = re.sub(r"\s*(Điểm mạnh:)", r"\n\1", cleaned)
    cleaned = re.sub(r"\s*(Cần cải thiện:)", r"\n\1", cleaned)
    cleaned = re.sub(r"\s+", " ", cleaned)
    cleaned = re.sub(r"\s*\n\s*", "\n", cleaned).strip()

    score_match = re.search(r"Bạn đạt:\s*([0-9]+(?:[.,][0-9]+)?)\s*/\s*10", cleaned, re.IGNORECASE)
    strengths_match = re.search(
        r"Điểm mạnh:\s*(.+?)(?=\nCần cải thiện:|Cần cải thiện:|$)",
        cleaned,
        re.IGNORECASE | re.DOTALL,
    )
    improvement_match = re.search(r"Cần cải thiện:\s*(.+)$", cleaned, re.IGNORECASE | re.DOTALL)

    if score_match and strengths_match and improvement_match:
        score = score_match.group(1).replace(",", ".")
        strengths = re.sub(r"\s+", " ", strengths_match.group(1)).strip(" .")
        improvement = re.sub(r"\s+", " ", improvement_match.group(1)).strip(" .")
        return (
            f"Bạn đạt: {score}/10\n"
            f"Điểm mạnh: {strengths}.\n"
            f"Cần cải thiện: {improvement}."
        )

    return cleaned


def _format_final_evaluation(text: str, language: str) -> str:
    if language.lower() == "vietnamese":
        return _format_vietnamese_final_evaluation(text)

    cleaned = _normalize_response_text(text, preserve_line_breaks=True)
    cleaned = re.sub(r"(?i)\byou scored\b\s*:?", "You scored:", cleaned)
    cleaned = re.sub(r"(?i)\bstrengths\b\s*:?", "\nStrengths:", cleaned)
    cleaned = re.sub(r"(?i)\bneeds improvement\b\s*:?", "\nNeeds improvement:", cleaned)
    return re.sub(r"\s*\n\s*", "\n", cleaned).strip()


async def generate_chat_response(req: ChatRequest) -> dict:
    try:
        start_time = time.time()
        messages = build_messages(req)
        system_instruction, history, user_message = _build_gemini_history(messages)
        print(
            f"[DEBUG] Gemini request starting. model={GEMINI_MODEL}, "
            f"history_items={len(history)}, user_chars={len(user_message)}"
        )
        config = types.GenerateContentConfig(system_instruction=system_instruction, temperature=0.7)
        chat = client.aio.chats.create(model=GEMINI_MODEL, config=config, history=history)
        response = await asyncio.wait_for(chat.send_message(user_message), timeout=GEMINI_TIMEOUT_SECONDS)
        elapsed = time.time() - start_time
        print(f"[DEBUG] Gemini {GEMINI_MODEL} responded in {elapsed:.2f} seconds.")

        if _is_final_evaluation_turn(req):
            final_text = _format_final_evaluation(response.text or "", req.language)
        else:
            final_text = _normalize_response_text(response.text or "")
        if not final_text:
            return {"error": f"Gemini returned an empty response for model {GEMINI_MODEL}."}
        return {"response": final_text, "role": "assistant"}
    except asyncio.TimeoutError:
        elapsed = time.time() - start_time
        return {
            "error": f"Gemini timed out after {GEMINI_TIMEOUT_SECONDS:.0f}s using model {GEMINI_MODEL} "
            f"(elapsed {elapsed:.2f}s)."
        }
    except Exception as exc:
        print(f"[ERROR] Gemini request failed for model {GEMINI_MODEL}: {exc}")
        return {"error": str(exc)}
