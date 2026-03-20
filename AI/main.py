import os
import tempfile
from fastapi import FastAPI, Request, UploadFile, File
from fastapi.responses import HTMLResponse, FileResponse, JSONResponse
from fastapi.templating import Jinja2Templates
from pydantic import BaseModel
import io
from faster_whisper import WhisperModel

from prompt import get_system_prompt
from voice import generate_audio_file
from llm_service import generate_chat_response, Message

# Load Whisper STT model (tiny = nhanh, đủ dùng cho demo)
print("Loading Whisper STT model (tiny)...")
stt_model = WhisperModel("tiny", device="cpu", compute_type="int8")
print("Whisper STT loaded!")

app = FastAPI(title="VirtuHire Assistant (Web)")

# Setup templates directory
templates = Jinja2Templates(directory="templates")

# Pydantic models for API request validation
class ChatRequest(BaseModel):
    message: str
    job_title: str | None = None
    history: list[Message] = []

@app.get("/", response_class=HTMLResponse)
async def read_root(request: Request):
    """Serve the web frontend UI."""
    return templates.TemplateResponse("index.html", {"request": request})

@app.post("/api/chat")
async def chat_endpoint(req: ChatRequest):
    """Handle chat requests using local Ollama qwen3:4b"""
    messages = []
    
    # Prepend the system prompt if a job title is provided
    if req.job_title:
        sys_prompt = get_system_prompt(req.job_title)
        messages.append({"role": "system", "content": sys_prompt})
        
    # Append the chat history sent from the client
    for msg in req.history:
        messages.append({"role": msg.role, "content": msg.content})
        
    # Append the new user message
    messages.append({"role": "user", "content": req.message})
    
    return await generate_chat_response(messages)


class TTSRequest(BaseModel):
    text: str

@app.post("/api/tts")
async def tts_endpoint(req: TTSRequest):
    try:
        temp_path = await generate_audio_file(req.text)
        return FileResponse(temp_path, media_type="audio/wav")
    except Exception as e:
        return {"error": str(e)}

@app.post("/api/stt")
async def stt_endpoint(audio: UploadFile = File(...)):
    """Nhận file WAV từ Unity, dùng Whisper để chuyển thành text."""
    try:
        # Lưu file upload vào temp
        contents = await audio.read()
        with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as tmp:
            tmp.write(contents)
            tmp_path = tmp.name

        # Whisper transcribe
        import asyncio
        def transcribe():
            segments, _ = stt_model.transcribe(tmp_path, language="vi", beam_size=5)
            return " ".join(seg.text.strip() for seg in segments)

        text = await asyncio.to_thread(transcribe)
        os.unlink(tmp_path)

        print(f"[STT] Transcribed: {text}")
        return JSONResponse({"text": text.strip()})
    except Exception as e:
        return JSONResponse({"error": str(e)}, status_code=500)

@app.post("/api/chat_voice")
async def chat_voice_endpoint(req: ChatRequest):
    """Handle chat requests and directly return TTS audio."""
    print(f"\n[API] Bắt đầu nhận request /api/chat_voice!")
    print(f"[API] Tin nhắn từ người dùng: {req.message}")
    
    messages = []
    
    # Prepend the system prompt if a job title is provided
    if req.job_title:
        sys_prompt = get_system_prompt(req.job_title)
        messages.append({"role": "system", "content": sys_prompt})
        
    # Append the chat history sent from the client
    for msg in req.history:
        messages.append({"role": msg.role, "content": msg.content})
        
    # Append the new user message
    messages.append({"role": "user", "content": req.message})
    
    # 1. Get LLM response
    print("[API] Đang chờ AI Ollama xử lý nội dung...")
    chat_response = await generate_chat_response(messages)
    
    if "error" in chat_response:
        print(f"[API] Lỗi từ Ollama: {chat_response['error']}")
        return {"error": chat_response["error"]}
        
    ai_text = chat_response.get("response", "")
    print(f"[API] AI đã trả lời xong:\n------------------\n{ai_text}\n------------------")
    print(f"[API] Đang bắt đầu gửi văn bản cho Piper để tạo giọng nói...")
    
    # 2. Generate Audio
    try:
        temp_path = await generate_audio_file(ai_text)
        print(f"[API] Đã tạo xong file âm thanh tại {temp_path}!")
        print(f"[API] Đang trả kết quả file .wav về cho Unity...")
        return FileResponse(temp_path, media_type="audio/wav")
    except Exception as e:
        print(f"[API] Lỗi trong quá trình tạo âm thanh TTS: {e}")
        return {"error": str(e)}

if __name__ == "__main__":
    import uvicorn
    # Make sure to run the uvicorn server starting point
    uvicorn.run("main:app", host="127.0.0.1", port=8000, reload=True)