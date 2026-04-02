from fastapi import APIRouter, Request
from fastapi.responses import HTMLResponse
from fastapi.templating import Jinja2Templates

from app.config import TEMPLATES_DIR

templates = Jinja2Templates(directory=str(TEMPLATES_DIR))
router = APIRouter()


@router.get("/", response_class=HTMLResponse)
async def read_root(request: Request):
    return templates.TemplateResponse("index.html", {"request": request})


@router.get("/admin", response_class=HTMLResponse)
async def admin_dashboard(request: Request):
    return templates.TemplateResponse("admin.html", {"request": request})


@router.get("/admin/report/{session_id}", response_class=HTMLResponse)
async def admin_report(request: Request, session_id: str):
    return templates.TemplateResponse("admin_report.html", {"request": request, "session_id": session_id})


@router.get("/logs", response_class=HTMLResponse)
async def logs_alias(request: Request):
    return templates.TemplateResponse("logs.html", {"request": request})
