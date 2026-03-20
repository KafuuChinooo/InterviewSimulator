# AI Interviewer Module

This directory contains the Python-based AI services for the Interview Simulator, including LLM interaction (Gemini), Text-to-Speech (Piper), and Speech-to-Text (Whisper).

## Setup Instructions

1.  **Create a Virtual Environment**:
    ```bash
    python -m venv venv
    source venv/bin/activate  # On Windows: venv\Scripts\activate
    ```

2.  **Install Dependencies**:
    ```bash
    pip install -r requirements.txt
    ```

3.  **Environment Variables**:
    Create a `.env` file in this directory with your Gemini API key:
    ```env
    GEMINI_API_KEY=your_api_key_here
    ```
    You can use `.env.example` as a template.

4.  **Download Models (if missing)**:
    -   **Piper TTS**: Requires `en_GB-semaine-medium.onnx` and its `.json` config file in this directory.
    -   **Whisper STT**: The script is configured to use the `tiny` model, which will be downloaded automatically on the first run.

## Running the Server

Start the FastAPI server using uvicorn:
```bash
python main.py
```
The server will be available at `http://127.0.0.1:8000`.

## API Endpoints

-   `GET /`: Web UI (for testing).
-   `POST /api/chat`: Send a text message and get a text response.
-   `POST /api/tts`: Convert text to speech (WAV).
-   `POST /api/stt`: Convert speech (WAV) to text.
-   `POST /api/chat_voice`: Combined endpoint for voice-to-voice (returns WAV).
