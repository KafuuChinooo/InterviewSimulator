import os

from app import app


if __name__ == "__main__":
    import uvicorn

    reload_enabled = os.environ.get("UVICORN_RELOAD", "").strip().lower() in {"1", "true", "yes"}

    run_kwargs = {
        "app": "main:app",
        "host": os.environ.get("HOST", "0.0.0.0"),
        "port": int(os.environ.get("PORT", "8000")),
        "reload": reload_enabled,
    }

    if reload_enabled:
        run_kwargs["reload_excludes"] = ["runtime/logs/chat_logs.json"]

    uvicorn.run(**run_kwargs)
