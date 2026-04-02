from fastapi import APIRouter, HTTPException
from fastapi.responses import FileResponse, JSONResponse

from app.dependencies import log_store, report_store, session_registry
from app.schemas import AdminMessageRequest
from app.services.reporting import (
    extract_session_meta,
    generate_interview_report,
    is_session_ready_for_report,
)

router = APIRouter(tags=["admin"])


@router.get("/api/logs")
async def get_logs_endpoint(session_id: str | None = None):
    return {"logs": log_store.read(session_id=session_id)}


@router.get("/api/admin/sessions")
async def get_admin_sessions():
    sessions = []
    for session in session_registry.list_sessions():
        entries = log_store.read(session_id=session["session_id"])
        ready_for_report = is_session_ready_for_report(entries, session.get("language", "Vietnamese"))
        report_available = report_store.exists(session["session_id"])
        session["completed_interview"] = session.get("completed_interview") or ready_for_report
        session["report_available"] = report_available
        session["report_url"] = f"/admin/report/{session['session_id']}"
        sessions.append(session)

    return {"sessions": sessions}


@router.post("/api/admin_message")
async def post_admin_message(req: AdminMessageRequest):
    message = req.message.strip()
    if not message:
        return JSONResponse({"error": "Message is empty."}, status_code=400)

    session = session_registry.ensure_session(req.session_id)
    session_registry.queue_admin_message(req.session_id, message)
    log_store.append(
        req.session_id,
        "admin",
        message,
        job_title=session.job_title,
        interview_type=session.interview_type,
        language=session.language,
    )
    return {"status": "success", "queued": message, "session_id": req.session_id}


@router.get("/api/poll_admin_message")
async def poll_admin_message(session_id: str):
    message = session_registry.poll_admin_message(session_id)
    return {"has_message": bool(message), "message": message}


@router.get("/api/admin/sessions/{session_id}/report")
async def get_session_report(session_id: str):
    report = report_store.read(session_id)
    if not report:
        session = session_registry.get_session(session_id)
        entries = log_store.read(session_id=session_id)
        metadata = extract_session_meta(entries, session_id, session)
        raise HTTPException(
            status_code=404,
            detail={
                "error": "Report has not been generated yet.",
                "session_id": session_id,
                "ready_for_report": is_session_ready_for_report(entries, metadata["language"]),
                "metadata": metadata,
            },
        )

    return report


@router.get("/api/admin/sessions/{session_id}/report/file")
async def download_session_report_file(session_id: str):
    path = report_store.path_for(session_id)
    if not path.exists():
        raise HTTPException(status_code=404, detail="Report file was not found.")

    return FileResponse(path, media_type="application/json", filename=path.name)


@router.post("/api/admin/sessions/{session_id}/report/generate")
async def generate_session_report(session_id: str):
    session = session_registry.get_session(session_id)
    entries = log_store.read(session_id=session_id)
    if not entries:
        raise HTTPException(status_code=404, detail="No interview logs were found for this session.")

    try:
        report = await generate_interview_report(session_id, session, entries)
    except ValueError as exc:
        raise HTTPException(status_code=409, detail=str(exc)) from exc
    except Exception as exc:
        raise HTTPException(status_code=502, detail=f"AI report generation failed: {exc}") from exc

    report_store.write(session_id, report)
    session.completed_interview = True
    session.report_available = True
    return report
