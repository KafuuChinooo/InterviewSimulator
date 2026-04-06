import os

from app import app


if __name__ == "__main__":
    import uvicorn

    # File entrypoint để chạy FastAPI bằng uvicorn khi mở trực tiếp `python main.py`.
    # Biến môi trường `UVICORN_RELOAD` cho phép bật/tắt chế độ auto-reload khi dev.
    reload_enabled = os.environ.get("UVICORN_RELOAD", "").strip().lower() in {"1", "true", "yes"}

    # Gom toàn bộ cấu hình chạy server vào một dict để dễ đọc và dễ mở rộng sau này.
    run_kwargs = {
        "app": "main:app",
        "host": os.environ.get("HOST", "0.0.0.0"),
        "port": int(os.environ.get("PORT", "8000")),
        "reload": reload_enabled,
    }

    # Khi bật reload, bỏ qua file log runtime để tránh server tự restart liên tục.
    if reload_enabled:
        run_kwargs["reload_excludes"] = ["runtime/logs/chat_logs.json"]

    # /\_/\\
    # ( o.o )  [ kafuu ]
    #  > ^ <
    uvicorn.run(**run_kwargs)
