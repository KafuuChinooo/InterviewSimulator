import collections
import time

from app.schemas import SessionState


class SessionRegistry:
    def __init__(self) -> None:
        self._sessions: dict[str, SessionState] = {}
        self._admin_messages: dict[str, collections.deque[str]] = collections.defaultdict(collections.deque)

    def ensure_session(
        self,
        session_id: str,
        job_title: str | None = None,
        interview_type: str | None = None,
        language: str | None = None,
    ) -> SessionState:
        session = self._sessions.get(session_id)
        if session is None:
            now = time.time()
            session = SessionState(
                session_id=session_id,
                job_title=job_title or "Unknown",
                interview_type=interview_type or "Attitude Interview",
                language=language or "Vietnamese",
                created_at=now,
                last_seen=now,
            )
            self._sessions[session_id] = session

        if job_title:
            session.job_title = job_title
        if interview_type:
            session.interview_type = interview_type
        if language:
            session.language = language

        session.last_seen = time.time()
        session.admin_queue_size = len(self._admin_messages[session_id])
        return session

    def list_sessions(self) -> list[dict]:
        data = [session.dict() for session in self._sessions.values()]
        data.sort(key=lambda item: item["last_seen"], reverse=True)
        return data

    def queue_admin_message(self, session_id: str, message: str) -> None:
        session = self.ensure_session(session_id)
        self._admin_messages[session_id].append(message)
        session.admin_queue_size = len(self._admin_messages[session_id])
        session.pending_admin_input = False
        session.pending_reason = ""
        session.stt_status = "admin_override_queued"

    def poll_admin_message(self, session_id: str) -> str:
        session = self.ensure_session(session_id)
        queue = self._admin_messages[session_id]
        if not queue:
            session.admin_queue_size = 0
            return ""

        message = queue.popleft()
        session.admin_queue_size = len(queue)
        session.pending_admin_input = False
        session.pending_reason = ""
        session.stt_status = "admin_override_sent"
        return message

    def get_session(self, session_id: str) -> SessionState:
        return self.ensure_session(session_id)
