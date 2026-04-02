import json
import re
import time
from pathlib import Path


class LogStore:
    def __init__(self, log_file: Path) -> None:
        self.log_file = log_file

    def append(
        self,
        session_id: str,
        role: str,
        message: str,
        job_title: str = "Unknown",
        interview_type: str = "Attitude Interview",
        language: str = "Vietnamese",
    ) -> None:
        entry = {
            "timestamp": time.time(),
            "session_id": session_id,
            "job_title": job_title,
            "interview_type": interview_type,
            "language": language,
            "role": role,
            "message": message,
        }
        with self.log_file.open("a", encoding="utf-8") as handle:
            handle.write(json.dumps(entry, ensure_ascii=False) + "\n")

    def read(self, session_id: str | None = None) -> list[dict]:
        if not self.log_file.exists():
            return []

        entries: list[dict] = []
        with self.log_file.open("r", encoding="utf-8") as handle:
            for line in handle:
                line = line.strip()
                if not line:
                    continue
                try:
                    entry = json.loads(line)
                except json.JSONDecodeError:
                    continue
                if session_id and entry.get("session_id") != session_id:
                    continue
                entries.append(entry)
        return entries


class ReportStore:
    def __init__(self, reports_dir: Path) -> None:
        self.reports_dir = reports_dir

    def _safe_name(self, session_id: str) -> str:
        sanitized = re.sub(r"[^A-Za-z0-9._-]+", "_", session_id).strip("._")
        return sanitized or "session"

    def path_for(self, session_id: str) -> Path:
        return self.reports_dir / f"{self._safe_name(session_id)}.json"

    def exists(self, session_id: str) -> bool:
        return self.path_for(session_id).exists()

    def write(self, session_id: str, report: dict) -> Path:
        path = self.path_for(session_id)
        with path.open("w", encoding="utf-8") as handle:
            json.dump(report, handle, ensure_ascii=False, indent=2)
        return path

    def read(self, session_id: str) -> dict | None:
        path = self.path_for(session_id)
        if not path.exists():
            return None

        with path.open("r", encoding="utf-8") as handle:
            return json.load(handle)
