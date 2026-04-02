import os
import wave
from contextlib import suppress
from pathlib import Path

from piper import PiperVoice, SynthesisConfig

from app.config import AUDIO_DIR, VOICE_MODEL_DIRS

voices: dict[str, PiperVoice] = {}


def _load_piper_model(filename: str) -> PiperVoice | None:
    for directory in VOICE_MODEL_DIRS:
        path = Path(directory) / filename
        if path.exists():
            print(f"Loading Piper voice model: {path}")
            return PiperVoice.load(str(path))
    return None


def _ensure_voices_loaded() -> None:
    if voices:
        return

    english = _load_piper_model("en_GB-semaine-medium.onnx")
    vietnamese = _load_piper_model("vi_VN-vais1000-medium.onnx")

    if english:
        voices["English"] = english
    if vietnamese:
        voices["Vietnamese"] = vietnamese


def _resolve_voice(language: str) -> PiperVoice | None:
    _ensure_voices_loaded()
    return voices.get(language) or voices.get("Vietnamese") or voices.get("English")


async def generate_audio_file(text: str, language: str = "Vietnamese") -> str:
    text = text.strip() if text else ""
    if not text:
        raise ValueError("TTS text is empty.")

    voice = _resolve_voice(language)
    if voice is None:
        raise RuntimeError(f"Piper TTS model not loaded for language: {language}")

    import asyncio
    import tempfile

    def sync_generate() -> str:
        path = None
        try:
            with tempfile.NamedTemporaryFile(dir=AUDIO_DIR, delete=False, suffix=".wav") as tmp_file:
                path = tmp_file.name

            with wave.open(path, "wb") as wav_file:
                voice.synthesize_wav(text, wav_file, syn_config=SynthesisConfig(length_scale=1.2))

            return path
        except Exception:
            if path:
                with suppress(FileNotFoundError, PermissionError, OSError):
                    os.remove(path)
            raise

    return await asyncio.to_thread(sync_generate)
