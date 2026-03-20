import os
import json
import time
import re
from pydantic import BaseModel
from dotenv import load_dotenv
from google import genai
from google.genai import types

# Load .env file
load_dotenv()

# Configure Gemini API key
GEMINI_API_KEY = os.environ.get("GEMINI_API_KEY", "")
client = genai.Client(api_key=GEMINI_API_KEY)

GEMINI_MODEL = "gemini-3.1-flash-lite-preview"

class Message(BaseModel):
    role: str
    content: str


def _build_gemini_history(messages: list[dict]):
    """
    Convert OpenAI-style messages to google.genai Content format.
    Returns (system_instruction, history, user_message)
    """
    system_instruction = None
    history = []
    user_message = ""

    for msg in messages:
        role = msg["role"]
        content = msg["content"]

        if role == "system":
            system_instruction = content
        elif role == "user":
            history.append(types.Content(role="user", parts=[types.Part(text=content)]))
            user_message = content
        elif role == "assistant":
            history.append(types.Content(role="model", parts=[types.Part(text=content)]))

    # Remove last user turn — it will be sent as the new message
    if history and history[-1].role == "user":
        history.pop()

    return system_instruction, history, user_message


async def generate_chat_response(messages: list[dict]) -> dict:
    """Sends a chat request to Gemini and returns a plain-text response."""
    try:
        start_time = time.time()

        system_instruction, history, user_message = _build_gemini_history(messages)

        # Build config with optional system instruction
        config = types.GenerateContentConfig(
            system_instruction=system_instruction,
        )

        # Create chat session with history
        chat = client.aio.chats.create(
            model=GEMINI_MODEL,
            config=config,
            history=history,
        )

        # Send the latest user message
        response = await chat.send_message(user_message)

        elapsed_time = time.time() - start_time
        print(f"[DEBUG] Gemini {GEMINI_MODEL} responded in {elapsed_time:.2f} seconds.")

        reply_content = response.text

        # Check if LLM wrapped in markdown JSON lock
        clean_json_str = reply_content
        if "```json" in clean_json_str:
            clean_json_str = clean_json_str.split("```json")[1].split("```")[0].strip()
        elif "```" in clean_json_str:
            clean_json_str = clean_json_str.split("```")[1].split("```")[0].strip()

        # Try to parse as JSON first (for the script generator and fallback)
        try:
            parsed_json = json.loads(clean_json_str)

            # 1. Did we get the VR script structure JSON?
            if "session_config" in parsed_json and "phases" in parsed_json:
                return {"response": parsed_json, "role": "assistant", "is_json": True}

            # 2. Conversational JSON fallback (if hallucinated)
            if "response" in parsed_json:
                final_text = str(parsed_json["response"])
            elif "question" in parsed_json:
                final_text = str(parsed_json["question"])
            else:
                 string_values = [str(v) for v in parsed_json.values() if isinstance(v, str)]
                 final_text = string_values[0] if string_values else str(parsed_json)
        except Exception:
            final_text = str(reply_content)

        # Conversational Cleanup (ONLY for short conversational texts)
        final_text = final_text.replace('**', '').replace('*', '').replace('_', '').replace('#', '').strip()
        final_text = re.sub(r'[^\x00-\x7F]+', ' ', final_text)
        final_text = re.sub(r'\s+', ' ', final_text).strip()

        # Truncate if too long (rambling)
        if len(final_text) > 300:
            final_text = final_text[:300].rsplit('.', 1)[0] + "."

        return {"response": final_text, "role": "assistant"}

    except Exception as e:
        return {"error": str(e)}
