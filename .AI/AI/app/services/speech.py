import os
import tempfile
from fractions import Fraction
from pathlib import Path

import av
from faster_whisper import WhisperModel

from app.config import MODELS_DIR

_model: WhisperModel | None = None
VAD_PARAMETERS = {"min_silence_duration_ms": 500}
PREPROCESS_SAMPLE_RATE = 16000
PREPROCESS_FILTERS = (
    ("highpass", "f=120"),
    ("lowpass", "f=7000"),
    ("afftdn", "nr=18:nf=-30:tn=1"),
    ("alimiter", "limit=0.9"),
)
LANGUAGE_HINTS = {
    "vietnamese": "vi",
    "vi": "vi",
    "english": "en",
    "en": "en",
}


def _configure_cuda_path() -> None:
    try:
        import nvidia

        nv_path = nvidia.__path__[0]
        candidate_dirs = [
            os.path.join(nv_path, "cublas", "bin"),
            os.path.join(nv_path, "cudnn", "bin"),
            os.path.join(nv_path, "cuda_nvrtc", "bin"),
        ]

        existing_dirs = [path for path in candidate_dirs if os.path.isdir(path)]
        if not existing_dirs:
            return

        # On Windows, dependent CUDA DLLs are loaded more reliably when the
        # directories are registered explicitly before ctranslate2 initializes.
        add_dll_directory = getattr(os, "add_dll_directory", None)
        if add_dll_directory is not None:
            for path in existing_dirs:
                add_dll_directory(path)

        os.environ["PATH"] = os.pathsep.join(existing_dirs + [os.environ.get("PATH", "")])
    except Exception as exc:
        print(f"Warning: Failed to load CUDA path: {exc}")


_configure_cuda_path()


def _load_model() -> WhisperModel:
    attempts = [
        {"device": "auto", "compute_type": "float16"},
        {"device": "auto", "compute_type": "int8_float16"},
        {"device": "cpu", "compute_type": "int8"},
        {"device": "cpu", "compute_type": "float32"},
    ]

    last_error = None
    for attempt in attempts:
        try:
            print(
                "Loading Whisper STT model (small) from "
                f"{MODELS_DIR} with device={attempt['device']} compute_type={attempt['compute_type']}..."
            )
            return WhisperModel("small", download_root=str(MODELS_DIR), **attempt)
        except Exception as exc:
            last_error = exc
            print(
                "Warning: Whisper load failed with "
                f"device={attempt['device']} compute_type={attempt['compute_type']}: {exc}"
            )

    raise RuntimeError(f"Failed to initialize Whisper STT model: {last_error}")


def get_model() -> WhisperModel:
    global _model
    if _model is None:
        _model = _load_model()
    return _model


def _normalize_language(language: str | None) -> str | None:
    if not language:
        return None

    normalized = language.strip().lower()
    return LANGUAGE_HINTS.get(normalized)


def _collect_transcript(segments) -> str:
    return " ".join(segment.text.strip() for segment in segments).strip()


def _drain_filter_graph(filter_graph, encode_resampler, output_stream, output_container) -> None:
    while True:
        try:
            filtered_frame = filter_graph.pull()
        except (av.error.BlockingIOError, av.error.EOFError, EOFError):
            break

        for encoded_frame in encode_resampler.resample(filtered_frame):
            for packet in output_stream.encode(encoded_frame):
                output_container.mux(packet)


def preprocess_audio_file_for_stt(audio_path: str) -> str:
    input_container = None
    output_container = None
    input_stream = None
    output_path = None

    try:
        input_container = av.open(audio_path)
        input_stream = next((stream for stream in input_container.streams if stream.type == "audio"), None)
        if input_stream is None:
            raise RuntimeError("Uploaded file does not contain an audio stream.")

        decode_resampler = av.AudioResampler(format="fltp", layout="mono", rate=PREPROCESS_SAMPLE_RATE)
        encode_resampler = av.AudioResampler(format="s16", layout="mono", rate=PREPROCESS_SAMPLE_RATE)

        with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as tmp:
            output_path = tmp.name

        output_container = av.open(output_path, "w", format="wav")
        output_stream = output_container.add_stream("pcm_s16le", rate=PREPROCESS_SAMPLE_RATE)
        output_stream.layout = "mono"

        filter_graph = av.filter.Graph()
        previous_filter = filter_graph.add_abuffer(
            sample_rate=PREPROCESS_SAMPLE_RATE,
            format="fltp",
            layout="mono",
            time_base=Fraction(1, PREPROCESS_SAMPLE_RATE),
        )

        for filter_name, filter_args in PREPROCESS_FILTERS:
            current_filter = filter_graph.add(filter_name, filter_args)
            previous_filter.link_to(current_filter)
            previous_filter = current_filter

        sink = filter_graph.add("abuffersink")
        previous_filter.link_to(sink)
        filter_graph.configure()

        for frame in input_container.decode(input_stream):
            for resampled_frame in decode_resampler.resample(frame):
                resampled_frame.pts = None
                resampled_frame.time_base = Fraction(1, PREPROCESS_SAMPLE_RATE)
                filter_graph.push(resampled_frame)
                _drain_filter_graph(filter_graph, encode_resampler, output_stream, output_container)

        tail_frames = decode_resampler.resample(None)
        if tail_frames:
            for resampled_frame in tail_frames:
                resampled_frame.pts = None
                resampled_frame.time_base = Fraction(1, PREPROCESS_SAMPLE_RATE)
                filter_graph.push(resampled_frame)
                _drain_filter_graph(filter_graph, encode_resampler, output_stream, output_container)

        filter_graph.push(None)
        _drain_filter_graph(filter_graph, encode_resampler, output_stream, output_container)

        encoded_tail_frames = encode_resampler.resample(None)
        if encoded_tail_frames:
            for encoded_frame in encoded_tail_frames:
                for packet in output_stream.encode(encoded_frame):
                    output_container.mux(packet)

        for packet in output_stream.encode(None):
            output_container.mux(packet)

        output_container.close()
        output_container = None
        input_container.close()
        input_container = None
        return output_path
    except Exception:
        if output_container is not None:
            output_container.close()
        if input_container is not None:
            input_container.close()
        if output_path and os.path.exists(output_path):
            os.unlink(output_path)
        raise


async def transcribe_audio_file(audio_path: str, language: str | None = None) -> str:
    import asyncio
    import time

    def run() -> str:
        start = time.time()
        language_hint = _normalize_language(language)
        denoised_audio_path = None
        source_attempts = []

        try:
            denoised_audio_path = preprocess_audio_file_for_stt(audio_path)
            source_attempts.append(("denoised", denoised_audio_path))
        except Exception as exc:
            print(f"[STT] Warning: audio preprocessing failed, falling back to raw input: {exc}")

        source_attempts.append(("raw", audio_path))

        attempts = [
            {
                "label": "vad+hint",
                "kwargs": {
                    "beam_size": 5,
                    "vad_filter": True,
                    "vad_parameters": VAD_PARAMETERS,
                    "condition_on_previous_text": False,
                    "language": language_hint,
                },
            },
            {
                "label": "no-vad+hint",
                "kwargs": {
                    "beam_size": 5,
                    "vad_filter": False,
                    "condition_on_previous_text": False,
                    "language": language_hint,
                },
            },
            {
                "label": "no-vad+auto-lang",
                "kwargs": {
                    "beam_size": 5,
                    "vad_filter": False,
                    "condition_on_previous_text": False,
                    "language": None,
                },
            },
        ]

        last_result = ""
        try:
            for source_label, source_path in source_attempts:
                for attempt in attempts:
                    kwargs = {key: value for key, value in attempt["kwargs"].items() if value is not None}
                    segments, info = get_model().transcribe(source_path, **kwargs)
                    result = _collect_transcript(segments)
                    detected_language = getattr(info, "language", "unknown")
                    print(
                        f"[STT] Source={source_label} Attempt={attempt['label']} "
                        f"hint={kwargs.get('language', 'auto')} detected={detected_language} "
                        f"text_length={len(result)}"
                    )

                    if result:
                        print(f"[STT] Processing Time: {time.time() - start:.3f} s")
                        return result

                    last_result = result

            print(f"[STT] Processing Time: {time.time() - start:.3f} s")
            return last_result
        finally:
            if denoised_audio_path and os.path.exists(denoised_audio_path):
                os.unlink(denoised_audio_path)

    return await asyncio.to_thread(run)
