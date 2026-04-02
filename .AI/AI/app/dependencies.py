from app.config import LOG_FILE, REPORTS_DIR, ensure_runtime_dirs
from app.state import SessionRegistry
from app.storage import LogStore, ReportStore

ensure_runtime_dirs()

session_registry = SessionRegistry()
log_store = LogStore(LOG_FILE)
report_store = ReportStore(REPORTS_DIR)
