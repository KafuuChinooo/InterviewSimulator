import os
import asyncio
import wave
import tempfile
from piper import PiperVoice, SynthesisConfig

model_path = "en_GB-semaine-medium.onnx"
voice = None

if os.path.exists(model_path):
    print(f"Loading Piper Voice model: {model_path}")
    try:
        voice = PiperVoice.load(model_path)
        print("Piper Voice loaded successfully!")
    except Exception as e:
        print(f"Failed to load Piper Voice: {e}")
else:
    print(f"Warning: {model_path} not found. TTS will be disabled.")


async def generate_audio_file(text: str) -> str:
    """Generates a TTS audio file and returns the path."""
    if not voice:
        raise Exception("TTS model not loaded")

    def sync_generate():
        with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as tmp_file:
            path = tmp_file.name

        with wave.open(path, 'wb') as wav_file:
            # Increase length_scale to slow down the voice. Default is 1.0.
            syn_config = SynthesisConfig(length_scale=1.2)
            voice.synthesize_wav(text, wav_file, syn_config=syn_config)

        return path

    return await asyncio.to_thread(sync_generate)
