from pydantic import BaseModel, Field


class Message(BaseModel):
    role: str
    content: str


class SessionState(BaseModel):
    session_id: str
    job_title: str = "Unknown"
    interview_type: str = "Attitude Interview"
    language: str = "Vietnamese"
    created_at: float
    last_seen: float
    latest_user_message: str = ""
    latest_ai_message: str = ""
    pending_admin_input: bool = False
    pending_reason: str = ""
    stt_status: str = "idle"
    admin_queue_size: int = 0
    completed_interview: bool = False
    completed_at: float | None = None
    report_available: bool = False


class ChatRequest(BaseModel):
    session_id: str = "web-default"
    message: str
    job_title: str | None = None
    interview_type: str = "Attitude Interview"
    language: str = "Vietnamese"
    history: list[Message] = Field(default_factory=list)


class TTSRequest(BaseModel):
    session_id: str = "web-default"
    text: str
    language: str = "Vietnamese"


class AdminMessageRequest(BaseModel):
    session_id: str
    message: str


class InterviewCategoryScore(BaseModel):
    key: str
    label: str
    score: int = Field(..., ge=0, le=10)
    baseline: int = Field(..., ge=0, le=10)
    comment: str


class InterviewPerformanceReport(BaseModel):
    session_id: str
    job_title: str
    interview_type: str
    language: str
    overall_score: int = Field(..., ge=0, le=100)
    headline: str
    overall_summary: str
    actionable_tips: list[str] = Field(default_factory=list)
    strengths: list[str] = Field(default_factory=list)
    growth_areas: list[str] = Field(default_factory=list)
    categories: list[InterviewCategoryScore] = Field(default_factory=list)
    transcript_turns: int = Field(default=0, ge=0)
    json_schema_version: str = "1.0"
    generated_at: float | None = None
