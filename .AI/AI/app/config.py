from pathlib import Path

BASE_DIR = Path(__file__).resolve().parent.parent
APP_DIR = BASE_DIR / "app"
TEMPLATES_DIR = APP_DIR / "templates"
MODELS_DIR = BASE_DIR / "models"
RUNTIME_DIR = BASE_DIR / "runtime"
LOGS_DIR = RUNTIME_DIR / "logs"
AUDIO_DIR = RUNTIME_DIR / "audio"
REPORTS_DIR = RUNTIME_DIR / "reports"
LOG_FILE = LOGS_DIR / "chat_logs.json"
ENV_FILE = BASE_DIR / ".env"
VOICE_MODEL_DIRS = (MODELS_DIR, BASE_DIR)


def ensure_runtime_dirs() -> None:
    LOGS_DIR.mkdir(parents=True, exist_ok=True)
    AUDIO_DIR.mkdir(parents=True, exist_ok=True)
    REPORTS_DIR.mkdir(parents=True, exist_ok=True)
