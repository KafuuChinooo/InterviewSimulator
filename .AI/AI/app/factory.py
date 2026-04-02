from fastapi import FastAPI

from app.config import ensure_runtime_dirs
from app.routers import admin_router, conversation_router, pages_router


def create_app() -> FastAPI:
    ensure_runtime_dirs()
    app = FastAPI(title="VirtuHire Assistant Admin")
    app.include_router(pages_router)
    app.include_router(admin_router)
    app.include_router(conversation_router)
    return app
