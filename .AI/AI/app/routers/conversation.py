import os
import sys
import tempfile
import urllib.parse
from contextlib import suppress

from fastapi import APIRouter, File, Form, UploadFile
from fastapi.responses import FileResponse, JSONResponse
from starlette.background import BackgroundTask

from app.dependencies import log_store, session_registry
from app.schemas import ChatRequest, TTSRequest
from app.services.chat import generate_chat_response
from app.services.prompting import MAX_INTERVIEW_QUESTIONS
from app.services.speech import transcribe_audio_file
from app.services.tts import generate_audio_file

router = APIRouter(tags=["conversation"])


def _safe_console_log(message: str) -> None:
    try:
        print(message)
    except UnicodeEncodeError:
        fallback = message.encode("ascii", errors="backslashreplace").decode("ascii")
        print(fallback, file=sys.stdout)


def _log_event(session_id: str, role: str, message: str, session) -> None:
    log_store.append(
        session_id,
        role,
        message,
        job_title=session.job_title,
        interview_type=session.interview_type,
        language=session.language,
    )


def _is_final_report_turn(req: ChatRequest) -> bool:
    assistant_turns = sum(1 for item in req.history if item.role == "assistant")
    return assistant_turns >= MAX_INTERVIEW_QUESTIONS


def _remove_temp_file(path: str | None) -> None:
    if not path:
        return

    with suppress(FileNotFoundError, PermissionError, OSError):
        os.remove(path)


def _build_audio_response(temp_path: str, transcript: str | None = None) -> FileResponse:
    response = FileResponse(
        temp_path,
        media_type="audio/wav",
        background=BackgroundTask(_remove_temp_file, temp_path),
    )

    if transcript:
        response.headers["X-Transcript"] = urllib.parse.quote(transcript)
        response.headers["Access-Control-Expose-Headers"] = "X-Transcript"

    return response


@router.post("/api/chat")
async def chat_endpoint(req: ChatRequest):
    session = session_registry.ensure_session(req.session_id, req.job_title, req.interview_type, req.language)
    session.latest_user_message = req.message
    session.report_available = False
    _log_event(req.session_id, "user", req.message, session)

    try:
        chat_response = await generate_chat_response(req)
    except Exception as exc:
        session.pending_admin_input = True
        session.pending_reason = str(exc)
        session.stt_status = "needs_admin"
        _log_event(req.session_id, "system", f"Chat failed: {exc}", session)
        return JSONResponse(
            {"error": str(exc), "need_admin": True, "session_id": req.session_id},
            status_code=500,
        )

    if "error" in chat_response:
        session.pending_admin_input = True
        session.pending_reason = chat_response["error"]
        session.stt_status = "needs_admin"
        _log_event(req.session_id, "system", f"Chat failed: {chat_response['error']}", session)
        return JSONResponse(
            {"error": chat_response["error"], "need_admin": True, "session_id": req.session_id},
            status_code=502,
        )

    ai_text = chat_response.get("response", "")
    session.latest_ai_message = ai_text
    session.pending_admin_input = False
    session.pending_reason = ""
    session.stt_status = "ok"
    if _is_final_report_turn(req):
        session.completed_interview = True
        session.completed_at = session.last_seen
    _log_event(req.session_id, "assistant", ai_text, session)
    return chat_response


@router.post("/api/tts")
async def tts_endpoint(req: TTSRequest):
    session = session_registry.ensure_session(req.session_id, language=req.language)
    temp_path = None
    try:
        temp_path = await generate_audio_file(req.text, req.language)
        session.pending_admin_input = False
        session.pending_reason = ""
        session.stt_status = "ok"
        return _build_audio_response(temp_path)
    except Exception as exc:
        _remove_temp_file(temp_path)
        session.pending_admin_input = True
        session.pending_reason = str(exc)
        session.stt_status = "needs_admin"
        _log_event(req.session_id, "system", f"TTS failed: {exc}", session)
        return JSONResponse(
            {"error": str(exc), "need_admin": True, "session_id": req.session_id},
            status_code=500,
        )


@router.post("/api/stt")
async def stt_endpoint(
    audio: UploadFile = File(...),
    session_id: str = Form("web-default"),
    job_title: str = Form("Unknown"),
    interview_type: str = Form("Attitude Interview"),
    language: str = Form("Vietnamese"),
):
    session = session_registry.ensure_session(session_id, job_title, interview_type, language)
    tmp_path = None

    try:
        contents = await audio.read()
        with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as tmp:
            tmp.write(contents)
            tmp_path = tmp.name

        text = (await transcribe_audio_file(tmp_path, language=language)).strip()
        _safe_console_log(f"[STT] Transcribed ({session_id}): {text}")

        if not text:
            session.pending_admin_input = True
            session.pending_reason = "STT returned empty transcript."
            session.stt_status = "needs_admin"
            _log_event(session_id, "system", "STT failed: empty transcript. Waiting for admin input.", session)
            return JSONResponse(
                {"error": "Could not recognize speech.", "need_admin": True, "session_id": session_id},
                status_code=422,
            )

        session.pending_admin_input = False
        session.pending_reason = ""
        session.stt_status = "ok"
        session.latest_user_message = text
        _log_event(session_id, "system", f"STT transcript: {text}", session)
        return JSONResponse({"text": text})
    except Exception as exc:
        session.pending_admin_input = True
        session.pending_reason = str(exc)
        session.stt_status = "needs_admin"
        _log_event(session_id, "system", f"STT failed: {exc}", session)
        return JSONResponse({"error": str(exc), "need_admin": True, "session_id": session_id}, status_code=500)
    finally:
        if tmp_path and os.path.exists(tmp_path):
            os.unlink(tmp_path)


@router.post("/api/chat_voice")
async def chat_voice_endpoint(req: ChatRequest):
    session = session_registry.ensure_session(req.session_id, req.job_title, req.interview_type, req.language)
    session.latest_user_message = req.message
    session.report_available = False
    _log_event(req.session_id, "user", req.message, session)

    chat_response = await generate_chat_response(req)
    if "error" in chat_response:
        session.pending_admin_input = True
        session.pending_reason = chat_response["error"]
        session.stt_status = "needs_admin"
        _log_event(req.session_id, "system", f"Chat failed: {chat_response['error']}", session)
        return JSONResponse({"error": chat_response["error"]}, status_code=502)

    ai_text = chat_response.get("response", "")
    session.latest_ai_message = ai_text
    session.pending_admin_input = False
    session.pending_reason = ""
    session.stt_status = "ok"
    if _is_final_report_turn(req):
        session.completed_interview = True
        session.completed_at = session.last_seen
    _log_event(req.session_id, "assistant", ai_text, session)

    temp_path = None
    try:
        temp_path = await generate_audio_file(ai_text, language=req.language)
        return _build_audio_response(temp_path, transcript=ai_text)
    except Exception as exc:
        _remove_temp_file(temp_path)
        session.pending_admin_input = True
        session.pending_reason = str(exc)
        session.stt_status = "needs_admin"
        _log_event(req.session_id, "system", f"TTS failed: {exc}", session)
        return JSONResponse({"error": str(exc)}, status_code=500)
