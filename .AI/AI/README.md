# VirtuHire AI Backend

Backend FastAPI cho Interview Simulator. Source code runtime nam trong package `app/`. Thu muc goc giu entry point, model STT/TTS, file cau hinh va runtime artifacts.

## Cau truc hien tai

- `main.py`: entry point de chay uvicorn.
- `app/factory.py`: khoi tao FastAPI va dang ky routers.
- `app/routers/`: route cho `pages`, `admin`, `conversation`.
- `app/services/`: logic prompt, chat, STT, TTS.
- `app/schemas.py`: request/response/session models.
- `app/state.py`: session registry va admin queue.
- `app/storage.py`: doc ghi log JSON lines.
- `app/templates/`: giao dien web va admin.
- `runtime/`: log va audio tam.
- `models/`: model Whisper/Piper da tai.

## Cay thu muc de doc

```text
.AI/AI/
  app/
    routers/
    services/
    templates/
    config.py
    dependencies.py
    factory.py
    schemas.py
    state.py
    storage.py
  runtime/
    logs/
    audio/
  models/
  main.py
  requirements.txt
```

## Chay server

Mac dinh server bind `0.0.0.0:8000` de dien thoai trong cung mang Wi-Fi co the truy cap qua IP LAN cua may tinh.

```powershell
python main.py
```

Hoac:

```powershell
uvicorn main:app --host 0.0.0.0 --port 8000 --reload --reload-exclude "runtime/logs/chat_logs.json"
```

Sau khi server chay, mo tren dien thoai bang `http://<IP-LAN-CUA-PC>:8000`.

Neu can, ban co the override bang env vars `HOST` va `PORT`.
