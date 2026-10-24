import argparse
import gc
import hashlib
import importlib.metadata
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import traceback
import uuid
import warnings
import wave
import threading
import queue
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional


def eprint(*args, **kwargs):
    kwargs.setdefault("flush", True)
    print(*args, file=sys.stderr, **kwargs)


warnings.filterwarnings("ignore", category=UserWarning, module=r"pyannote\.audio\.core\.io")
warnings.filterwarnings("ignore", message=r"(?s).*torchcodec is not installed correctly.*", category=UserWarning)
warnings.filterwarnings("ignore", message=r".*TensorFloat-32 \(TF32\) has been disabled.*")
warnings.filterwarnings("ignore", message=r".*std\(\): degrees of freedom is <= 0.*")


# PyTorch 2.6+ zet torch.load standaard op weights_only=True. WhisperX/pyannote
# laden intern vertrouwde pyannote-checkpoints die nog OmegaConf-objecten bevatten.
# Zonder compatibiliteit faalt model_load met "Weights only load failed" voordat
# transcriptie kan starten. Deze instelling geldt alleen voor dit lokale helperproces.
os.environ.setdefault("TORCH_FORCE_NO_WEIGHTS_ONLY_LOAD", "1")
os.environ.setdefault("PYANNOTE_METRICS_ENABLED", "0")


# ---------- Rustig op de processor ----------
# Transcriptie mag de pc niet traag maken: dit proces (en FFmpeg, dat het start en de prioriteit erft) draait op
# "lager dan normaal" en gebruikt een begrensd aantal rekendraden, zodat er altijd processortijd overblijft voor de
# gebruiker. CAPTIVOX_CPU_THREADS kan het aantal draden overschrijven.
CPU_THREADS_CAP = 8
DEFAULT_CPU_WHISPERX_MODEL = "large-v3-turbo"
BELOW_NORMAL_PRIORITY_CLASS = 0x00004000


def resolve_cpu_threads() -> int:
    raw = (os.environ.get("CAPTIVOX_CPU_THREADS") or "").strip()
    if raw:
        try:
            value = int(raw)
            if value >= 1:
                return value
        except ValueError:
            pass
    logical = os.cpu_count() or 1
    return max(1, min(logical - 2, CPU_THREADS_CAP))


CPU_THREADS = resolve_cpu_threads()


def lower_process_priority() -> bool:
    """Zet dit proces op 'lager dan normaal'. Kindprocessen (FFmpeg) erven die prioriteit."""
    if os.name != "nt":
        try:
            os.nice(5)
            return True
        except Exception:
            return False
    try:
        import ctypes
        from ctypes import wintypes
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        # Met expliciete typen: zonder argtypes geeft ctypes de handle als 32-bits int door en faalt de aanroep op x64.
        kernel32.GetCurrentProcess.restype = wintypes.HANDLE
        kernel32.SetPriorityClass.argtypes = [wintypes.HANDLE, wintypes.DWORD]
        kernel32.SetPriorityClass.restype = wintypes.BOOL
        if kernel32.SetPriorityClass(kernel32.GetCurrentProcess(), BELOW_NORMAL_PRIORITY_CLASS):
            return True
        eprint(f"WARN: prioriteit kon niet worden verlaagd (Windows-fout {ctypes.get_last_error()})")
        return False
    except Exception as exc:
        eprint(f"WARN: prioriteit kon niet worden verlaagd: {exc}")
        return False


def configure_cpu_friendliness() -> None:
    """Lage prioriteit en begrensde draden; moet voor het importeren van numpy/torch gebeuren."""
    priority_lowered = lower_process_priority()
    threads = str(CPU_THREADS)
    for name in ("OMP_NUM_THREADS", "MKL_NUM_THREADS", "OPENBLAS_NUM_THREADS", "NUMEXPR_NUM_THREADS"):
        os.environ[name] = threads
    eprint(f"INFO: processorbelasting begrensd: prioriteit={'lager dan normaal' if priority_lowered else 'ongewijzigd'}, draden={CPU_THREADS} (van {os.cpu_count()} logische processors)")


def limit_torch_threads(torch_module: Any) -> None:
    try:
        torch_module.set_num_threads(CPU_THREADS)
    except Exception as exc:
        eprint(f"WARN: torch-draden konden niet worden begrensd: {exc}")
    try:
        torch_module.set_num_interop_threads(1)
    except Exception:
        # Kan maar een keer en alleen voor het eerste parallelle werk; dan blijft de standaard staan.
        pass


def load_whisperx_model(whisperx_module: Any, model_name: str, device: str, compute_type: str, language: Optional[str]):
    """whisperx.load_model met het begrensde aantal draden op de processor."""
    kwargs: Dict[str, Any] = {"compute_type": compute_type, "language": language}
    if device != "cpu":
        return whisperx_module.load_model(model_name, device, **kwargs)
    # whisperx.load_model is een doorgeefluik (*args, **kwargs) naar whisperx.asr.load_model; die heeft "threads"
    # (standaard 4), dat faster-whisper/CTranslate2 als cpu_threads gebruikt.
    supports_threads = False
    try:
        import inspect
        import whisperx.asr as whisperx_asr  # type: ignore
        supports_threads = "threads" in inspect.signature(whisperx_asr.load_model).parameters
    except Exception:
        supports_threads = False
    if supports_threads:
        kwargs["threads"] = CPU_THREADS
    return whisperx_module.load_model(model_name, device, **kwargs)


def whisper_model_available_locally(model_name: str) -> bool:
    if not model_name:
        return False
    if os.path.isdir(model_name):
        return True
    try:
        from faster_whisper.utils import download_model
        download_model(model_name, local_files_only=True)
        return True
    except Exception:
        return False


def choose_cpu_whisper_model(current_model: str) -> str:
    """Het model voor de processor: het lichtere CPU-model (standaard large-v3-turbo met int8) in plaats van het
    videokaartmodel. Staat dat lichtere model nog niet op de pc maar het huidige wel, dan blijft het huidige, zodat een
    terugval midden in een transcriptie niet eerst gigabytes hoeft te downloaden."""
    cpu_model = (os.environ.get("CAPTIVOX_WHISPERX_CPU_MODEL") or "").strip()
    if not cpu_model or cpu_model == current_model:
        return current_model or cpu_model or DEFAULT_CPU_WHISPERX_MODEL
    if not current_model or current_model in ("ffmpeg-whisper-gpu", "whispercpp-vulkan"):
        return cpu_model
    if whisper_model_available_locally(cpu_model):
        eprint(f"INFO: processorroute gebruikt het CPU-model {cpu_model} in plaats van {current_model}")
        return cpu_model
    if whisper_model_available_locally(current_model):
        eprint(f"INFO: CPU-model {cpu_model} staat niet op deze pc; {current_model} is wel aanwezig en wordt gebruikt")
        return current_model
    return cpu_model


def configure_torch_checkpoint_compatibility(torch_module: Any) -> None:
    """Maak pyannote/WhisperX checkpoint loading compatibel met PyTorch 2.6+."""
    try:
        from omegaconf.dictconfig import DictConfig
        from omegaconf.listconfig import ListConfig

        serialization = getattr(torch_module, "serialization", None)
        add_safe_globals = getattr(serialization, "add_safe_globals", None) if serialization is not None else None
        if callable(add_safe_globals):
            try:
                add_safe_globals([DictConfig, ListConfig])
                eprint("INFO: torch checkpoint compatibility: OmegaConf safe globals geregistreerd.")
            except Exception as exc:
                eprint(f"WARN: torch checkpoint safe globals konden niet worden geregistreerd: {exc}")
    except Exception as exc:
        eprint(f"WARN: torch checkpoint compatibility setup overgeslagen: {exc}")


def install_huggingface_hub_token_compatibility() -> None:
    """Maak oudere pyannote-code compatibel met nieuwe huggingface_hub API's."""
    try:
        import huggingface_hub
        original = getattr(huggingface_hub, "hf_hub_download", None)
        if original is None or getattr(original, "_captivox_use_auth_token_compat", False):
            return

        def hf_hub_download_compat(*args, **kwargs):
            if "use_auth_token" in kwargs and "token" not in kwargs:
                kwargs["token"] = kwargs.pop("use_auth_token")
            else:
                kwargs.pop("use_auth_token", None)
            return original(*args, **kwargs)

        hf_hub_download_compat._captivox_use_auth_token_compat = True  # type: ignore[attr-defined]
        huggingface_hub.hf_hub_download = hf_hub_download_compat
    except Exception as exc:
        eprint(f"WARN: huggingface_hub compatibiliteit niet toegepast: {exc}")


PYANNOTE_COMMUNITY_1_MODEL = "pyannote/speaker-diarization-community-1"
PYANNOTE_MAINWORKER_CPU_MODEL = "pyannote/speaker-diarization-3.1"
PYANNOTE_LEGACY_STABLE_MODEL = "pyannote/speaker-diarization-3.1"
DIARIZATION_ACCELERATION_CPU = "cpu"
DIARIZATION_ACCELERATION_AUTO = "auto"
DIARIZATION_ACCELERATION_AMD_ROCM = "amd-rocm-experimental"
DIARIZATION_ACCELERATION_NVIDIA_CUDA = "nvidia-cuda-experimental"


def normalize_diarization_acceleration(value: Optional[str]) -> str:
    normalized = (value or DIARIZATION_ACCELERATION_AUTO).strip().lower()
    aliases = {
        "amd-rocm": DIARIZATION_ACCELERATION_AMD_ROCM,
        "rocm": DIARIZATION_ACCELERATION_AMD_ROCM,
        "nvidia-cuda": DIARIZATION_ACCELERATION_NVIDIA_CUDA,
        "cuda": DIARIZATION_ACCELERATION_NVIDIA_CUDA,
        "gpu": DIARIZATION_ACCELERATION_AUTO,
    }
    normalized = aliases.get(normalized, normalized)
    if normalized not in {DIARIZATION_ACCELERATION_CPU, DIARIZATION_ACCELERATION_AUTO, DIARIZATION_ACCELERATION_AMD_ROCM, DIARIZATION_ACCELERATION_NVIDIA_CUDA}:
        return DIARIZATION_ACCELERATION_AUTO
    return normalized


def parse_major_version(version_text: Optional[str]) -> Optional[int]:
    if not version_text:
        return None
    match = re.match(r"\s*(\d+)", str(version_text))
    return int(match.group(1)) if match else None


def get_installed_pyannote_audio_version() -> Optional[str]:
    try:
        return package_version("pyannote.audio")
    except Exception:
        return None


def resolve_diarization_model_for_runtime(model_id: Optional[str]) -> Dict[str, Any]:
    """Kies een pyannote-model dat past bij de geinstalleerde hoofdworker-runtime.

    De hoofdworker blijft op pyannote.audio 3.x om de bestaande WhisperX/NVIDIA-route
    niet onnodig te breken. Voor `speaker-diarization-community-1` probeert de AMD-
    postprocessing eerst de aparte CPU-sidecar met pyannote.audio 4.x. Alleen wanneer
    die sidecar niet beschikbaar is, valt deze hoofdworker gecontroleerd terug op 3.1.
    """
    requested = (model_id or PYANNOTE_MAINWORKER_CPU_MODEL).strip()
    version_text = get_installed_pyannote_audio_version()
    major = parse_major_version(version_text)
    effective = requested
    fallback_active = False
    fallback_reason = None

    if requested == PYANNOTE_COMMUNITY_1_MODEL and (major is None or major < 4):
        effective = PYANNOTE_LEGACY_STABLE_MODEL
        fallback_active = True
        fallback_reason = (
            f"{PYANNOTE_COMMUNITY_1_MODEL} vereist pyannote.audio 4.x. "
            f"De hoofdworker gebruikt {version_text or 'onbekend'}; valt terug op {PYANNOTE_LEGACY_STABLE_MODEL} "
            "als de aparte community-1 CPU-runtime niet beschikbaar is."
        )

    return {
        "requested_model": requested,
        "effective_model": effective,
        "pyannote_audio_version": version_text,
        "compatibility_fallback_active": fallback_active,
        "compatibility_fallback_reason": fallback_reason,
    }


def load_pyannote_pipeline(model_id: str, hf_token: Optional[str]):
    """Laad pyannote Pipeline compatibel met meerdere pyannote.audio-versies."""
    install_huggingface_hub_token_compatibility()
    from pyannote.audio import Pipeline

    attempts = []
    if hf_token:
        attempts.append(("token", {"token": hf_token}))
        attempts.append(("use_auth_token", {"use_auth_token": hf_token}))
    attempts.append(("cache/no-token", {}))

    last_type_error: Optional[Exception] = None
    for label, kwargs in attempts:
        try:
            pipeline = Pipeline.from_pretrained(model_id, **kwargs)
            eprint(f"INFO: diarization pipeline geladen via pyannote API: {label}")
            return pipeline
        except TypeError as exc:
            message = str(exc).lower()
            if "unexpected keyword argument" in message or "got an unexpected" in message:
                last_type_error = exc
                eprint(f"WARN: pyannote API ondersteunt argument voor {label} niet; probeer compatibiliteitsroute.")
                continue
            raise

    if last_type_error is not None:
        raise last_type_error
    raise RuntimeError("Diarization pipeline kon niet worden geladen.")


def format_diarization_exception(exc: Exception) -> str:
    """Maak pyannote/Hugging Face-fouten bruikbaar zonder token of lokale paden te lekken."""
    raw = str(exc) if exc is not None else ""
    lower = raw.lower()
    access_markers = ["403 forbidden", "cannot access content", "gated", "access to model", "make sure you have access", "repo not found", "not found"]
    cache_markers = ["cannot find the requested files in the local cache", "cannot find an appropriate cached snapshot", "local cache"]
    if any(marker in lower for marker in access_markers):
        return ("Pyannote diarization-model kon niet worden geladen door Hugging Face-toegang (403/gated/not found). "
                "Controleer of het opgeslagen HF-token read-toegang heeft en of de modelvoorwaarden voor het diarization-model zijn geaccepteerd.")
    if "unexpected keyword argument 'plda'" in lower or 'unexpected keyword argument "plda"' in lower:
        return ("Pyannote diarization-model is niet compatibel met de geinstalleerde pyannote.audio-runtime. "
                "Gebruik pyannote/speaker-diarization-3.1 bij pyannote.audio 3.x, of upgrade de volledige workerstack gecontroleerd naar pyannote.audio 4.x.")
    if any(marker in lower for marker in cache_markers):
        return ("Pyannote diarization-model staat niet lokaal in cache en kon niet online worden opgehaald. "
                "Controleer internettoegang, HF-token en modeltoegang. Voor pyannote/speaker-diarization-3.1 moeten ook de modelvoorwaarden van pyannote/speaker-diarization-3.1 en pyannote/segmentation-3.0 geaccepteerd zijn.")
    cleaned = re.sub(r"https://huggingface\.co/[^\s]+", "https://huggingface.co/[redacted]", raw)
    cleaned = re.sub(r"[A-Za-z]:\\[^\r\n]+", "[REDACTED_LOCAL_PATH]", cleaned)
    return cleaned or "Onbekende pyannote diarization-fout."

def format_ts(seconds: float) -> str:
    seconds = max(0, int(round(seconds)))
    hours, rem = divmod(seconds, 3600)
    minutes, secs = divmod(rem, 60)
    if hours:
        return f"{hours:02d}:{minutes:02d}:{secs:02d}"
    return f"{minutes:02d}:{secs:02d}"


def simplify_speaker(raw_speaker: Any, mapping: Dict[str, str]) -> str:
    if raw_speaker is None:
        return "Onbekend"
    key = str(raw_speaker).strip()
    if not key:
        return "Onbekend"
    if key not in mapping:
        mapping[key] = f"Persoon {len(mapping) + 1}"
    return mapping[key]


KNOWN_INTRO_NOISE = {
    "ondertiteld door de amara org gemeenschap",
    "ondertiteld door de amara org community",
}


def normalize_noise_text(text: str) -> str:
    value = re.sub(r"[^\w\s]", " ", text.lower(), flags=re.UNICODE)
    return " ".join(value.split())


def remove_known_intro_noise(serializable: Dict[str, Any]) -> int:
    segments = serializable.get("segments")
    if not isinstance(segments, list):
        return 0

    cleaned: List[Dict[str, Any]] = []
    removed = 0
    for index, seg in enumerate(segments):
        if not isinstance(seg, dict):
            cleaned.append(seg)
            continue
        text = str(seg.get("text", "") or "").strip()
        start = float(seg.get("start", 0.0) or 0.0)
        if index < 3 and start <= 15.0 and normalize_noise_text(text) in KNOWN_INTRO_NOISE:
            removed += 1
            continue
        cleaned.append(seg)

    if removed:
        serializable["segments"] = cleaned
    return removed


# Stukken zonder spraak die een spraakmodel soms in stiltes zet: alleen sterretjes, puntjes of streepjes, of een
# markering als [BLANK_AUDIO]. Bewust voorzichtig: alleen stukken die uit niets anders bestaan.
NON_SPEECH_ONLY = re.compile(r"^[\s*.\u2026\u00b7\-_~]*$")
NON_SPEECH_MARKER = re.compile(r"^[\[(]\s*(blank[_ ]audio|silence|stilte|no speech|music|muziek|applause|applaus)\s*[\])]$", re.IGNORECASE)


def is_non_speech_text(text: str) -> bool:
    value = (text or "").strip()
    return bool(NON_SPEECH_ONLY.match(value) or NON_SPEECH_MARKER.match(value))


def remove_non_speech_segments(serializable: Dict[str, Any]) -> int:
    segments = serializable.get("segments")
    if not isinstance(segments, list):
        return 0
    kept = [seg for seg in segments if not (isinstance(seg, dict) and is_non_speech_text(str(seg.get("text", "") or "")))]
    removed = len(segments) - len(kept)
    if removed:
        serializable["segments"] = kept
        serializable["removed_non_speech_segments"] = int(serializable.get("removed_non_speech_segments") or 0) + removed
        eprint(f"INFO: {removed} stuk(ken) zonder spraak weggelaten (alleen tekens als *** of ..., of [BLANK_AUDIO])")
    return removed


def merge_segments(
    segments: List[Dict[str, Any]],
    max_gap: float = 0.8,
    max_duration: float = 30.0,
    merge_unknown_speaker: bool = True,
) -> List[Dict[str, Any]]:
    merged: List[Dict[str, Any]] = []
    for seg in segments:
        text = str(seg.get("text", "")).strip()
        if not text:
            continue
        current = {
            "start": float(seg.get("start", 0.0) or 0.0),
            "end": float(seg.get("end", 0.0) or 0.0),
            "speaker": seg.get("speaker"),
            "text": text,
        }
        if not merged:
            merged.append(current)
            continue
        prev = merged[-1]
        current_speaker = current.get("speaker")
        prev_speaker = prev.get("speaker")
        speaker_known = current_speaker not in (None, "") and prev_speaker not in (None, "")
        same_speaker = prev_speaker == current_speaker and (merge_unknown_speaker or speaker_known)
        gap = current["start"] - float(prev.get("end", 0.0) or 0.0)
        merged_duration = current["end"] - float(prev.get("start", 0.0) or 0.0)
        if same_speaker and gap <= max_gap and merged_duration <= max_duration:
            prev["end"] = current["end"]
            prev["text"] = (prev["text"].rstrip() + " " + current["text"].lstrip()).strip()
        else:
            merged.append(current)
    return merged


def json_safe(value: Any) -> Any:
    if isinstance(value, (str, int, float, bool)) or value is None:
        return value
    if isinstance(value, list):
        return [json_safe(v) for v in value]
    if isinstance(value, dict):
        return {str(k): json_safe(v) for k, v in value.items()}
    if hasattr(value, "item"):
        try:
            return value.item()
        except Exception:
            pass
    return str(value)


def annotation_to_df(annotation_like):
    import pandas as pd

    if hasattr(annotation_like, "exclusive_speaker_diarization"):
        ann = annotation_like.exclusive_speaker_diarization
        eprint("INFO: gebruik exclusive_speaker_diarization uit DiarizeOutput")
    elif hasattr(annotation_like, "speaker_diarization"):
        ann = annotation_like.speaker_diarization
        eprint("INFO: gebruik speaker_diarization uit DiarizeOutput")
    else:
        ann = annotation_like

    if hasattr(ann, "itertracks"):
        rows = []
        for turn, _, speaker in ann.itertracks(yield_label=True):
            rows.append({"start": float(turn.start), "end": float(turn.end), "speaker": speaker})
        return pd.DataFrame(rows, columns=["start", "end", "speaker"])

    if isinstance(ann, pd.DataFrame):
        needed = [c for c in ["start", "end", "speaker"] if c in ann.columns]
        return ann[needed].copy()

    raise TypeError(f"Onbekend diarization-object: {type(annotation_like)!r} / inner={type(ann)!r}")


def load_diarization_waveform(input_path: str, ffmpeg_path: str, work_dir: str):
    """Maak een 16 kHz mono waveform voor pyannote zonder de ASR-engine te wijzigen."""
    import numpy as np
    import torch

    wav_path = prepare_diarization_wav_path(input_path, ffmpeg_path, work_dir)

    with wave.open(wav_path, "rb") as wav_file:
        channels = wav_file.getnchannels()
        sample_rate = wav_file.getframerate()
        sample_width = wav_file.getsampwidth()
        frames = wav_file.readframes(wav_file.getnframes())
    if channels != 1 or sample_rate != 16000 or sample_width != 2:
        raise RuntimeError(f"Diarization WAV heeft onverwacht formaat: channels={channels}, sample_rate={sample_rate}, sample_width={sample_width}")
    audio = np.frombuffer(frames, dtype=np.int16).astype(np.float32) / 32768.0
    waveform = torch.from_numpy(audio)[None, :].float().cpu()
    return {"waveform": waveform, "sample_rate": 16000}


def resolve_pyannote_runtime_device(torch_module: Any, prefer_gpu: bool = True) -> Dict[str, Any]:
    """Bepaal of pyannote op GPU kan draaien. Op Windows/AMD is CPU de realistische fallback."""
    result: Dict[str, Any] = {
        "device": "cpu",
        "engine": "pyannote",
        "gpu_available": False,
        "gpu_backend": None,
        "fallback_active": False,
        "fallback_reason": None,
    }
    if not prefer_gpu:
        return result
    try:
        cuda = getattr(torch_module, "cuda", None)
        if cuda is not None and bool(cuda.is_available()):
            hip_version = getattr(getattr(torch_module, "version", None), "hip", None)
            backend = "rocm/hip" if hip_version else "cuda"
            result.update({
                "device": "cuda",
                "gpu_available": True,
                "gpu_backend": backend,
                "fallback_active": False,
                "fallback_reason": None,
            })
            return result
        result["fallback_active"] = True
        result["fallback_reason"] = "pyannote GPU-runtime niet beschikbaar in deze Python-runtime; diarization draait op CPU."
    except Exception as exc:
        result["fallback_active"] = True
        result["fallback_reason"] = f"pyannote GPU-detectie faalde; diarization draait op CPU: {exc}"
    return result


def assign_segment_speakers_by_overlap(serializable: Dict[str, Any], diarize_df) -> Dict[str, Any]:
    """Ken speakerlabels toe aan FFmpeg/whisper.cpp segmenten op basis van maximale tijdsoverlap."""
    segments = serializable.get("segments")
    if not isinstance(segments, list):
        return {"assigned_segments": 0, "speaker_count": 0, "method": "segment_overlap", "diarization_segments": []}

    diarization_segments: List[Dict[str, Any]] = []
    try:
        for _, row in diarize_df.iterrows():
            start = float(row.get("start", 0.0) or 0.0)
            end = float(row.get("end", start) or start)
            speaker = str(row.get("speaker", "") or "").strip()
            if speaker and end > start:
                diarization_segments.append({"start": start, "end": end, "speaker": speaker})
    except Exception:
        diarization_segments = []

    assigned = 0
    speakers = set()
    for seg in segments:
        if not isinstance(seg, dict):
            continue
        start = float(seg.get("start", 0.0) or 0.0)
        end = float(seg.get("end", start) or start)
        if end <= start:
            end = start + 0.01
        scores: Dict[str, float] = {}
        for dia in diarization_segments:
            overlap = max(0.0, min(end, float(dia["end"])) - max(start, float(dia["start"])))
            if overlap > 0:
                speaker = str(dia["speaker"])
                scores[speaker] = scores.get(speaker, 0.0) + overlap
        if not scores:
            midpoint = start + ((end - start) / 2.0)
            for dia in diarization_segments:
                if float(dia["start"]) <= midpoint <= float(dia["end"]):
                    scores[str(dia["speaker"])] = 0.001
                    break
        if scores:
            speaker = max(scores.items(), key=lambda item: item[1])[0]
            seg["speaker"] = speaker
            assigned += 1
            speakers.add(speaker)

    serializable["diarization_segments"] = diarization_segments
    serializable["speaker_assignment_method"] = "segment_overlap"
    return {
        "assigned_segments": assigned,
        "speaker_count": len(speakers),
        "method": "segment_overlap",
        "diarization_segments": diarization_segments,
    }


def prepare_diarization_wav_path(input_path: str, ffmpeg_path: str, work_dir: str) -> str:
    """Maak een 16 kHz mono WAV-pad dat zowel hoofdworker als sidecar kunnen gebruiken."""
    wav_path = os.path.join(work_dir, "diarization_input_16k.wav")
    ffmpeg_exe = ensure_ffmpeg(ffmpeg_path)
    command = [
        ffmpeg_exe, "-y", "-nostdin", "-hide_banner", "-loglevel", "error",
        "-i", input_path, "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", wav_path,
    ]
    result = run_process_capture(command, timeout_seconds=None)
    if result.get("exit_code") != 0 or not os.path.exists(wav_path):
        raise RuntimeError(f"ffmpeg conversie voor diarization faalde: {result.get('stderr') or result.get('stdout') or result.get('error')}")
    return wav_path




def apply_pyannote_diarization_postprocess(serializable: Dict[str, Any], args, hf_token: Optional[str], progress, begin_phase, end_phase, runtime_root: str) -> Dict[str, Any]:
    """Voer speaker diarization uit na AMD/Vulkan-ASR en schrijf labels terug naar transcriptsegmenten."""
    if not getattr(args, "enable_diarization", False):
        return {
            "status": "disabled", "applied": False, "model": None, "warning": None,
            "engine": "pyannote", "device": None, "gpu_backend": None,
            "fallback_active": False, "fallback_reason": None,
            "requested_acceleration": normalize_diarization_acceleration(getattr(args, "diarization_acceleration", DIARIZATION_ACCELERATION_AUTO)),
            "effective_acceleration": "disabled",
            "gpu_available": False, "gpu_validated": False,
            "assigned_segments": 0, "speaker_count": 0,
        }
    if not hf_token:
        raise RuntimeError("Diarization is ingeschakeld maar er is geen Hugging Face token beschikbaar.")

    requested_diarization_model_id = getattr(args, "diarization_model", None) or PYANNOTE_MAINWORKER_CPU_MODEL
    requested_diarization_acceleration = normalize_diarization_acceleration(getattr(args, "diarization_acceleration", DIARIZATION_ACCELERATION_AUTO))
    work_dir = os.path.join(runtime_root or tempfile.gettempdir(), "diarization", f"captivox_{uuid.uuid4().hex}")
    os.makedirs(work_dir, exist_ok=True)
    gpu_sidecar_note: Optional[Dict[str, Any]] = None
    gpu_sidecar_failure_reason: Optional[str] = None
    gpu_sidecar_attempted: bool = False
    gpu_phase_started: bool = False



    model_resolution = resolve_diarization_model_for_runtime(requested_diarization_model_id)
    diarization_model_id = model_resolution["effective_model"]
    compatibility_fallback_active = bool(model_resolution.get("compatibility_fallback_active"))
    compatibility_fallback_reason = model_resolution.get("compatibility_fallback_reason")


    import torch
    configure_torch_checkpoint_compatibility(torch)

    runtime = resolve_pyannote_runtime_device(torch, prefer_gpu=False)
    intended_cpu_mainworker = (runtime.get("device") == "cpu" and diarization_model_id == PYANNOTE_MAINWORKER_CPU_MODEL and not compatibility_fallback_active)
    explicit_gpu_request = requested_diarization_acceleration in {DIARIZATION_ACCELERATION_AMD_ROCM, DIARIZATION_ACCELERATION_NVIDIA_CUDA}
    gpu_fallback_after_attempt = bool(gpu_sidecar_attempted and gpu_sidecar_failure_reason)
    combined_fallback_active = bool((runtime.get("fallback_active") and not intended_cpu_mainworker) or compatibility_fallback_active or gpu_fallback_after_attempt)
    combined_fallback_reason = None if intended_cpu_mainworker else runtime.get("fallback_reason")
    if gpu_sidecar_failure_reason:
        combined_fallback_reason = (combined_fallback_reason + " | " if combined_fallback_reason else "") + str(gpu_sidecar_failure_reason)
    if compatibility_fallback_reason:
        combined_fallback_reason = (combined_fallback_reason + " | " if combined_fallback_reason else "") + str(compatibility_fallback_reason)
        eprint(f"WARN: {compatibility_fallback_reason}")
    diarization_execution_mode = "intended_cpu_mainworker" if intended_cpu_mainworker and not gpu_fallback_after_attempt else ("cpu_fallback_after_gpu_request" if gpu_fallback_after_attempt else ("gpu" if runtime.get("device") == "cuda" else "cpu"))
    progress.set_metadata(
        diarization_engine="pyannote",
        diarization_requested=True,
        diarization_model=diarization_model_id,
        diarization_requested_model=requested_diarization_model_id,
        diarization_effective_model=diarization_model_id,
        pyannote_audio_version=model_resolution.get("pyannote_audio_version"),
        diarization_model_compatibility_fallback_active=compatibility_fallback_active,
        diarization_model_compatibility_fallback_reason=compatibility_fallback_reason,
        diarization_device=runtime.get("device"),
        diarization_gpu_backend=runtime.get("gpu_backend"),
        diarization_requested_acceleration=requested_diarization_acceleration,
        diarization_effective_acceleration="cpu",
        diarization_gpu_available=False,
        diarization_gpu_validated=False,
        diarization_gpu_backend_requested=(gpu_sidecar_note or {}).get("backend") if isinstance(gpu_sidecar_note, dict) else None,
        diarization_fallback_active=combined_fallback_active,
        diarization_fallback_reason=combined_fallback_reason,
        diarization_execution_mode=diarization_execution_mode)

    diarize_model = None
    diarization_output = None
    diarize_df = None
    try:
        begin_phase("diarization_load")
        diarize_model = load_pyannote_pipeline(diarization_model_id, hf_token)
        try:
            if runtime.get("device") == "cuda":
                diarize_model.to(torch.device("cuda"))
                eprint(f"INFO: diarization draait op GPU-backend: {runtime.get('gpu_backend') or 'cuda'}")
            else:
                eprint("INFO: diarization draait op CPU.")
        except Exception as device_exc:
            runtime["device"] = "cpu"
            runtime["fallback_active"] = True
            runtime["fallback_reason"] = f"pyannote GPU-toewijzing faalde; diarization draait op CPU: {device_exc}"
            progress.set_metadata(diarization_device="cpu", diarization_fallback_active=True, diarization_fallback_reason=runtime.get("fallback_reason"))
            eprint(f"WARN: {runtime['fallback_reason']}")
        end_phase("diarization_load")

        begin_phase("diarization")
        diarization_input = load_diarization_waveform(args.input, getattr(args, "ffmpeg_path", "ffmpeg"), work_dir)
        diarize_kwargs = {}
        if getattr(args, "min_speakers", 0) and int(args.min_speakers) > 0:
            diarize_kwargs["min_speakers"] = int(args.min_speakers)
        if getattr(args, "max_speakers", 0) and int(args.max_speakers) > 0:
            diarize_kwargs["max_speakers"] = int(args.max_speakers)
        diarization_hook = PyannoteProgressBridge(progress, "diarization")
        try:
            with diarization_hook as hook:
                diarization_output = diarize_model(diarization_input, hook=hook, **diarize_kwargs)
        except TypeError as hook_exc:
            if "hook" not in str(hook_exc).lower():
                raise
            eprint("WARN: pyannote hook niet ondersteund in deze omgeving; diarization loopt door zonder subprogress.")
            diarization_output = diarize_model(diarization_input, **diarize_kwargs)
        end_phase("diarization")

        diarize_df = annotation_to_df(diarization_output)
        if diarize_df.empty:
            return {
                "status": "empty", "applied": False, "model": diarization_model_id,
                "warning": "diarization gaf geen speakersegmenten terug", "engine": "pyannote",
                "device": runtime.get("device"), "gpu_backend": runtime.get("gpu_backend"),
                "fallback_active": combined_fallback_active, "fallback_reason": combined_fallback_reason,
                "requested_model": requested_diarization_model_id, "effective_model": diarization_model_id,
                "model_compatibility_fallback_active": compatibility_fallback_active, "model_compatibility_fallback_reason": compatibility_fallback_reason,
                "pyannote_audio_version": model_resolution.get("pyannote_audio_version"),
                "requested_acceleration": requested_diarization_acceleration,
                "effective_acceleration": "cpu",
                "gpu_available": False,
                "gpu_validated": False,
                "runtime_python": sys.executable,
                "runtime_torch": package_version("torch"),
                "runtime_torchaudio": package_version("torchaudio"),
                "runtime_torch_cuda": getattr(getattr(torch, "version", None), "cuda", None),
                "runtime_torch_hip": getattr(getattr(torch, "version", None), "hip", None),
                "assigned_segments": 0, "speaker_count": 0,
                "execution_mode": diarization_execution_mode,
            }

        assignment = assign_segment_speakers_by_overlap(serializable, diarize_df)
        applied = int(assignment.get("assigned_segments") or 0) > 0
        return {
            "status": "completed" if applied else "no_overlap",
            "applied": applied,
            "model": diarization_model_id,
            "warning": None if applied else "diarization leverde segmenten op, maar geen overlap met transcriptsegmenten",
            "engine": "pyannote",
            "device": runtime.get("device"),
            "gpu_backend": runtime.get("gpu_backend"),
            "fallback_active": combined_fallback_active,
            "fallback_reason": combined_fallback_reason,
            "requested_model": requested_diarization_model_id, "effective_model": diarization_model_id,
            "model_compatibility_fallback_active": compatibility_fallback_active, "model_compatibility_fallback_reason": compatibility_fallback_reason,
            "pyannote_audio_version": model_resolution.get("pyannote_audio_version"),
            "requested_acceleration": requested_diarization_acceleration,
            "effective_acceleration": "cpu",
            "gpu_available": False,
            "gpu_validated": False,
            "runtime_python": sys.executable,
            "runtime_torch": package_version("torch"),
            "runtime_torchaudio": package_version("torchaudio"),
            "runtime_torch_cuda": getattr(getattr(torch, "version", None), "cuda", None),
            "runtime_torch_hip": getattr(getattr(torch, "version", None), "hip", None),
            "assigned_segments": int(assignment.get("assigned_segments") or 0),
            "speaker_count": int(assignment.get("speaker_count") or 0),
            "execution_mode": diarization_execution_mode,
        }
    finally:
        shutil.rmtree(work_dir, ignore_errors=True)
        try:
            del diarize_model
            del diarization_output
            del diarize_df
        except Exception:
            pass
        gc.collect()
        try:
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
        except Exception:
            pass


def ensure_ffmpeg(ffmpeg_path: str) -> str:
    candidate = ffmpeg_path or os.environ.get("FFMPEG_PATH") or "ffmpeg"
    exe = shutil.which(candidate) if os.path.basename(candidate) == candidate else candidate
    if not exe or not os.path.exists(exe):
        raise FileNotFoundError(f"ffmpeg niet gevonden: {candidate}")
    result = subprocess.run([exe, "-version"], capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"ffmpeg -version faalde met exitcode {result.returncode}")
    eprint(f"INFO: ffmpeg OK via {exe}")
    # WhisperX (whisperx.load_audio) start zelf "ffmpeg" via PATH. De eigen FFmpeg van Captivox staat niet in PATH, dus
    # zet zijn map vooraan, alleen voor dit proces en zijn kinderen.
    try:
        ffmpeg_dir = os.path.dirname(os.path.abspath(exe))
        current_path = os.environ.get("PATH", "")
        entries = [entry for entry in current_path.split(os.pathsep) if entry]
        if not entries or os.path.normcase(os.path.abspath(entries[0])) != os.path.normcase(ffmpeg_dir):
            os.environ["PATH"] = ffmpeg_dir + (os.pathsep + current_path if current_path else "")
    except Exception as exc:
        eprint(f"WARN: map van ffmpeg kon niet aan PATH worden toegevoegd: {exc}")
    return exe


def write_test_wav(path: str, sample_rate: int = 16000, seconds: float = 1.0) -> None:
    frame_count = int(sample_rate * seconds)
    with wave.open(path, "wb") as wav_file:
        wav_file.setnchannels(1)
        wav_file.setsampwidth(2)
        wav_file.setframerate(sample_rate)
        wav_file.writeframes(b"\x00\x00" * frame_count)


def package_version(name: str) -> str:
    try:
        return importlib.metadata.version(name)
    except Exception:
        return "unknown"


def normalize_transcription_profile(value: Optional[str]) -> str:
    normalized = (value or "auto").strip().lower()
    if normalized in {"nvidia", "cuda", "nvidia-cuda"}:
        return "nvidia-cuda"
    if normalized in {"amd", "amd-gpu", "rocm", "hip"}:
        return "amd-gpu"
    if normalized in {"cpu", "cpu-fallback"}:
        return "cpu-fallback"
    return "auto"


def detect_torch_runtime(torch_module: Any) -> Dict[str, Any]:
    if torch_module is None:
        return {
            "cuda_available": False,
            "cuda_device_count": 0,
            "cuda_device_names": [],
            "cuda_version": None,
            "hip_version": None,
            "amd_runtime": False,
            "nvidia_runtime": False,
            "probe_skipped": "torch_not_imported",
        }
    cuda_available = False
    cuda_device_count = 0
    cuda_device_names: List[str] = []
    cuda_version = None
    hip_version = None
    try:
        cuda_available = bool(torch_module.cuda.is_available())
        cuda_device_count = int(torch_module.cuda.device_count()) if cuda_available else 0
        for index in range(cuda_device_count):
            try:
                cuda_device_names.append(str(torch_module.cuda.get_device_name(index)))
            except Exception:
                cuda_device_names.append(f"cuda:{index}")
    except Exception as exc:
        cuda_available = False
        cuda_device_names.append(f"cuda-detect-error: {exc}")
    try:
        cuda_version = getattr(getattr(torch_module, "version", None), "cuda", None)
    except Exception:
        cuda_version = None
    try:
        hip_version = getattr(getattr(torch_module, "version", None), "hip", None)
    except Exception:
        hip_version = None

    names_joined = " ".join(cuda_device_names).lower()
    amd_runtime = bool(hip_version) or "amd" in names_joined or "radeon" in names_joined
    nvidia_runtime = cuda_available and not amd_runtime
    return {
        "cuda_available": cuda_available,
        "cuda_device_count": cuda_device_count,
        "cuda_device_names": cuda_device_names,
        "cuda_version": cuda_version,
        "hip_version": hip_version,
        "amd_runtime": amd_runtime,
        "nvidia_runtime": nvidia_runtime,
    }


def detect_ctranslate2_runtime() -> Dict[str, Any]:
    result: Dict[str, Any] = {
        "ctranslate2_available": False,
        "ctranslate2_version": package_version("ctranslate2"),
        "ct2_cuda_available": False,
        "ct2_cuda_device_count": 0,
        "ct2_cuda_compute_types": [],
        "ct2_probe_error": None,
    }
    try:
        import ctranslate2  # type: ignore
        result["ctranslate2_available"] = True
        try:
            count = ctranslate2.get_cuda_device_count()
            result["ct2_cuda_device_count"] = int(count or 0)
            result["ct2_cuda_available"] = result["ct2_cuda_device_count"] > 0
        except Exception as exc:
            result["ct2_probe_error"] = f"get_cuda_device_count: {exc}"
        try:
            compute_types = ctranslate2.get_supported_compute_types("cuda")
            result["ct2_cuda_compute_types"] = [str(item) for item in compute_types]
            if result["ct2_cuda_compute_types"]:
                result["ct2_cuda_available"] = True
        except Exception as exc:
            existing = result.get("ct2_probe_error")
            result["ct2_probe_error"] = (str(existing) + "; " if existing else "") + f"get_supported_compute_types(cuda): {exc}"
    except Exception as exc:
        result["ct2_probe_error"] = str(exc)
    return result



def resolve_whispercpp_exe(args) -> str:
    candidate = (getattr(args, "whispercpp_exe", "") or "").strip().strip('"')
    if candidate and os.path.exists(candidate):
        return candidate
    return ""


def resolve_amd_runtime_manifest_from_model_hint(model_hint: str) -> Dict[str, Any]:
    try:
        hint = (model_hint or "").strip().strip('"')
        roots: List[str] = []
        if hint:
            abs_hint = os.path.abspath(hint)
            if os.path.basename(os.path.dirname(abs_hint)).lower() == "models":
                roots.append(os.path.dirname(os.path.dirname(abs_hint)))
            else:
                roots.append(os.path.dirname(abs_hint))
        local = os.environ.get("CAPTIVOX_WORKER_DATA_ROOT")
        if local:
            roots.append(os.path.join(local, "runtime", "whispercpp"))
        seen = set()
        for root in roots:
            root = os.path.abspath(root)
            if root in seen:
                continue
            seen.add(root)
            manifest_path = os.path.join(root, "runtime_manifest.json")
            if not os.path.exists(manifest_path):
                continue
            with open(manifest_path, "r", encoding="utf-8") as fh:
                data = json.load(fh)
            if isinstance(data, dict):
                data["_manifest_path"] = manifest_path
                return data
    except Exception:
        return {}
    return {}


def resolve_whispercpp_model(args) -> str:
    candidate = (getattr(args, "whispercpp_model", "") or "").strip().strip('"')
    manifest = resolve_amd_runtime_manifest_from_model_hint(candidate)
    selected = str(manifest.get("selected_model_path") or "").strip().strip('"') if isinstance(manifest, dict) else ""
    if selected and os.path.exists(selected):
        return selected
    try:
        local = os.environ.get("CAPTIVOX_WORKER_DATA_ROOT")
        if local:
            q5 = os.path.join(local, "runtime", "whispercpp", "models", "ggml-large-v3-turbo-q5_0.bin")
            if os.path.exists(q5):
                return q5
    except Exception:
        pass
    if candidate and os.path.exists(candidate):
        return candidate
    return ""


def build_amd_gpu_process_env(args) -> Dict[str, str]:
    env = os.environ.copy()
    model_hint = (getattr(args, "whispercpp_model", "") or "").strip().strip('"')
    manifest = resolve_amd_runtime_manifest_from_model_hint(model_hint)
    env["GGML_VK_VISIBLE_DEVICES"] = str(manifest.get("ggml_vk_visible_devices") or "0") if isinstance(manifest, dict) else "0"
    disable_coopmat = True
    if isinstance(manifest, dict) and "disable_coopmat" in manifest:
        disable_coopmat = bool(manifest.get("disable_coopmat"))
    if disable_coopmat:
        env["GGML_VK_DISABLE_COOPMAT"] = "1"
    else:
        env.pop("GGML_VK_DISABLE_COOPMAT", None)
    return env


def run_process_capture(command: List[str], timeout_seconds: Optional[int] = None, cwd: Optional[str] = None, env: Optional[Dict[str, str]] = None) -> Dict[str, Any]:
    try:
        completed = subprocess.run(command, capture_output=True, text=True, timeout=timeout_seconds, cwd=cwd, env=env)
        return {
            "exit_code": completed.returncode,
            "stdout": completed.stdout or "",
            "stderr": completed.stderr or "",
            "error": None,
        }
    except subprocess.TimeoutExpired as exc:
        return {
            "exit_code": -1,
            "stdout": exc.stdout or "",
            "stderr": exc.stderr or "",
            "error": f"timeout_after_{timeout_seconds}s",
        }
    except Exception as exc:
        return {"exit_code": -1, "stdout": "", "stderr": "", "error": str(exc)}



def resolve_whispercpp_runtime_root_from_model(model_path: str) -> str:
    model_path = os.path.abspath(model_path)
    model_dir = os.path.dirname(model_path)
    if os.path.basename(model_dir).lower() == "models":
        return os.path.dirname(model_dir)
    return model_dir


def relative_ffmpeg_filter_value(path: str, root: str) -> str:
    root_abs = os.path.abspath(root)
    path_abs = os.path.abspath(path)
    rel = os.path.relpath(path_abs, root_abs)
    if rel.startswith("..") or os.path.isabs(rel):
        raise RuntimeError(f"FFmpeg whisper-filterpad ligt niet onder runtime root: {path_abs}")

    # FFmpeg filteropties gebruiken ':' als scheidingsteken en '\\' als escape.
    # Geef daarom alleen relatieve POSIX-paden door binnen de runtime root.
    rel = rel.replace("\\", "/").replace(os.sep, "/")
    if "\\" in rel:
        raise RuntimeError(f"Relatief FFmpeg whisper-filterpad bevat nog een backslash: {rel}")
    if ":" in rel:
        raise RuntimeError(f"Relatief FFmpeg whisper-filterpad bevat onverwacht ':' teken: {rel}")
    if not rel.strip():
        raise RuntimeError(f"Relatief FFmpeg whisper-filterpad is leeg voor: {path_abs}")
    return rel.replace("'", r"\'")

def get_ffmpeg_whisper_help(ffmpeg_path: str) -> str:
    ffmpeg_exe = ensure_ffmpeg(ffmpeg_path)
    result = run_process_capture([ffmpeg_exe, "-hide_banner", "-h", "filter=whisper"], timeout_seconds=15)
    return ((result.get("stdout") or "") + "\n" + (result.get("stderr") or ""))


def build_ffmpeg_whisper_probe(args) -> Dict[str, Any]:
    ffmpeg_path = getattr(args, "ffmpeg_path", "ffmpeg") or "ffmpeg"
    model = resolve_whispercpp_model(args)
    probe: Dict[str, Any] = {
        "engine": "FFmpeg Whisper filter",
        "ffmpeg": ffmpeg_path,
        "model": model or (getattr(args, "whispercpp_model", "") or ""),
        "model_exists": bool(model),
        "filter_available": False,
        "use_gpu_option": False,
        "gpu_device_option": False,
        "available": False,
        "reason": None,
    }
    if not model:
        probe["reason"] = "Geen ggml-modelbestand gevonden. FFmpeg Whisper heeft een whisper.cpp ggml-model nodig, bijvoorbeeld ggml-large-v3-turbo-q5_0.bin."
        return probe
    try:
        help_text = get_ffmpeg_whisper_help(ffmpeg_path)
    except Exception as exc:
        probe["reason"] = f"ffmpeg whisper-filter kon niet worden bevraagd: {exc}"
        return probe
    lower = help_text.lower()
    probe["filter_available"] = "whisper" in lower and "model" in lower and "language" in lower
    probe["use_gpu_option"] = "use_gpu" in lower
    probe["gpu_device_option"] = "gpu_device" in lower
    if not probe["filter_available"]:
        probe["reason"] = "Deze FFmpeg-build heeft geen bruikbare whisper audiofilter. Gebruik een FFmpeg-build met --enable-whisper."
        return probe
    if not probe["use_gpu_option"]:
        probe["reason"] = "FFmpeg whisper-filter is aanwezig, maar use_gpu ontbreekt; GPU-gebruik kan niet betrouwbaar worden afgedwongen."
        return probe
    probe["available"] = True
    probe["reason"] = "FFmpeg whisper-filter met use_gpu is beschikbaar. Dit is de primaire AMD GPU-route; self-test valideert model en CoopMat-instellingen."
    return probe


def parse_ffmpeg_whisper_json_file(path: str, language: str) -> Dict[str, Any]:
    if not os.path.exists(path):
        return {"language": language, "segments": []}
    with open(path, "r", encoding="utf-8", errors="replace") as handle:
        raw_text = handle.read().strip()
    if not raw_text:
        return {"language": language, "segments": []}
    try:
        return normalize_whispercpp_json(json.loads(raw_text), language)
    except Exception:
        pass
    items: List[Any] = []
    for line in raw_text.splitlines():
        line = line.strip().rstrip(",")
        if not line:
            continue
        try:
            items.append(json.loads(line))
        except Exception:
            continue
    if items:
        return normalize_whispercpp_json(items, language)
    return {"language": language, "segments": []}


def build_ffmpeg_whisper_command(args, input_path: str, json_destination: str) -> tuple[List[str], str]:
    ffmpeg_exe = ensure_ffmpeg(getattr(args, "ffmpeg_path", "ffmpeg"))
    model = resolve_whispercpp_model(args)
    if not model:
        raise RuntimeError("FFmpeg Whisper-engine kan niet starten: ggml-model ontbreekt.")
    language = (getattr(args, "language", "nl") or "nl").strip() or "nl"
    runtime_root = resolve_whispercpp_runtime_root_from_model(model)
    model_value = relative_ffmpeg_filter_value(model, runtime_root)
    destination_value = relative_ffmpeg_filter_value(json_destination, runtime_root)
    whisper_filter = (
        f"whisper=model={model_value}:language={language}:queue=20:"
        f"use_gpu=true:gpu_device=0:destination={destination_value}:format=json"
    )
    # Logniveau debug: alleen dan geeft FFmpeg de regels van whisper.cpp door (ggml_vulkan, Vulkan0), waarmee hieronder
    # het gebruik van de videokaart wordt bevestigd. Op info faalt de bevestiging altijd.
    command = [
        ffmpeg_exe, "-y", "-nostdin", "-hide_banner", "-loglevel", "debug",
        "-i", input_path, "-vn", "-af", whisper_filter, "-f", "null", "-",
    ]
    return command, runtime_root


def run_ffmpeg_whisper_gpu_engine(args, progress, phase_timings: Dict[str, float], mark_phase, begin_phase, end_phase, duration_ms, hf_token: Optional[str] = None) -> int:
    requested_profile = normalize_transcription_profile(getattr(args, "transcription_profile", "auto"))
    active_profile = "amd-gpu"
    device = "vulkan"
    compute_type = "ffmpeg-whisper"
    batch_size = 1
    probe = build_runtime_backend_probe(None, args)
    probe["engine_selection"] = "ffmpeg-whisper-gpu"
    write_runtime_backend_probe(args, probe)
    model_path = resolve_whispercpp_model(args)
    amd_env = build_amd_gpu_process_env(args)
    progress.set_metadata(runtime_device=device, runtime_compute_type=compute_type, requested_compute_type=getattr(args, "compute_type", None), requested_transcription_profile=requested_profile, active_transcription_profile=active_profile, transcription_engine="FFmpeg Whisper GPU", gpu_fallback_active=False, model=model_path, amd_gpu_env={"GGML_VK_VISIBLE_DEVICES": amd_env.get("GGML_VK_VISIBLE_DEVICES"), "GGML_VK_DISABLE_COOPMAT": amd_env.get("GGML_VK_DISABLE_COOPMAT", "")})

    diarization_status = "disabled" if not getattr(args, "enable_diarization", False) else "pending"
    diarization_warning = None
    diarization_applied = False
    diarization_model = getattr(args, "diarization_model", None) if getattr(args, "enable_diarization", False) else None
    diarization_requested_model = diarization_model
    diarization_effective_model = diarization_model
    pyannote_audio_version = None
    diarization_model_compatibility_fallback_active = False
    diarization_model_compatibility_fallback_reason = None
    diarization_engine = "pyannote" if getattr(args, "enable_diarization", False) else None
    diarization_device = None
    diarization_gpu_backend = None
    diarization_fallback_active = False
    diarization_fallback_reason = None
    diarization_execution_mode = None
    diarization_requested_acceleration = normalize_diarization_acceleration(getattr(args, "diarization_acceleration", DIARIZATION_ACCELERATION_AUTO))
    diarization_effective_acceleration = "disabled" if not getattr(args, "enable_diarization", False) else None
    diarization_gpu_available = False
    diarization_gpu_validated = False
    diarization_gpu_device_name = None
    rocm_lstm_dropout_patch_applied = None
    rocm_lstm_modules_patched = None
    rocm_lstm_dropout_original_values = None
    rocm_lstm_dropout_patch_warning = None
    gpu_inference_smoketest_ok = None
    gpu_inference_smoketest_warning = None
    diarization_runtime_python = None
    diarization_runtime_torch = None
    diarization_runtime_torchaudio = None
    diarization_runtime_torch_cuda = None
    diarization_runtime_torch_hip = None
    diarization_assigned_segments = 0
    diarization_speaker_count = 0
    ffmpeg_stdout = ""
    ffmpeg_stderr = ""
    ffmpeg_exit_code = None
    gpu_confirmed = False

    runtime_root = resolve_whispercpp_runtime_root_from_model(model_path) if model_path else os.getcwd()
    run_dir = os.path.join(runtime_root, "runs", f"captivox_{uuid.uuid4().hex}")
    os.makedirs(run_dir, exist_ok=True)
    output_json = os.path.join(run_dir, "ffmpeg_whisper_output.json")
    try:
        command, ffmpeg_cwd = build_ffmpeg_whisper_command(args, args.input, output_json)
        begin_phase("transcribe")
        result = run_process_capture(command, timeout_seconds=None, cwd=ffmpeg_cwd, env=build_amd_gpu_process_env(args))
        ffmpeg_exit_code = int(result.get("exit_code") or 0)
        ffmpeg_stdout = result.get("stdout") or ""
        ffmpeg_stderr = result.get("stderr") or ""
        end_phase("transcribe")
        combined_log = (ffmpeg_stdout + "\n" + ffmpeg_stderr).lower()
        gpu_confirmed = any(token in combined_log for token in ["use gpu    = 1", "ggml_vulkan", "vulkan0", "using vulkan", "gpu_device = 0"])
        if ffmpeg_exit_code != 0:
            raise RuntimeError(f"FFmpeg Whisper GPU-engine faalde met exitcode {ffmpeg_exit_code}: {ffmpeg_stderr or ffmpeg_stdout or result.get('error')}")
        if not os.path.exists(output_json):
            raise RuntimeError("FFmpeg Whisper GPU-engine draaide, maar leverde geen JSON-destinationbestand op.")
        serializable = parse_ffmpeg_whisper_json_file(output_json, args.language or "nl")
        if not serializable.get("segments"):
            raise RuntimeError("FFmpeg Whisper GPU-engine leverde geen bruikbare transcriptsegmenten op.")
        if not gpu_confirmed:
            raise RuntimeError("FFmpeg Whisper draaide, maar GPU/Vulkan-gebruik werd niet bevestigd in de engine-output. Dit wordt niet als AMD GPU-transcriptie geaccepteerd.")
        mark_baseline_transcript_ready(
            serializable, args,
            engine="FFmpeg Whisper GPU", requested_profile=requested_profile, active_profile=active_profile,
            device=device, compute_type=compute_type, batch_size=batch_size,
            gpu_fallback_active=False, gpu_fallback_reason=None, confirmed=gpu_confirmed, confirmation_field="vulkan_confirmed")
        if getattr(args, "enable_diarization", False):
            try:
                diarization_result = apply_pyannote_diarization_postprocess(serializable, args, hf_token, progress, begin_phase, end_phase, runtime_root)
                diarization_status = str(diarization_result.get("status") or "unknown")
                diarization_applied = bool(diarization_result.get("applied"))
                diarization_model = diarization_result.get("model")
                diarization_requested_model = diarization_result.get("requested_model") or diarization_requested_model
                diarization_effective_model = diarization_result.get("effective_model") or diarization_model
                pyannote_audio_version = diarization_result.get("pyannote_audio_version")
                diarization_model_compatibility_fallback_active = bool(diarization_result.get("model_compatibility_fallback_active"))
                diarization_model_compatibility_fallback_reason = diarization_result.get("model_compatibility_fallback_reason")
                diarization_warning = diarization_result.get("warning")
                diarization_engine = diarization_result.get("engine")
                diarization_device = diarization_result.get("device")
                diarization_gpu_backend = diarization_result.get("gpu_backend")
                diarization_fallback_active = bool(diarization_result.get("fallback_active"))
                diarization_fallback_reason = diarization_result.get("fallback_reason")
                diarization_execution_mode = diarization_result.get("execution_mode")
                diarization_requested_acceleration = diarization_result.get("requested_acceleration") or diarization_requested_acceleration
                diarization_effective_acceleration = diarization_result.get("effective_acceleration") or diarization_effective_acceleration
                diarization_gpu_available = bool(diarization_result.get("gpu_available"))
                diarization_gpu_validated = bool(diarization_result.get("gpu_validated"))
                diarization_gpu_device_name = diarization_result.get("gpu_device_name")
                rocm_lstm_dropout_patch_applied = diarization_result.get("rocm_lstm_dropout_patch_applied")
                rocm_lstm_modules_patched = diarization_result.get("rocm_lstm_modules_patched")
                rocm_lstm_dropout_original_values = diarization_result.get("rocm_lstm_dropout_original_values")
                rocm_lstm_dropout_patch_warning = diarization_result.get("rocm_lstm_dropout_patch_warning")
                gpu_inference_smoketest_ok = diarization_result.get("gpu_inference_smoketest_ok")
                gpu_inference_smoketest_warning = diarization_result.get("gpu_inference_smoketest_warning")
                diarization_runtime_python = diarization_result.get("runtime_python")
                diarization_runtime_torch = diarization_result.get("runtime_torch")
                diarization_runtime_torchaudio = diarization_result.get("runtime_torchaudio")
                diarization_runtime_torch_cuda = diarization_result.get("runtime_torch_cuda")
                diarization_runtime_torch_hip = diarization_result.get("runtime_torch_hip")
                diarization_assigned_segments = int(diarization_result.get("assigned_segments") or 0)
                diarization_speaker_count = int(diarization_result.get("speaker_count") or 0)
                if diarization_status != "completed":
                    eprint(f"WARN: AMD diarization post-processing niet volledig toegepast: status={diarization_status}; warning={diarization_warning}")
            except Exception as diarization_exc:
                if "diarization_load_started" in phase_timings and "diarization_load_finished" not in phase_timings:
                    end_phase("diarization_load")
                if "diarization_started" in phase_timings and "diarization_finished" not in phase_timings:
                    end_phase("diarization")
                diarization_status = "failed"
                diarization_applied = False
                diarization_warning = format_diarization_exception(diarization_exc)
                eprint(f"WARN: AMD diarization post-processing mislukt; transcriptie wordt zonder speakerlabels opgeslagen: {diarization_warning}")
                eprint(traceback.format_exc())
        else:
            eprint("INFO: diarization overgeslagen op basis van instellingen.")
        serializable["diarization_requested"] = bool(getattr(args, "enable_diarization", False))
        serializable["diarization_engine"] = diarization_engine
        serializable["diarization_device"] = diarization_device
        serializable["diarization_gpu_backend"] = diarization_gpu_backend
        serializable["diarization_fallback_active"] = diarization_fallback_active
        serializable["diarization_fallback_reason"] = diarization_fallback_reason
        serializable["diarization_execution_mode"] = diarization_execution_mode
        serializable["diarization_requested_acceleration"] = diarization_requested_acceleration
        serializable["diarization_effective_acceleration"] = diarization_effective_acceleration
        serializable["diarization_gpu_available"] = diarization_gpu_available
        serializable["diarization_gpu_validated"] = diarization_gpu_validated
        serializable["diarization_gpu_device_name"] = diarization_gpu_device_name
        serializable["rocm_lstm_dropout_patch_applied"] = rocm_lstm_dropout_patch_applied
        serializable["rocm_lstm_modules_patched"] = rocm_lstm_modules_patched
        serializable["rocm_lstm_dropout_original_values"] = rocm_lstm_dropout_original_values
        serializable["rocm_lstm_dropout_patch_warning"] = rocm_lstm_dropout_patch_warning
        serializable["gpu_inference_smoketest_ok"] = gpu_inference_smoketest_ok
        serializable["gpu_inference_smoketest_warning"] = gpu_inference_smoketest_warning
        serializable["diarization_runtime_python"] = diarization_runtime_python
        serializable["diarization_runtime_torch"] = diarization_runtime_torch
        serializable["diarization_runtime_torchaudio"] = diarization_runtime_torchaudio
        serializable["diarization_runtime_torch_cuda"] = diarization_runtime_torch_cuda
        serializable["diarization_runtime_torch_hip"] = diarization_runtime_torch_hip
        serializable["diarization_assigned_segments"] = diarization_assigned_segments
        serializable["diarization_speaker_count"] = diarization_speaker_count
        serializable["engine"] = "FFmpeg Whisper GPU"
        serializable["requested_transcription_profile"] = requested_profile
        serializable["active_transcription_profile"] = active_profile
        serializable["runtime_device"] = device
        serializable["runtime_compute_type"] = compute_type
        serializable["runtime_batch_size"] = batch_size
        serializable["gpu_fallback_active"] = False
        serializable["gpu_fallback_reason"] = None
        serializable["vulkan_confirmed"] = gpu_confirmed
        serializable["diarization_applied"] = diarization_applied
        serializable["diarization_status"] = diarization_status
        serializable["diarization_model"] = diarization_model
        serializable["diarization_requested_model"] = diarization_requested_model
        serializable["diarization_effective_model"] = diarization_effective_model
        serializable["pyannote_audio_version"] = pyannote_audio_version
        serializable["diarization_model_compatibility_fallback_active"] = diarization_model_compatibility_fallback_active
        serializable["diarization_model_compatibility_fallback_reason"] = diarization_model_compatibility_fallback_reason
        serializable["diarization_warning"] = diarization_warning
        serializable["captivox_schema_version"] = "captivox-transcript-v1"
        serializable["generated_at"] = utc_now_z()
        begin_phase("export")
        write_transcript_artifacts(serializable, args)
        end_phase("export")
    finally:
        shutil.rmtree(run_dir, ignore_errors=True)

    mark_phase("helper_finished")
    progress.complete()
    if args.timings_json:
        timings_payload = {
            "device": device, "compute_type": compute_type, "engine": "FFmpeg Whisper GPU", "model": model_path,
            "requested_transcription_profile": requested_profile, "active_transcription_profile": active_profile, "batch_size": batch_size,
            "gpu_fallback_active": False, "gpu_fallback_reason": None, "vulkan_confirmed": gpu_confirmed, "runtime_backend_probe": json_safe(probe),
            "diarization_status": diarization_status, "diarization_applied": diarization_applied, "diarization_model": diarization_model, "diarization_requested_model": diarization_requested_model, "diarization_effective_model": diarization_effective_model, "pyannote_audio_version": pyannote_audio_version, "diarization_model_compatibility_fallback_active": diarization_model_compatibility_fallback_active, "diarization_model_compatibility_fallback_reason": diarization_model_compatibility_fallback_reason, "diarization_warning": diarization_warning, "diarization_engine": diarization_engine, "diarization_device": diarization_device, "diarization_gpu_backend": diarization_gpu_backend, "diarization_fallback_active": diarization_fallback_active, "diarization_fallback_reason": diarization_fallback_reason,
            "diarization_execution_mode": diarization_execution_mode,
            "diarization_requested_acceleration": diarization_requested_acceleration,
            "diarization_effective_acceleration": diarization_effective_acceleration,
            "diarization_gpu_available": diarization_gpu_available,
            "diarization_gpu_validated": diarization_gpu_validated,
            "diarization_gpu_device_name": diarization_gpu_device_name,
            "rocm_lstm_dropout_patch_applied": rocm_lstm_dropout_patch_applied,
            "rocm_lstm_modules_patched": rocm_lstm_modules_patched,
            "rocm_lstm_dropout_original_values": rocm_lstm_dropout_original_values,
            "rocm_lstm_dropout_patch_warning": rocm_lstm_dropout_patch_warning,
            "gpu_inference_smoketest_ok": gpu_inference_smoketest_ok,
            "gpu_inference_smoketest_warning": gpu_inference_smoketest_warning,
            "diarization_runtime_python": diarization_runtime_python,
            "diarization_runtime_torch": diarization_runtime_torch,
            "diarization_runtime_torchaudio": diarization_runtime_torchaudio,
            "diarization_runtime_torch_cuda": diarization_runtime_torch_cuda,
            "diarization_runtime_torch_hip": diarization_runtime_torch_hip,
            "diarization_assigned_segments": diarization_assigned_segments, "diarization_speaker_count": diarization_speaker_count, "ffmpeg_exit_code": ffmpeg_exit_code,
            "durations_ms": {"transcribe": duration_ms("transcribe_started", "transcribe_finished"), "diarization_load": duration_ms("diarization_load_started", "diarization_load_finished"), "diarization": duration_ms("diarization_started", "diarization_finished"), "export": duration_ms("export_started", "export_finished"), "helper_total": duration_ms("helper_started", "helper_finished")},
        }
        with open(args.timings_json, "w", encoding="utf-8") as f_timing:
            json.dump(json_safe(timings_payload), f_timing, ensure_ascii=False, indent=2)
    eprint("INFO: AMD transcriptie uitgevoerd via FFmpeg Whisper GPU-engine.")
    return 0

def build_whispercpp_vulkan_probe(args) -> Dict[str, Any]:
    disabled_reason = getattr(args, "whispercpp_disabled_reason", None)
    exe = resolve_whispercpp_exe(args)
    model = resolve_whispercpp_model(args)
    probe: Dict[str, Any] = {
        "engine": "whisper.cpp Vulkan",
        "exe": exe or (getattr(args, "whispercpp_exe", "") or ""),
        "model": model or (getattr(args, "whispercpp_model", "") or ""),
        "exe_exists": bool(exe),
        "model_exists": bool(model),
        "available": False,
        "vulkan_confirmed": False,
        "reason": None,
        "help_exit_code": None,
        "help_mentions_vulkan": False,
    }
    if not exe:
        probe["reason"] = "whisper-cli.exe niet gevonden. Plaats een Vulkan-build onder runtime\\whispercpp of configureer worker.whisperCppExe."
        return probe
    if not model:
        probe["reason"] = "Geen ggml-modelbestand gevonden voor whisper.cpp. Plaats bijvoorbeeld ggml-large-v3-turbo-q5_0.bin onder runtime\\whispercpp\\models of configureer worker.whisperCppModelPath."
        return probe
    help_result = run_process_capture([exe, "-h"], timeout_seconds=10)
    probe["help_exit_code"] = help_result.get("exit_code")
    combined = ((help_result.get("stdout") or "") + "\n" + (help_result.get("stderr") or "")).lower()
    probe["help_mentions_vulkan"] = "vulkan" in combined
    if help_result.get("exit_code") not in (0, 1):
        probe["reason"] = f"whisper-cli -h faalde: {help_result.get('error') or help_result.get('stderr') or help_result.get('stdout')}"
        return probe
    probe["available"] = True
    probe["reason"] = "whisper.cpp engine en ggml-model zijn gevonden; Captivox gebruikt AMD/Vulkan-safe flags worden capability-based gekozen op basis van whisper.cpp -h. Vulkan wordt tijdens echte transcriptie gevalideerd op basis van engine-output."
    probe["safe_vulkan_flags"] = ["--no-flash-attn", "--beam-size", "1", "--best-of", "1", "--device", "0"]
    return probe


def parse_timecode_to_seconds(value: Any, numeric_unit: str = "auto") -> float:
    if isinstance(value, (int, float)):
        numeric = float(value)
        if numeric_unit == "milliseconds":
            return numeric / 1000.0
        if numeric_unit == "seconds":
            return numeric
        # Auto mode is intentionally conservative. Explicit whisper.cpp/FFmpeg
        # timestamp fields are handled as milliseconds by the caller. For unknown
        # numeric start/end fields, only obviously millisecond-scale values are
        # divided here.
        return numeric / 1000.0 if numeric > 86400 else numeric
    text = str(value or "").strip()
    if not text:
        return 0.0
    text = text.replace(",", ".")
    parts = text.split(":")
    try:
        if len(parts) == 3:
            return int(parts[0]) * 3600 + int(parts[1]) * 60 + float(parts[2])
        if len(parts) == 2:
            return int(parts[0]) * 60 + float(parts[1])
        numeric = float(text)
        if numeric_unit == "milliseconds":
            return numeric / 1000.0
        if numeric_unit == "seconds":
            return numeric
        return numeric / 1000.0 if numeric > 86400 else numeric
    except Exception:
        return 0.0


def _numeric_value(value: Any) -> Optional[float]:
    if isinstance(value, (int, float)):
        return float(value)
    try:
        text = str(value or "").strip()
        if not text or ":" in text:
            return None
        return float(text.replace(",", "."))
    except Exception:
        return None


def _infer_unknown_numeric_time_unit(source_segments: List[Any]) -> str:
    durations: List[float] = []
    for item in source_segments:
        if not isinstance(item, dict):
            continue
        if isinstance(item.get("offsets"), dict) or isinstance(item.get("timestamps"), dict):
            continue
        start_raw = _numeric_value(item.get("start"))
        end_raw = _numeric_value(item.get("end"))
        if start_raw is None or end_raw is None:
            continue
        duration = end_raw - start_raw
        if duration > 0:
            durations.append(duration)
    if not durations:
        return "auto"
    durations_sorted = sorted(durations)
    median = durations_sorted[len(durations_sorted) // 2]
    # Whisper.cpp/FFmpeg segment durations in milliseconds are usually thousands
    # to tens of thousands. Real segment durations in seconds should not have a
    # median above three minutes. This avoids corrupting long recordings that use
    # second-based absolute timestamps.
    if median > 180.0:
        return "milliseconds"
    return "seconds"


def normalize_whispercpp_json(raw: Any, language: str) -> Dict[str, Any]:
    segments: List[Dict[str, Any]] = []
    source_segments = []
    if isinstance(raw, dict):
        if isinstance(raw.get("transcription"), list):
            source_segments = raw.get("transcription") or []
        elif isinstance(raw.get("segments"), list):
            source_segments = raw.get("segments") or []
        elif isinstance(raw.get("result"), dict) and isinstance(raw["result"].get("segments"), list):
            source_segments = raw["result"].get("segments") or []
    elif isinstance(raw, list):
        source_segments = raw

    unknown_numeric_unit = _infer_unknown_numeric_time_unit(source_segments)

    for item in source_segments:
        if not isinstance(item, dict):
            continue
        offsets = item.get("offsets") if isinstance(item.get("offsets"), dict) else {}
        timestamps = item.get("timestamps") if isinstance(item.get("timestamps"), dict) else {}
        if offsets and ("from" in offsets or "to" in offsets):
            start = parse_timecode_to_seconds(offsets.get("from", 0.0), numeric_unit="milliseconds")
            end = parse_timecode_to_seconds(offsets.get("to", offsets.get("from", 0.0)), numeric_unit="milliseconds")
        elif timestamps and ("from" in timestamps or "to" in timestamps):
            start = parse_timecode_to_seconds(timestamps.get("from", 0.0), numeric_unit="milliseconds")
            end = parse_timecode_to_seconds(timestamps.get("to", timestamps.get("from", 0.0)), numeric_unit="milliseconds")
        else:
            start = parse_timecode_to_seconds(item.get("start", 0.0), numeric_unit=unknown_numeric_unit)
            end = parse_timecode_to_seconds(item.get("end", start), numeric_unit=unknown_numeric_unit)
        text = str(item.get("text", "") or "").strip()
        if not text:
            continue
        segments.append({"start": start, "end": end, "text": text})
    return {"language": language, "segments": segments}


def parse_whispercpp_stdout(stdout: str, language: str) -> Dict[str, Any]:
    pattern = re.compile(r"\[(\d{2}:\d{2}:\d{2}[\.,]\d{3})\s+-->\s+(\d{2}:\d{2}:\d{2}[\.,]\d{3})\]\s*(.*)")
    segments: List[Dict[str, Any]] = []
    for line in stdout.splitlines():
        match = pattern.search(line)
        if not match:
            continue
        text = match.group(3).strip()
        if not text:
            continue
        segments.append({"start": parse_timecode_to_seconds(match.group(1)), "end": parse_timecode_to_seconds(match.group(2)), "text": text})
    return {"language": language, "segments": segments}


def convert_audio_for_whispercpp(input_path: str, ffmpeg_path: str, work_dir: str) -> str:
    output_path = os.path.join(work_dir, "whispercpp_input.wav")
    ffmpeg_exe = ensure_ffmpeg(ffmpeg_path)
    cmd = [ffmpeg_exe, "-y", "-i", input_path, "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", output_path]
    result = run_process_capture(cmd, timeout_seconds=None)
    if result.get("exit_code") != 0 or not os.path.exists(output_path):
        raise RuntimeError(f"ffmpeg conversie voor whisper.cpp faalde: {result.get('stderr') or result.get('stdout') or result.get('error')}")
    return output_path


def get_whispercpp_help_text(exe: str) -> str:
    try:
        result = run_process_capture([exe, "-h"], timeout_seconds=10)
        return ((result.get("stdout") or "") + "\n" + (result.get("stderr") or ""))
    except Exception:
        try:
            result = run_process_capture([exe, "--help"], timeout_seconds=10)
            return ((result.get("stdout") or "") + "\n" + (result.get("stderr") or ""))
        except Exception:
            return ""


def help_mentions_flag(help_text: str, flag: str) -> bool:
    return bool(help_text and flag in help_text)


def build_whispercpp_command(args, wav_path: str, output_base: str) -> List[str]:
    exe = resolve_whispercpp_exe(args)
    model = resolve_whispercpp_model(args)
    language = (getattr(args, "language", "nl") or "nl").strip()
    command = [exe, "-m", model, "-f", wav_path, "-l", language, "-oj", "-osrt", "-ovtt", "-of", output_base]
    help_text = get_whispercpp_help_text(exe)

    # AMD/Vulkan-safe defaults, maar alleen wanneer de gekozen whisper.cpp-build
    # deze flags ondersteunt. De runtime blijft standaard op een stabiele
    # whisper.cpp-ref; oudere CLI's mogen niet met unsupported flags worden gestart.
    if help_mentions_flag(help_text, "--no-flash-attn"):
        command.append("--no-flash-attn")
    if help_mentions_flag(help_text, "--beam-size"):
        command.extend(["--beam-size", "1"])
    elif help_mentions_flag(help_text, "-bs"):
        command.extend(["-bs", "1"])
    if help_mentions_flag(help_text, "--best-of"):
        command.extend(["--best-of", "1"])
    elif help_mentions_flag(help_text, "-bo"):
        command.extend(["-bo", "1"])
    if help_mentions_flag(help_text, "--device"):
        command.extend(["--device", "0"])
    return command

def run_whispercpp_vulkan_engine(args, progress, phase_timings: Dict[str, float], mark_phase, begin_phase, end_phase, duration_ms, hf_token: Optional[str] = None) -> int:
    requested_profile = normalize_transcription_profile(getattr(args, "transcription_profile", "auto"))
    active_profile = "amd-gpu"
    device = "vulkan"
    compute_type = "ggml"
    batch_size = 1
    probe = build_runtime_backend_probe(None, args)
    probe["engine_selection"] = "whispercpp-vulkan"
    write_runtime_backend_probe(args, probe)
    progress.set_metadata(
        runtime_device=device,
        runtime_compute_type=compute_type,
        requested_compute_type=getattr(args, "compute_type", None),
        requested_transcription_profile=requested_profile,
        active_transcription_profile=active_profile,
        transcription_engine="whisper.cpp Vulkan",
        gpu_fallback_active=False,
        model=resolve_whispercpp_model(args))

    diarization_status = "disabled" if not getattr(args, "enable_diarization", False) else "pending"
    diarization_warning = None
    diarization_applied = False
    diarization_model = getattr(args, "diarization_model", None) if getattr(args, "enable_diarization", False) else None
    diarization_requested_model = diarization_model
    diarization_effective_model = diarization_model
    pyannote_audio_version = None
    diarization_model_compatibility_fallback_active = False
    diarization_model_compatibility_fallback_reason = None
    diarization_engine = "pyannote" if getattr(args, "enable_diarization", False) else None
    diarization_device = None
    diarization_gpu_backend = None
    diarization_fallback_active = False
    diarization_fallback_reason = None
    diarization_execution_mode = None
    diarization_requested_acceleration = normalize_diarization_acceleration(getattr(args, "diarization_acceleration", DIARIZATION_ACCELERATION_AUTO))
    diarization_effective_acceleration = "disabled" if not getattr(args, "enable_diarization", False) else None
    diarization_gpu_available = False
    diarization_gpu_validated = False
    diarization_gpu_device_name = None
    rocm_lstm_dropout_patch_applied = None
    rocm_lstm_modules_patched = None
    rocm_lstm_dropout_original_values = None
    rocm_lstm_dropout_patch_warning = None
    gpu_inference_smoketest_ok = None
    gpu_inference_smoketest_warning = None
    diarization_runtime_python = None
    diarization_runtime_torch = None
    diarization_runtime_torchaudio = None
    diarization_runtime_torch_cuda = None
    diarization_runtime_torch_hip = None
    diarization_assigned_segments = 0
    diarization_speaker_count = 0
    serializable: Dict[str, Any] = {}
    whispercpp_stdout = ""
    whispercpp_stderr = ""
    whispercpp_exit_code = None
    vulkan_confirmed = False

    with tempfile.TemporaryDirectory(prefix="captivox_whispercpp_") as tmp:
        begin_phase("audio_prepare")
        wav_path = convert_audio_for_whispercpp(args.input, args.ffmpeg_path, tmp)
        end_phase("audio_prepare")

        output_base = os.path.join(tmp, "whispercpp_output")
        command = build_whispercpp_command(args, wav_path, output_base)
        begin_phase("transcribe")
        result = run_process_capture(command, timeout_seconds=None)
        whispercpp_exit_code = int(result.get("exit_code") or 0)
        whispercpp_stdout = result.get("stdout") or ""
        whispercpp_stderr = result.get("stderr") or ""
        end_phase("transcribe")

        combined_log = (whispercpp_stdout + "\n" + whispercpp_stderr).lower()
        vulkan_confirmed = "vulkan" in combined_log
        if whispercpp_exit_code != 0:
            raise RuntimeError(f"whisper.cpp Vulkan-engine faalde met exitcode {whispercpp_exit_code}: {whispercpp_stderr or whispercpp_stdout or result.get('error')}")
        if not vulkan_confirmed:
            raise RuntimeError("whisper.cpp draaide, maar Vulkan-backend werd niet bevestigd in de engine-output. Dit wordt niet als AMD GPU-transcriptie geaccepteerd.")

        json_path = output_base + ".json"
        srt_path = output_base + ".srt"
        vtt_path = output_base + ".vtt"
        if os.path.exists(json_path):
            with open(json_path, "r", encoding="utf-8") as handle:
                raw_json = json.load(handle)
            serializable = normalize_whispercpp_json(raw_json, args.language or "nl")
        else:
            serializable = parse_whispercpp_stdout(whispercpp_stdout, args.language or "nl")

        if not serializable.get("segments"):
            raise RuntimeError("whisper.cpp leverde geen bruikbare transcriptsegmenten op.")

        mark_baseline_transcript_ready(
            serializable, args,
            engine="whisper.cpp Vulkan", requested_profile=requested_profile, active_profile=active_profile,
            device=device, compute_type=compute_type, batch_size=batch_size,
            gpu_fallback_active=False, gpu_fallback_reason=None, confirmed=vulkan_confirmed, confirmation_field="vulkan_confirmed")

        if getattr(args, "enable_diarization", False):
            try:
                diarization_result = apply_pyannote_diarization_postprocess(serializable, args, hf_token, progress, begin_phase, end_phase, tempfile.gettempdir())
                diarization_status = str(diarization_result.get("status") or "unknown")
                diarization_applied = bool(diarization_result.get("applied"))
                diarization_model = diarization_result.get("model")
                diarization_requested_model = diarization_result.get("requested_model") or diarization_requested_model
                diarization_effective_model = diarization_result.get("effective_model") or diarization_model
                pyannote_audio_version = diarization_result.get("pyannote_audio_version")
                diarization_model_compatibility_fallback_active = bool(diarization_result.get("model_compatibility_fallback_active"))
                diarization_model_compatibility_fallback_reason = diarization_result.get("model_compatibility_fallback_reason")
                diarization_warning = diarization_result.get("warning")
                diarization_engine = diarization_result.get("engine")
                diarization_device = diarization_result.get("device")
                diarization_gpu_backend = diarization_result.get("gpu_backend")
                diarization_fallback_active = bool(diarization_result.get("fallback_active"))
                diarization_fallback_reason = diarization_result.get("fallback_reason")
                diarization_execution_mode = diarization_result.get("execution_mode")
                diarization_requested_acceleration = diarization_result.get("requested_acceleration") or diarization_requested_acceleration
                diarization_effective_acceleration = diarization_result.get("effective_acceleration") or diarization_effective_acceleration
                diarization_gpu_available = bool(diarization_result.get("gpu_available"))
                diarization_gpu_validated = bool(diarization_result.get("gpu_validated"))
                diarization_gpu_device_name = diarization_result.get("gpu_device_name")
                rocm_lstm_dropout_patch_applied = diarization_result.get("rocm_lstm_dropout_patch_applied")
                rocm_lstm_modules_patched = diarization_result.get("rocm_lstm_modules_patched")
                rocm_lstm_dropout_original_values = diarization_result.get("rocm_lstm_dropout_original_values")
                rocm_lstm_dropout_patch_warning = diarization_result.get("rocm_lstm_dropout_patch_warning")
                gpu_inference_smoketest_ok = diarization_result.get("gpu_inference_smoketest_ok")
                gpu_inference_smoketest_warning = diarization_result.get("gpu_inference_smoketest_warning")
                diarization_runtime_python = diarization_result.get("runtime_python")
                diarization_runtime_torch = diarization_result.get("runtime_torch")
                diarization_runtime_torchaudio = diarization_result.get("runtime_torchaudio")
                diarization_runtime_torch_cuda = diarization_result.get("runtime_torch_cuda")
                diarization_runtime_torch_hip = diarization_result.get("runtime_torch_hip")
                diarization_assigned_segments = int(diarization_result.get("assigned_segments") or 0)
                diarization_speaker_count = int(diarization_result.get("speaker_count") or 0)
                if diarization_status != "completed":
                    eprint(f"WARN: AMD whisper.cpp diarization post-processing niet volledig toegepast: status={diarization_status}; warning={diarization_warning}")
            except Exception as diarization_exc:
                if "diarization_load_started" in phase_timings and "diarization_load_finished" not in phase_timings:
                    end_phase("diarization_load")
                if "diarization_started" in phase_timings and "diarization_finished" not in phase_timings:
                    end_phase("diarization")
                diarization_status = "failed"
                diarization_applied = False
                diarization_warning = format_diarization_exception(diarization_exc)
                eprint(f"WARN: AMD whisper.cpp diarization post-processing mislukt; transcriptie wordt zonder speakerlabels opgeslagen: {diarization_warning}")
                eprint(traceback.format_exc())
        else:
            eprint("INFO: diarization overgeslagen op basis van instellingen.")

        serializable["engine"] = "whisper.cpp Vulkan"
        serializable["requested_transcription_profile"] = requested_profile
        serializable["active_transcription_profile"] = active_profile
        serializable["runtime_device"] = device
        serializable["runtime_compute_type"] = compute_type
        serializable["runtime_batch_size"] = batch_size
        serializable["gpu_fallback_active"] = False
        serializable["gpu_fallback_reason"] = None
        serializable["vulkan_confirmed"] = vulkan_confirmed
        serializable["diarization_requested"] = bool(getattr(args, "enable_diarization", False))
        serializable["diarization_engine"] = diarization_engine
        serializable["diarization_device"] = diarization_device
        serializable["diarization_gpu_backend"] = diarization_gpu_backend
        serializable["diarization_fallback_active"] = diarization_fallback_active
        serializable["diarization_fallback_reason"] = diarization_fallback_reason
        serializable["diarization_execution_mode"] = diarization_execution_mode
        serializable["diarization_requested_acceleration"] = diarization_requested_acceleration
        serializable["diarization_effective_acceleration"] = diarization_effective_acceleration
        serializable["diarization_gpu_available"] = diarization_gpu_available
        serializable["diarization_gpu_validated"] = diarization_gpu_validated
        serializable["diarization_gpu_device_name"] = diarization_gpu_device_name
        serializable["rocm_lstm_dropout_patch_applied"] = rocm_lstm_dropout_patch_applied
        serializable["rocm_lstm_modules_patched"] = rocm_lstm_modules_patched
        serializable["rocm_lstm_dropout_original_values"] = rocm_lstm_dropout_original_values
        serializable["rocm_lstm_dropout_patch_warning"] = rocm_lstm_dropout_patch_warning
        serializable["gpu_inference_smoketest_ok"] = gpu_inference_smoketest_ok
        serializable["gpu_inference_smoketest_warning"] = gpu_inference_smoketest_warning
        serializable["diarization_runtime_python"] = diarization_runtime_python
        serializable["diarization_runtime_torch"] = diarization_runtime_torch
        serializable["diarization_runtime_torchaudio"] = diarization_runtime_torchaudio
        serializable["diarization_runtime_torch_cuda"] = diarization_runtime_torch_cuda
        serializable["diarization_runtime_torch_hip"] = diarization_runtime_torch_hip
        serializable["diarization_assigned_segments"] = diarization_assigned_segments
        serializable["diarization_speaker_count"] = diarization_speaker_count
        serializable["diarization_applied"] = diarization_applied
        serializable["diarization_status"] = diarization_status
        serializable["diarization_model"] = diarization_model
        serializable["diarization_requested_model"] = diarization_requested_model
        serializable["diarization_effective_model"] = diarization_effective_model
        serializable["pyannote_audio_version"] = pyannote_audio_version
        serializable["diarization_model_compatibility_fallback_active"] = diarization_model_compatibility_fallback_active
        serializable["diarization_model_compatibility_fallback_reason"] = diarization_model_compatibility_fallback_reason
        serializable["diarization_warning"] = diarization_warning
        serializable["captivox_schema_version"] = "captivox-transcript-v1"
        serializable["generated_at"] = utc_now_z()

        begin_phase("export")
        write_transcript_artifacts(serializable, args)
        end_phase("export")

    mark_phase("helper_finished")
    progress.complete()
    if args.timings_json:
        timings_payload = {
            "device": device,
            "compute_type": compute_type,
            "engine": "whisper.cpp Vulkan",
            "model": resolve_whispercpp_model(args),
            "requested_transcription_profile": requested_profile,
            "active_transcription_profile": active_profile,
            "batch_size": batch_size,
            "gpu_fallback_active": False,
            "gpu_fallback_reason": None,
            "vulkan_confirmed": vulkan_confirmed,
            "runtime_backend_probe": json_safe(probe),
            "diarization_requested": bool(getattr(args, "enable_diarization", False)),
            "diarization_status": diarization_status,
            "diarization_applied": diarization_applied,
            "diarization_model": diarization_model,
            "diarization_requested_model": diarization_requested_model,
            "diarization_effective_model": diarization_effective_model,
            "pyannote_audio_version": pyannote_audio_version,
            "diarization_model_compatibility_fallback_active": diarization_model_compatibility_fallback_active,
            "diarization_model_compatibility_fallback_reason": diarization_model_compatibility_fallback_reason,
            "diarization_warning": diarization_warning,
            "diarization_engine": diarization_engine,
            "diarization_device": diarization_device,
            "diarization_gpu_backend": diarization_gpu_backend,
            "diarization_fallback_active": diarization_fallback_active,
            "diarization_fallback_reason": diarization_fallback_reason,
            "diarization_execution_mode": diarization_execution_mode,
            "diarization_requested_acceleration": diarization_requested_acceleration,
            "diarization_effective_acceleration": diarization_effective_acceleration,
            "diarization_gpu_available": diarization_gpu_available,
            "diarization_gpu_validated": diarization_gpu_validated,
            "diarization_gpu_device_name": diarization_gpu_device_name,
            "rocm_lstm_dropout_patch_applied": rocm_lstm_dropout_patch_applied,
            "rocm_lstm_modules_patched": rocm_lstm_modules_patched,
            "rocm_lstm_dropout_original_values": rocm_lstm_dropout_original_values,
            "rocm_lstm_dropout_patch_warning": rocm_lstm_dropout_patch_warning,
            "gpu_inference_smoketest_ok": gpu_inference_smoketest_ok,
            "gpu_inference_smoketest_warning": gpu_inference_smoketest_warning,
            "diarization_runtime_python": diarization_runtime_python,
            "diarization_runtime_torch": diarization_runtime_torch,
            "diarization_runtime_torchaudio": diarization_runtime_torchaudio,
            "diarization_runtime_torch_cuda": diarization_runtime_torch_cuda,
            "diarization_runtime_torch_hip": diarization_runtime_torch_hip,
            "diarization_assigned_segments": diarization_assigned_segments,
            "diarization_speaker_count": diarization_speaker_count,
            "whispercpp_exit_code": whispercpp_exit_code,
            "durations_ms": {
                "audio_prepare": duration_ms("audio_prepare_started", "audio_prepare_finished"),
                "transcribe": duration_ms("transcribe_started", "transcribe_finished"),
                "export": duration_ms("export_started", "export_finished"),
                "helper_total": duration_ms("helper_started", "helper_finished"),
            }
        }
        with open(args.timings_json, "w", encoding="utf-8") as f_timing:
            json.dump(json_safe(timings_payload), f_timing, ensure_ascii=False, indent=2)
    eprint("INFO: AMD transcriptie uitgevoerd via whisper.cpp Vulkan-engine.")
    return 0

def build_runtime_backend_probe(torch_module: Any, args=None) -> Dict[str, Any]:
    torch_runtime = detect_torch_runtime(torch_module)
    ct2_runtime = detect_ctranslate2_runtime()
    whispercpp_runtime = build_whispercpp_vulkan_probe(args) if args is not None else {"available": False, "reason": "args_not_available"}
    ffmpeg_whisper_runtime = build_ffmpeg_whisper_probe(args) if args is not None else {"available": False, "reason": "args_not_available"}
    hip_available = bool(torch_runtime.get("hip_version"))
    amd_runtime_available = bool(torch_runtime.get("amd_runtime")) or (hip_available and bool(ct2_runtime.get("ct2_cuda_available")))
    amd_gpu_available = bool(ffmpeg_whisper_runtime.get("available")) or bool(whispercpp_runtime.get("available"))
    return {
        "probe_version": "2",
        "torch": torch_runtime,
        "ctranslate2": ct2_runtime,
        "ffmpeg_whisper": ffmpeg_whisper_runtime,
        "whispercpp_vulkan": whispercpp_runtime,
        "nvidia_cuda_runtime_available": bool(torch_runtime.get("cuda_available")) and not bool(torch_runtime.get("amd_runtime")),
        "amd_hip_runtime_available": amd_runtime_available,
        "amd_ffmpeg_whisper_available": bool(ffmpeg_whisper_runtime.get("available")),
        "amd_whispercpp_vulkan_available": bool(whispercpp_runtime.get("available")),
        "gpu_runtime_available": bool(torch_runtime.get("cuda_available")) or amd_gpu_available or bool(ct2_runtime.get("ct2_cuda_device_count") or 0) > 0,
        "note": "NVIDIA gebruikt WhisperX/CTranslate2/CUDA. AMD gebruikt primair FFmpeg Whisper filter met use_gpu=true, gevalideerd quantized model en CoopMat-safe runtime-instellingen; speaker diarization probeert bij AMD/NVIDIA auto eerst de geisoleerde GPU-sidecar en valt bij timeout of fout gecontroleerd terug op CPU-sidecar.",
    }

def write_runtime_backend_probe(args, probe: Dict[str, Any]) -> None:
    timings_path = getattr(args, "timings_json", None)
    if not timings_path:
        return
    try:
        directory = os.path.dirname(os.path.abspath(timings_path))
        if not directory:
            return
        os.makedirs(directory, exist_ok=True)
        path = os.path.join(directory, "runtime_backend_probe.json")
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(json_safe(probe), handle, ensure_ascii=False, indent=2)
    except Exception as exc:
        eprint(f"WARN: runtime_backend_probe.json kon niet worden geschreven: {exc}")


def resolve_runtime_from_profile(args, torch_module: Any) -> Dict[str, Any]:
    requested_profile = normalize_transcription_profile(getattr(args, "transcription_profile", "auto"))
    requested_compute_type = (getattr(args, "compute_type", "float16") or "float16").strip().lower()
    backend_probe = build_runtime_backend_probe(torch_module, args)
    write_runtime_backend_probe(args, backend_probe)
    capabilities = backend_probe["torch"]
    ctranslate2_capabilities = backend_probe["ctranslate2"]
    fallback_reason = None
    gpu_fallback_active = False
    active_profile = requested_profile
    device = (getattr(args, "device", "auto") or "auto").strip().lower()
    compute_type = requested_compute_type
    batch_size = max(1, int(getattr(args, "batch_size", 4) or 4))

    if requested_profile == "cpu-fallback":
        active_profile = "cpu-fallback"
        device = "cpu"
        compute_type = "int8" if compute_type == "float16" else compute_type
        batch_size = min(batch_size, 2)
    elif requested_profile == "nvidia-cuda":
        if capabilities["cuda_available"]:
            active_profile = "nvidia-cuda"
            device = "cuda"
            compute_type = "float16" if compute_type in {"auto", ""} else compute_type
        else:
            active_profile = "cpu-fallback"
            device = "cpu"
            compute_type = "int8"
            batch_size = min(batch_size, 2)
            gpu_fallback_active = True
            fallback_reason = "NVIDIA CUDA-profiel geselecteerd, maar CUDA is niet beschikbaar in deze Python-runtime."
    elif requested_profile == "amd-gpu":
        ffmpeg_whisper_runtime = backend_probe.get("ffmpeg_whisper", {}) if isinstance(backend_probe, dict) else {}
        whispercpp_runtime = backend_probe.get("whispercpp_vulkan", {}) if isinstance(backend_probe, dict) else {}
        if isinstance(ffmpeg_whisper_runtime, dict) and ffmpeg_whisper_runtime.get("available"):
            active_profile = "amd-gpu"
            device = "vulkan"
            compute_type = "ffmpeg-whisper"
            batch_size = 1
        elif isinstance(whispercpp_runtime, dict) and whispercpp_runtime.get("available"):
            active_profile = "amd-gpu"
            device = "vulkan"
            compute_type = "ggml"
            batch_size = 1
        else:
            active_profile = "cpu-fallback"
            device = "cpu"
            compute_type = "int8"
            batch_size = min(batch_size, 2)
            gpu_fallback_active = True
            fallback_reason = "AMD GPU-profiel geselecteerd, maar geen werkende FFmpeg Whisper GPU-route of whisper.cpp Vulkan-runtime gevonden. AMD wordt in deze Windows-lijn niet via WhisperX/CTranslate2 CUDA uitgevoerd."
    else:
        if device == "auto":
            if capabilities["cuda_available"]:
                device = "cuda"
                active_profile = "amd-gpu" if capabilities["amd_runtime"] else "nvidia-cuda"
            else:
                device = "cpu"
                active_profile = "cpu-fallback"
                compute_type = "int8" if compute_type == "float16" else compute_type
                batch_size = min(batch_size, 2)
        if device == "cpu" and compute_type == "float16":
            compute_type = "int8"

    if device == "cpu" and compute_type == "float16":
        compute_type = "int8"
        batch_size = min(batch_size, 2)

    return {
        "requested_profile": requested_profile,
        "active_profile": active_profile,
        "device": device,
        "compute_type": compute_type,
        "requested_compute_type": requested_compute_type,
        "batch_size": batch_size,
        "gpu_fallback_active": gpu_fallback_active,
        "gpu_fallback_reason": fallback_reason,
        "capabilities": capabilities,
        "ctranslate2_capabilities": ctranslate2_capabilities,
        "backend_probe": backend_probe,
    }


def is_gpu_runtime_failure(exc: Exception) -> bool:
    message = str(exc).lower()
    gpu_terms = [
        "cuda", "cublas", "cudnn", "gpu", "hip", "rocm", "driver", "out of memory",
        "no kernel image", "invalid device", "device-side assert", "failed to initialize"
    ]
    return any(term in message for term in gpu_terms)



def emit_selftest_summary(success: bool, mode: str, messages: List[str], checks: Dict[str, Any], args, python_version: Optional[str] = None, runtime_device: Optional[str] = None, runtime_compute_type: Optional[str] = None) -> None:
    payload = {
        "success": success,
        "runtime_mode": "python",
        "self_test_mode": mode,
        "python_version": python_version or sys.version.split()[0],
        "model": args.model,
        "requested_transcription_profile": normalize_transcription_profile(getattr(args, "transcription_profile", "auto")),
        "checks": json_safe(checks),
        "messages": messages,
    }
    if runtime_device:
        payload["device"] = runtime_device
    if runtime_compute_type:
        payload["compute_type"] = runtime_compute_type
    print("SELFTEST_JSON:" + json.dumps(payload, ensure_ascii=False))


def run_self_test(args, hf_token: Optional[str]) -> int:
    ensure_ffmpeg(args.ffmpeg_path)

    mode = (getattr(args, "self_test_mode", "quick") or "quick").strip().lower()
    py_version = sys.version.split()[0]
    whisperx_version = package_version('whisperx')
    torch_version = package_version('torch')
    pyannote_version = package_version('pyannote.audio')
    eprint(f"INFO: versions -> python={py_version} whisperx={whisperx_version} torch={torch_version} pyannote.audio={pyannote_version}")

    if mode == "quick":
        eprint(f"INFO: self-test runtime -> device={args.device} compute_type={args.compute_type} model={args.model} mode={mode}")
        checks: Dict[str, Any] = {
            "ffmpeg_ok": True,
            "python_ok": True,
            "whisperx_package_ok": whisperx_version != "unknown",
            "torch_package_ok": torch_version != "unknown",
            "pyannote_package_ok": (not args.enable_diarization) or pyannote_version != "unknown",
            "token_ok": (not args.enable_diarization) or bool(hf_token),
        }
        messages: List[str] = []
        missing = [name for name in ("whisperx_package_ok", "torch_package_ok", "pyannote_package_ok") if not checks[name]]
        if missing:
            messages.append("Vereiste runtimepakketten ontbreken: " + ", ".join(missing))
            emit_selftest_summary(False, mode, messages, checks, args, python_version=py_version)
            return 3
        try:
            if args.enable_diarization:
                if not hf_token:
                    messages.append("Diarization self-test vereist een HF-token.")
                    emit_selftest_summary(False, mode, messages, checks, args, python_version=py_version)
                    eprint("ERROR: diarization self-test vereist een HF-token.")
                    return 2

                try:
                    from huggingface_hub import HfApi
                except Exception as exc:
                    eprint(f"ERROR: huggingface_hub import mislukt: {exc}")
                    eprint(traceback.format_exc())
                    messages.append(f"huggingface_hub import mislukt: {exc}")
                    emit_selftest_summary(False, mode, messages, checks, args, python_version=py_version)
                    return 3

                api = HfApi()
                api.whoami(token=hf_token)
                api.model_info(args.diarization_model, token=hf_token)
                checks["diarization_model_ok"] = True
                messages.append(f"Gated diarization-model bereikbaar: {args.diarization_model}")
                eprint(f"INFO: gated diarization-model bereikbaar: {args.diarization_model}")
            else:
                checks["diarization_model_ok"] = True

            messages.append("Quick self-test geslaagd.")
            emit_selftest_summary(True, mode, messages, checks, args, python_version=py_version)
            eprint("INFO: quick self-test geslaagd.")
            return 0
        except Exception as exc:
            eprint(f"ERROR: quick self-test mislukt: {exc}")
            eprint(traceback.format_exc())
            messages.append(f"Quick self-test mislukt: {exc}")
            emit_selftest_summary(False, mode, messages, checks, args, python_version=py_version)
            return 1

    try:
        import torch
        configure_torch_checkpoint_compatibility(torch)
        limit_torch_threads(torch)
        import whisperx
        if args.enable_diarization:
            import pyannote.audio  # noqa: F401
    except Exception as exc:
        eprint(f"ERROR: import self-test mislukt: {exc}")
        eprint(traceback.format_exc())
        emit_selftest_summary(False, mode, [f"Import self-test mislukt: {exc}"], {"ffmpeg_ok": True, "imports_ok": False}, args, python_version=py_version)
        return 3

    runtime = resolve_runtime_from_profile(args, torch)
    device = runtime["device"]
    compute_type = runtime["compute_type"]
    batch_size = int(runtime["batch_size"])
    if device == "cpu":
        args.model = choose_cpu_whisper_model(args.model)

    eprint(f"INFO: self-test runtime -> requested_profile={runtime['requested_profile']} active_profile={runtime['active_profile']} device={device} compute_type={compute_type} model={args.model} mode={mode}")
    if runtime.get("gpu_fallback_active"):
        eprint(f"WARN: self-test GPU fallback actief: {runtime.get('gpu_fallback_reason')}")

    temp_dir = tempfile.mkdtemp(prefix="local_capture_agent_selftest_")
    sample_path = os.path.join(temp_dir, "selftest.wav")
    try:
        write_test_wav(sample_path)
        mark_phase("model_load_started")
        model = load_whisperx_model(whisperx, args.model, device, compute_type, args.language)
        mark_phase("model_load_finished")
        audio = whisperx.load_audio(sample_path)
        result = model.transcribe(audio, batch_size=1, language=args.language)
        eprint(f"INFO: self-test ASR uitgevoerd. segment_count={len(result.get('segments', []))}")

        checks = {"ffmpeg_ok": True, "imports_ok": True, "asr_ok": True}
        messages = [f"Self-test ASR uitgevoerd. segment_count={len(result.get('segments', []))}"]

        if args.enable_diarization:
            if not hf_token:
                eprint("ERROR: diarization self-test vereist een HF-token.")
                messages.append("Diarization self-test vereist een HF-token.")
                checks["token_ok"] = False
                emit_selftest_summary(False, mode, messages, checks, args, python_version=py_version, runtime_device=device, runtime_compute_type=compute_type)
                return 2
            load_pyannote_pipeline(args.diarization_model, hf_token)
            checks["token_ok"] = True
            checks["diarization_model_ok"] = True
            eprint(f"INFO: gated diarization-model bereikbaar: {args.diarization_model}")
            messages.append(f"Gated diarization-model bereikbaar: {args.diarization_model}")

        eprint("INFO: self-test geslaagd.")
        messages.append("Self-test geslaagd.")
        emit_selftest_summary(True, mode, messages, checks, args, python_version=py_version, runtime_device=device, runtime_compute_type=compute_type)
        return 0
    except Exception as exc:
        eprint(f"ERROR: self-test mislukt: {exc}")
        eprint(traceback.format_exc())
        emit_selftest_summary(False, mode, [f"Self-test mislukt: {exc}"], {"ffmpeg_ok": True}, args, python_version=py_version, runtime_device=device, runtime_compute_type=compute_type)
        return 1
    finally:
        shutil.rmtree(temp_dir, ignore_errors=True)
        gc.collect()
        try:
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
        except Exception:
            pass




def format_srt_timestamp(seconds: float) -> str:
    total_ms = max(0, int(round(seconds * 1000)))
    hours, rem = divmod(total_ms, 3600000)
    minutes, rem = divmod(rem, 60000)
    secs, ms = divmod(rem, 1000)
    return f"{hours:02d}:{minutes:02d}:{secs:02d},{ms:03d}"


def format_vtt_timestamp(seconds: float) -> str:
    total_ms = max(0, int(round(seconds * 1000)))
    hours, rem = divmod(total_ms, 3600000)
    minutes, rem = divmod(rem, 60000)
    secs, ms = divmod(rem, 1000)
    return f"{hours:02d}:{minutes:02d}:{secs:02d}.{ms:03d}"


def build_subtitle_outputs(serializable: Dict[str, Any]) -> tuple[str, str]:
    # Subtitles must remain time-accurate. For non-diarized AMD/FFmpeg output all
    # segments have an unknown speaker; merging those into one long cue makes SRT
    # and VTT practically unusable. Keep unknown-speaker segments granular, while
    # still allowing short same-speaker merges when diarization labels exist.
    segments = merge_segments(
        serializable.get("segments", []),
        max_gap=0.8,
        max_duration=18.0,
        merge_unknown_speaker=False,
    )
    srt_lines: List[str] = []
    vtt_lines: List[str] = ["WEBVTT", ""]
    for index, seg in enumerate(segments, start=1):
        start = float(seg.get("start", 0.0) or 0.0)
        end = float(seg.get("end", start) or start)
        speaker = str(seg.get("speaker", "") or "").strip()
        text = str(seg.get("text", "") or "").strip()
        if not text:
            continue
        prefix = f"[{speaker}] " if speaker else ""
        srt_lines.append(str(index))
        srt_lines.append(f"{format_srt_timestamp(start)} --> {format_srt_timestamp(end)}")
        srt_lines.append(prefix + text)
        srt_lines.append("")
        vtt_lines.append(f"{format_vtt_timestamp(start)} --> {format_vtt_timestamp(end)}")
        if speaker:
            vtt_lines.append(f"<v {speaker}>{text}")
        else:
            vtt_lines.append(text)
        vtt_lines.append("")
    return "\n".join(srt_lines).rstrip() + ("\n" if srt_lines else ""), "\n".join(vtt_lines).rstrip() + ("\n" if vtt_lines else "")

def build_outputs(serializable: Dict[str, Any], output_format: str) -> tuple[Optional[str], Optional[str]]:
    merged = merge_segments(serializable.get("segments", []))
    speaker_map: Dict[str, str] = {}
    lines: List[str] = []
    # Zonder sprekerherkenning heeft geen enkel stuk een spreker; dan alleen de tijd, niet overal "Onbekend".
    has_speakers = any(str(seg.get("speaker") or "").strip() for seg in merged)
    for seg in merged:
        timestamp = f"[{format_ts(float(seg.get('start', 0.0) or 0.0))}]"
        lines.append(f"{timestamp} {simplify_speaker(seg.get('speaker'), speaker_map)}" if has_speakers else timestamp)
        lines.append(str(seg.get("text", "")).strip())
        lines.append("")
    text_output = "\n".join(lines).strip() + ("\n" if lines else "")
    json_output = json.dumps(serializable, ensure_ascii=False, indent=2)

    normalized = (output_format or "txt+json").lower()
    if normalized == "txt":
        return text_output, None
    if normalized == "json":
        return None, json_output
    return text_output, json_output




def atomic_write_text(path: str, text: str) -> None:
    target = Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    temp = target.with_name(target.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with open(temp, "w", encoding="utf-8", newline="") as output:
            output.write(text)
            output.flush()
            os.fsync(output.fileno())
        os.replace(temp, target)
    finally:
        temp.unlink(missing_ok=True)


def transcript_receipt_path(args) -> Path:
    path = args.output_json if getattr(args, "output_format", "txt+json") == "json" else args.output_txt
    return Path(path).parent / "transcript.outputs.json"


def write_transcript_artifacts(serializable: Dict[str, Any], args) -> None:
    """Publish a complete baseline/output set atomically, with hashes for recovery.

    A baseline remains recoverable if optional diarization fails afterwards.
    The receipt is removed before replacement, so a torn set is never complete.
    """
    receipt = transcript_receipt_path(args)
    receipt.unlink(missing_ok=True)
    # In elke route (WhisperX, FFmpeg Whisper, whisper.cpp): stukken zonder spraak niet in de tekst.
    remove_non_speech_segments(serializable)
    text_output, json_output = build_outputs(serializable, getattr(args, "output_format", "txt+json"))
    srt_output, vtt_output = build_subtitle_outputs(serializable)
    outputs = {}
    for kind, path, content in (("txt", args.output_txt, text_output), ("json", args.output_json, json_output)):
        if content is not None and path:
            atomic_write_text(path, content)
            data = Path(path).read_bytes()
            outputs[kind] = {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}
    for path, content in ((getattr(args, "output_srt", None), srt_output), (getattr(args, "output_vtt", None), vtt_output)):
        if path:
            atomic_write_text(path, content)
    atomic_write_text(str(receipt), json.dumps({"schema": "captivox-transcript-outputs-v1", "outputs": outputs}))


def mark_baseline_transcript_ready(serializable: Dict[str, Any], args, *, engine: str, requested_profile: str, active_profile: str, device: str, compute_type: str, batch_size: int, gpu_fallback_active: bool, gpu_fallback_reason: Optional[str], confirmed: bool, confirmation_field: str) -> None:
    """Markeer en schrijf de basistranscriptie voor diarization-postprocessing."""
    serializable["engine"] = engine
    serializable["requested_transcription_profile"] = requested_profile
    serializable["active_transcription_profile"] = active_profile
    serializable["runtime_device"] = device
    serializable["runtime_compute_type"] = compute_type
    serializable["runtime_batch_size"] = batch_size
    serializable["gpu_fallback_active"] = gpu_fallback_active
    serializable["gpu_fallback_reason"] = gpu_fallback_reason
    serializable[confirmation_field] = confirmed
    if getattr(args, "enable_diarization", False):
        serializable["diarization_requested"] = True
        serializable["diarization_status"] = "pending"
        serializable["diarization_requested_acceleration"] = normalize_diarization_acceleration(getattr(args, "diarization_acceleration", DIARIZATION_ACCELERATION_AUTO))
    else:
        serializable["diarization_requested"] = False
        serializable["diarization_status"] = "disabled"
        serializable["diarization_effective_acceleration"] = "disabled"
    serializable["captivox_schema_version"] = "captivox-transcript-v1"
    serializable["generated_at"] = utc_now_z()
    write_transcript_artifacts(serializable, args)
    eprint("INFO: basistranscriptie weggeschreven voor diarization-postprocessing.")


def utc_now_z() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def atomic_write_json(path: str, payload: Dict[str, Any]) -> None:
    """Best-effort atomic JSON write for live helper progress.

    Antivirus, file indexers or diagnostics export can briefly lock the progress
    file on Windows. A heartbeat write must never crash the helper; losing one
    progress update is acceptable, losing the transcription run is not.
    """
    if not path:
        return
    target = Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    tmp = target.with_name(f"{target.name}.{os.getpid()}.{uuid.uuid4().hex}.tmp")
    payload_text = json.dumps(json_safe(payload), ensure_ascii=False, indent=2)
    last_exc: Optional[Exception] = None
    try:
        for attempt in range(5):
            try:
                tmp.write_text(payload_text, encoding="utf-8")
                os.replace(tmp, target)
                return
            except PermissionError as exc:
                last_exc = exc
                time.sleep(0.05 * (attempt + 1))
            except OSError as exc:
                # Windows can surface transient sharing violations as OSError.
                last_exc = exc
                time.sleep(0.05 * (attempt + 1))
        eprint(f"WARN: helper_progress.json tijdelijk niet schrijfbaar; heartbeat overgeslagen: {last_exc}")
    finally:
        try:
            if tmp.exists():
                tmp.unlink()
        except Exception:
            pass


class HelperProgressReporter:
    def __init__(self, path: Optional[str]):
        self._path = path
        self._lock = threading.Lock()
        self._stop = threading.Event()
        self._thread: Optional[threading.Thread] = None
        self._last_emit_monotonic = 0.0
        now = utc_now_z()
        self._payload: Dict[str, Any] = {
            "state": "starting",
            "current_phase": None,
            "phase_started_at": None,
            "last_completed_phase": None,
            "last_heartbeat_at": now,
            "last_activity_at": now,
            "last_progress_event_at": None,
            "started_at": now,
            "pid": os.getpid(),
            "error": None,
            "activity_seq": 0,
            "phase_progress": {},
            "phases": {},
        }

    def start(self) -> None:
        if not self._path:
            return
        self._emit_locked(force=True)
        self._thread = threading.Thread(target=self._heartbeat_loop, name="captivox-helper-heartbeat", daemon=True)
        self._thread.start()

    def _heartbeat_loop(self) -> None:
        while not self._stop.wait(5.0):
            with self._lock:
                self._payload["last_heartbeat_at"] = utc_now_z()
                phase_started_at = self._payload.get("phase_started_at")
                if phase_started_at:
                    try:
                        started = datetime.fromisoformat(str(phase_started_at).replace("Z", "+00:00"))
                        self._payload["phase_elapsed_sec"] = round((datetime.now(timezone.utc) - started).total_seconds(), 3)
                    except Exception:
                        self._payload["phase_elapsed_sec"] = None
                self._emit_locked()

    def _mark_activity_locked(self, now: str) -> None:
        self._payload["last_activity_at"] = now
        self._payload["activity_seq"] = int(self._payload.get("activity_seq") or 0) + 1

    def update_phase(self, phase: str, state: str = "running") -> None:
        with self._lock:
            now = utc_now_z()
            self._payload["state"] = state
            self._payload["current_phase"] = phase
            self._payload["phase_started_at"] = now
            self._payload["last_heartbeat_at"] = now
            self._payload["phase_elapsed_sec"] = 0.0
            self._payload["phase_progress"] = {}
            self._mark_activity_locked(now)
            phases = self._payload.setdefault("phases", {})
            phase_state = phases.setdefault(phase, {})
            phase_state["started_at"] = now
            self._emit_locked(force=True)

    def set_metadata(self, **values: Any) -> None:
        with self._lock:
            now = utc_now_z()
            for key, value in values.items():
                self._payload[key] = value
            self._payload["last_heartbeat_at"] = now
            self._mark_activity_locked(now)
            self._emit_locked(force=True)

    def report_progress(self, phase: str, step_name: str, completed: Optional[int], total: Optional[int]) -> None:
        with self._lock:
            now = utc_now_z()
            phase_progress = {
                "step_name": step_name,
                "completed": int(completed) if completed is not None else None,
                "total": int(total) if total is not None else None,
                "updated_at": now,
            }
            self._payload["phase_progress"] = phase_progress
            self._payload["last_progress_event_at"] = now
            if phase and self._payload.get("current_phase") != phase:
                self._payload["current_phase"] = phase
            self._mark_activity_locked(now)
            phases = self._payload.setdefault("phases", {})
            phase_state = phases.setdefault(phase or "unknown", {})
            phase_state["last_progress"] = phase_progress
            self._emit_locked()

    def finish_phase(self, phase: str) -> None:
        with self._lock:
            now = utc_now_z()
            phases = self._payload.setdefault("phases", {})
            phase_state = phases.setdefault(phase, {})
            phase_state["finished_at"] = now
            self._payload["last_completed_phase"] = phase
            self._payload["last_heartbeat_at"] = now
            self._payload["phase_progress"] = {}
            self._mark_activity_locked(now)
            self._emit_locked(force=True)

    def complete(self) -> None:
        with self._lock:
            now = utc_now_z()
            self._payload["state"] = "completed"
            self._payload["last_heartbeat_at"] = now
            self._payload["completed_at"] = now
            self._mark_activity_locked(now)
            self._emit_locked(force=True)
        self.stop()

    def fail(self, message: str) -> None:
        with self._lock:
            now = utc_now_z()
            self._payload["state"] = "failed"
            self._payload["error"] = message
            self._payload["last_heartbeat_at"] = now
            self._payload["failed_at"] = now
            self._mark_activity_locked(now)
            self._emit_locked(force=True)
        self.stop()

    def stop(self) -> None:
        self._stop.set()
        if self._thread and self._thread.is_alive():
            self._thread.join(timeout=1.0)

    def _emit_locked(self, force: bool = False) -> None:
        if not self._path:
            return
        now_monotonic = time.monotonic()
        if not force and (now_monotonic - self._last_emit_monotonic) < 1.0:
            return
        atomic_write_json(self._path, self._payload)
        self._last_emit_monotonic = now_monotonic


class PyannoteProgressBridge:
    def __init__(self, reporter: HelperProgressReporter, phase: str):
        self._reporter = reporter
        self._phase = phase

    def __enter__(self):
        return self

    def __exit__(self, *args):
        return None

    def __call__(self, step_name, step_artifact, file=None, total=None, completed=None):
        step = str(step_name) if step_name is not None else "unknown"
        completed_value = int(completed) if completed is not None else None
        total_value = int(total) if total is not None else None
        self._reporter.report_progress(self._phase, step, completed_value, total_value)


def main() -> int:
    parser = argparse.ArgumentParser(description="Transcribe one recorder session file with WhisperX and optional diarization.")
    parser.add_argument("--input", help="Input audio/video file")
    parser.add_argument("--output-txt", help="Output transcript TXT path")
    parser.add_argument("--output-json", help="Output raw JSON path")
    parser.add_argument("--output-srt", help="Optional SRT export path")
    parser.add_argument("--output-vtt", help="Optional WebVTT export path")
    parser.add_argument("--timings-json", help="Optional phase timings JSON path")
    parser.add_argument("--progress-json", help="Optional live helper progress JSON path")
    parser.add_argument("--language", default="nl", help="Whisper language code, default nl")
    parser.add_argument("--model", default="large-v2", help="Whisper model name")
    parser.add_argument("--batch-size", type=int, default=4, help="WhisperX batch size")
    parser.add_argument("--compute-type", default="float16", help="float16 or int8")
    parser.add_argument("--device", default="auto", help="auto, cuda, or cpu")
    parser.add_argument("--transcription-profile", default="auto", help="auto, nvidia-cuda, amd-gpu, or cpu-fallback")
    parser.add_argument("--whispercpp-exe", default="", help="Optional whisper.cpp whisper-cli.exe path for AMD/Vulkan transcription")
    parser.add_argument("--whispercpp-model", default="", help="Optional whisper.cpp ggml model path for AMD/Vulkan transcription")
    parser.add_argument("--min-speakers", type=int, default=2)
    parser.add_argument("--max-speakers", type=int, default=4)
    parser.add_argument("--hf-token", default=None, help="Deprecated. Ignored for token safety; use HF_TOKEN environment variable.")
    parser.add_argument("--enable-diarization", default="true", help="true/false")
    parser.add_argument("--diarization-model", default=PYANNOTE_MAINWORKER_CPU_MODEL, help="Diarization model id")
    parser.add_argument("--diarization-acceleration", default=DIARIZATION_ACCELERATION_AUTO, help="cpu, auto, amd-rocm-experimental, or nvidia-cuda-experimental")
    parser.add_argument("--output-format", default="txt+json", help="txt+json, txt, or json")
    parser.add_argument("--ffmpeg-path", default=os.environ.get("FFMPEG_PATH", "ffmpeg"), help="Path to ffmpeg executable")
    parser.add_argument("--initial-prompt", default=None, help="Optional prompt/context for transcription")
    parser.add_argument("--self-test", action="store_true", help="Run environment and model self-test")
    parser.add_argument("--self-test-mode", default="quick", choices=["quick", "full"], help="Self-test scope: quick or full")
    args = parser.parse_args()

    configure_cpu_friendliness()
    args.enable_diarization = str(args.enable_diarization).strip().lower() in {"1", "true", "yes", "y"}
    args.diarization_acceleration = normalize_diarization_acceleration(args.diarization_acceleration)
    if args.hf_token:
        eprint("WARN: legacy hf-token argument is genegeerd voor tokenveiligheid; gebruik HF_TOKEN als environment variable.")
    hf_token = os.environ.get("HF_TOKEN") or os.environ.get("HUGGINGFACE_TOKEN")

    if args.self_test:
        return run_self_test(args, hf_token)

    if not args.input or not args.output_txt or not args.output_json:
        eprint("ERROR: --input, --output-txt en --output-json zijn verplicht buiten self-test modus.")
        return 2

    os.makedirs(os.path.dirname(args.output_txt), exist_ok=True)
    os.makedirs(os.path.dirname(args.output_json), exist_ok=True)
    if args.output_srt:
        os.makedirs(os.path.dirname(args.output_srt), exist_ok=True)
    if args.output_vtt:
        os.makedirs(os.path.dirname(args.output_vtt), exist_ok=True)
    if args.timings_json:
        os.makedirs(os.path.dirname(args.timings_json), exist_ok=True)

    transcript_receipt_path(args).unlink(missing_ok=True)
    progress = HelperProgressReporter(args.progress_json)
    progress.start()

    warnings.filterwarnings("ignore", message=r".*torchcodec is not installed correctly.*", category=UserWarning)
    warnings.filterwarnings("ignore", message=r".*TensorFloat-32 \(TF32\) has been disabled.*")
    warnings.filterwarnings("ignore", message=r".*std\(\): degrees of freedom is <= 0.*")
    warnings.filterwarnings("ignore", message=r".*Lightning automatically upgraded your loaded checkpoint.*")

    phase_timings = {}

    def mark_phase(name: str):
        phase_timings[name] = time.perf_counter()

    def begin_phase(name: str):
        mark_phase(f"{name}_started")
        progress.update_phase(name)
        eprint(f"PHASE:{name}:started")

    def end_phase(name: str):
        mark_phase(f"{name}_finished")
        progress.finish_phase(name)
        eprint(f"PHASE:{name}:finished")

    def duration_ms(start_name: str, end_name: str):
        start = phase_timings.get(start_name)
        end = phase_timings.get(end_name)
        if start is None or end is None:
            return None
        return round((end - start) * 1000.0, 3)

    mark_phase("helper_started")
    begin_phase("bootstrap")
    end_phase("bootstrap")

    try:
        begin_phase("ffmpeg_check")
        ensure_ffmpeg(args.ffmpeg_path)
        end_phase("ffmpeg_check")

        pre_requested_profile = normalize_transcription_profile(getattr(args, "transcription_profile", "auto"))
        if pre_requested_profile == "amd-gpu":
            pre_probe = build_runtime_backend_probe(None, args)
            write_runtime_backend_probe(args, pre_probe)
            ffmpeg_probe = pre_probe.get("ffmpeg_whisper", {}) if isinstance(pre_probe, dict) else {}
            whispercpp_probe = pre_probe.get("whispercpp_vulkan", {}) if isinstance(pre_probe, dict) else {}
            if isinstance(ffmpeg_probe, dict) and ffmpeg_probe.get("available"):
                try:
                    return run_ffmpeg_whisper_gpu_engine(args, progress, phase_timings, mark_phase, begin_phase, end_phase, duration_ms, hf_token)
                except Exception as amd_exc:
                    eprint(f"WARN: AMD FFmpeg Whisper GPU-engine faalde; secundaire whisper.cpp Vulkan-route wordt geprobeerd indien beschikbaar: {amd_exc}")
                    eprint(traceback.format_exc())
            if isinstance(whispercpp_probe, dict) and whispercpp_probe.get("available"):
                try:
                    return run_whispercpp_vulkan_engine(args, progress, phase_timings, mark_phase, begin_phase, end_phase, duration_ms, hf_token)
                except Exception as amd_exc:
                    eprint(f"WARN: AMD whisper.cpp Vulkan-engine faalde; WhisperX CPU fallback wordt geprobeerd: {amd_exc}")
                    eprint(traceback.format_exc())
                    args.whispercpp_disabled_reason = f"whisper.cpp Vulkan-engine faalde eerder in deze run: {amd_exc}"
                    args.whispercpp_exe = ""
                    args.whispercpp_model = ""
            else:
                eprint(f"WARN: AMD-profiel geselecteerd, maar geen AMD GPU-engine is beschikbaar: ffmpeg={ffmpeg_probe.get('reason') if isinstance(ffmpeg_probe, dict) else 'geen probe'}; whispercpp={whispercpp_probe.get('reason') if isinstance(whispercpp_probe, dict) else 'geen probe'}")

        # Geen AMD-engine beschikbaar of gelukt: WhisperX op de processor met het CPU-model (standaard large-v3-turbo),
        # niet met de plaatsnaam van de AMD-route (die kent WhisperX niet).
        if args.model in ("ffmpeg-whisper-gpu", "whispercpp-vulkan"):
            args.model = ((os.environ.get("CAPTIVOX_WHISPERX_CPU_MODEL") or "").strip()
                          or (os.environ.get("CAPTIVOX_WHISPERX_MODEL") or "").strip()
                          or DEFAULT_CPU_WHISPERX_MODEL)
            eprint(f"INFO: AMD-route niet gebruikt; WhisperX-model voor de terugval: {args.model}")
        begin_phase("dependency_imports")
        import numpy as np
        import pandas as pd  # noqa: F401
        import torch
        configure_torch_checkpoint_compatibility(torch)
        limit_torch_threads(torch)
        import whisperx
        if args.enable_diarization:
            from pyannote.audio import Pipeline
        else:
            Pipeline = None  # type: ignore
        end_phase("dependency_imports")
    except Exception as exc:
        progress.fail(str(exc))
        eprint(f"ERROR: WhisperX/pyannote import of bootstrap mislukt: {exc}")
        eprint(traceback.format_exc())
        return 3

    try:
        begin_phase("runtime_resolve")
        runtime = resolve_runtime_from_profile(args, torch)
        device = runtime["device"]
        compute_type = runtime["compute_type"]
        active_profile = runtime["active_profile"]
        requested_profile = runtime["requested_profile"]
        batch_size = int(runtime["batch_size"])
        gpu_fallback_active = bool(runtime.get("gpu_fallback_active"))
        gpu_fallback_reason = runtime.get("gpu_fallback_reason")
        runtime_capabilities = runtime.get("capabilities", {})
        runtime_backend_probe = runtime.get("backend_probe", {})
        if device == "cpu":
            args.model = choose_cpu_whisper_model(args.model)
        eprint(f"INFO: requested_profile={requested_profile}, active_profile={active_profile}, device={device}, model={args.model}, batch_size={batch_size}, compute_type={compute_type}, diarization={args.enable_diarization}")
        if gpu_fallback_active and gpu_fallback_reason:
            eprint(f"WARN: GPU fallback actief: {gpu_fallback_reason}")
        progress.set_metadata(
            runtime_device=device,
            runtime_compute_type=compute_type,
            requested_compute_type=args.compute_type,
            requested_transcription_profile=requested_profile,
            active_transcription_profile=active_profile,
            gpu_fallback_active=gpu_fallback_active,
            gpu_fallback_reason=gpu_fallback_reason,
            cuda_available=runtime_capabilities.get("cuda_available"),
            cuda_device_names=runtime_capabilities.get("cuda_device_names"),
            torch_cuda_version=runtime_capabilities.get("cuda_version"),
            torch_hip_version=runtime_capabilities.get("hip_version"),
            ctranslate2_version=runtime_backend_probe.get("ctranslate2", {}).get("ctranslate2_version") if isinstance(runtime_backend_probe, dict) else None,
            ctranslate2_cuda_available=runtime_backend_probe.get("ctranslate2", {}).get("ct2_cuda_available") if isinstance(runtime_backend_probe, dict) else None,
            model=args.model)
        end_phase("runtime_resolve")

        if device == "vulkan":
            backend_probe_for_engine = build_runtime_backend_probe(None, args)
            ffmpeg_probe = backend_probe_for_engine.get("ffmpeg_whisper", {}) if isinstance(backend_probe_for_engine, dict) else {}
            if isinstance(ffmpeg_probe, dict) and ffmpeg_probe.get("available"):
                return run_ffmpeg_whisper_gpu_engine(args, progress, phase_timings, mark_phase, begin_phase, end_phase, duration_ms, hf_token)
            return run_whispercpp_vulkan_engine(args, progress, phase_timings, mark_phase, begin_phase, end_phase, duration_ms, hf_token)

        begin_phase("audio_load")
        audio = whisperx.load_audio(args.input)
        end_phase("audio_load")
        def load_and_transcribe(current_device: str, current_compute_type: str, current_batch_size: int):
            begin_phase("model_load")
            loaded_model = load_whisperx_model(whisperx, args.model, current_device, current_compute_type, args.language)
            end_phase("model_load")
            local_transcribe_kwargs = {"batch_size": current_batch_size, "language": args.language}
            if args.initial_prompt:
                local_transcribe_kwargs["initial_prompt"] = args.initial_prompt
            begin_phase("transcribe")
            local_result = loaded_model.transcribe(audio, **local_transcribe_kwargs)
            end_phase("transcribe")
            return loaded_model, local_result

        try:
            model, result = load_and_transcribe(device, compute_type, batch_size)
        except Exception as gpu_exc:
            if device != "cpu" and is_gpu_runtime_failure(gpu_exc):
                eprint(f"WARN: GPU-transcriptie faalde; CPU fallback wordt geprobeerd: {gpu_exc}")
                if "model_load_started" in phase_timings and "model_load_finished" not in phase_timings:
                    end_phase("model_load")
                if "transcribe_started" in phase_timings and "transcribe_finished" not in phase_timings:
                    end_phase("transcribe")
                gpu_fallback_active = True
                gpu_fallback_reason = f"GPU-runtime faalde: {gpu_exc}"
                active_profile = "cpu-fallback"
                device = "cpu"
                compute_type = "int8"
                batch_size = min(batch_size, 2)
                args.model = choose_cpu_whisper_model(args.model)
                progress.set_metadata(
                    runtime_device=device,
                    runtime_compute_type=compute_type,
                    active_transcription_profile=active_profile,
                    gpu_fallback_active=True,
                    gpu_fallback_reason=gpu_fallback_reason,
                    model=args.model)
                model, result = load_and_transcribe(device, compute_type, batch_size)
            else:
                raise
        detected_language = result.get("language") or args.language or "nl"
        eprint(f"INFO: detected_language={detected_language}")

        begin_phase("align_load")
        model_a, metadata = whisperx.load_align_model(language_code=detected_language, device=device)
        end_phase("align_load")
        begin_phase("align")
        result = whisperx.align(result["segments"], model_a, metadata, audio, device, return_char_alignments=False)
        end_phase("align")
        del model_a
        del metadata
        del model
        gc.collect()
        if device == "cuda":
            torch.cuda.empty_cache()

        serializable: Dict[str, Any] = json_safe(result)
        diarization_applied = False
        diarization_warning = None
        diarization_status = "disabled" if not args.enable_diarization else "pending"
        requested_diarization_model = args.diarization_model if args.enable_diarization else None
        effective_diarization_model = requested_diarization_model
        pyannote_audio_version = None
        diarization_requested_acceleration = normalize_diarization_acceleration(getattr(args, "diarization_acceleration", DIARIZATION_ACCELERATION_AUTO))
        diarization_effective_acceleration = "disabled" if not args.enable_diarization else ("nvidia-cuda" if device == "cuda" else "cpu")
        diarization_gpu_available = bool(device == "cuda")
        diarization_gpu_validated = bool(device == "cuda")
        diarization_gpu_device_name = None
        try:
            if device == "cuda":
                diarization_gpu_device_name = torch.cuda.get_device_name(0)
        except Exception:
            diarization_gpu_device_name = None
        diarization_runtime_python = sys.executable if args.enable_diarization else None
        diarization_runtime_torch = package_version("torch") if args.enable_diarization else None
        diarization_runtime_torchaudio = package_version("torchaudio") if args.enable_diarization else None
        diarization_runtime_torch_cuda = getattr(getattr(torch, "version", None), "cuda", None) if args.enable_diarization else None
        diarization_runtime_torch_hip = getattr(getattr(torch, "version", None), "hip", None) if args.enable_diarization else None
        diarization_model_compatibility_fallback_active = False
        diarization_model_compatibility_fallback_reason = None
        # Zonder sprekerherkenning (of met de CPU-route) zet niemand deze waarden; ze gaan wel in transcript.json.
        rocm_lstm_dropout_patch_applied = None
        rocm_lstm_modules_patched = None
        rocm_lstm_dropout_original_values = None
        rocm_lstm_dropout_patch_warning = None
        gpu_inference_smoketest_ok = None
        gpu_inference_smoketest_warning = None
        if args.enable_diarization:
            if not hf_token:
                eprint("ERROR: geen Hugging Face token gevonden. Zet HF_TOKEN als environment variable.")
                return 2

            waveform = torch.from_numpy(np.asarray(audio)[None, :]).float().cpu()
            diarization_input = {"waveform": waveform, "sample_rate": 16000}
            try:
                model_resolution = resolve_diarization_model_for_runtime(args.diarization_model)
                effective_diarization_model = model_resolution["effective_model"]
                requested_diarization_model = model_resolution["requested_model"]
                pyannote_audio_version = model_resolution.get("pyannote_audio_version")
                diarization_model_compatibility_fallback_active = bool(model_resolution.get("compatibility_fallback_active"))
                diarization_model_compatibility_fallback_reason = model_resolution.get("compatibility_fallback_reason")
                if model_resolution.get("compatibility_fallback_reason"):
                    eprint(f"WARN: {model_resolution.get('compatibility_fallback_reason')}")
                    progress.set_metadata(
                        diarization_requested_model=requested_diarization_model,
                        diarization_effective_model=effective_diarization_model,
                        pyannote_audio_version=model_resolution.get("pyannote_audio_version"),
                        diarization_model_compatibility_fallback_active=True,
                        diarization_model_compatibility_fallback_reason=model_resolution.get("compatibility_fallback_reason"))
                eprint(f"INFO: diarization pipeline laden: {effective_diarization_model}")
                begin_phase("diarization_load")
                diarize_model = load_pyannote_pipeline(effective_diarization_model, hf_token)
                end_phase("diarization_load")
                if device == "cuda":
                    diarize_model.to(torch.device("cuda"))
                diarize_kwargs = {}
                if args.min_speakers > 0:
                    diarize_kwargs["min_speakers"] = args.min_speakers
                if args.max_speakers > 0:
                    diarize_kwargs["max_speakers"] = args.max_speakers
                begin_phase("diarization")
                diarization_hook = PyannoteProgressBridge(progress, "diarization")
                try:
                    with diarization_hook as hook:
                        diarization_output = diarize_model(diarization_input, hook=hook, **diarize_kwargs)
                except TypeError as hook_exc:
                    if "hook" not in str(hook_exc).lower():
                        raise
                    eprint("WARN: pyannote hook niet ondersteund in deze omgeving; diarization loopt door zonder subprogress.")
                    diarization_output = diarize_model(diarization_input, **diarize_kwargs)
                end_phase("diarization")
                diarize_df = annotation_to_df(diarization_output)
                if diarize_df.empty:
                    diarization_warning = "diarization gaf geen speakersegmenten terug"
                    diarization_status = "empty"
                    eprint("WARN: diarization gaf geen speakersegmenten terug; transcript wordt zonder speakerlabels opgeslagen.")
                else:
                    result = whisperx.assign_word_speakers(diarize_df, result)
                    serializable = json_safe(result)
                    diarization_applied = True
                    diarization_status = "completed"
                del diarize_model
                del diarization_output
                del diarize_df
                del waveform
                gc.collect()
                if device == "cuda":
                    torch.cuda.empty_cache()
            except Exception as diarization_exc:
                if "diarization_load_started" in phase_timings and "diarization_load_finished" not in phase_timings:
                    end_phase("diarization_load")
                if "diarization_started" in phase_timings and "diarization_finished" not in phase_timings:
                    end_phase("diarization")
                diarization_warning = format_diarization_exception(diarization_exc)
                diarization_status = "failed"
                eprint(f"WARN: diarization mislukt; transcriptie gaat verder zonder speakerlabels: {diarization_warning}")
                eprint(traceback.format_exc())
        else:
            diarization_status = "disabled"
            eprint("INFO: diarization overgeslagen op basis van instellingen.")

        removed_intro_noise = remove_known_intro_noise(serializable)
        if removed_intro_noise:
            eprint(f"INFO: bekende openingsruis verwijderd uit transcriptsegmenten: {removed_intro_noise}")

        serializable["diarization_requested"] = bool(args.enable_diarization)
        serializable["diarization_engine"] = "pyannote" if args.enable_diarization else None
        serializable["diarization_device"] = device if args.enable_diarization else None
        serializable["diarization_gpu_backend"] = "cuda" if args.enable_diarization and device == "cuda" else None
        base_diarization_fallback_active = bool(args.enable_diarization and device != "cuda")
        base_diarization_fallback_reason = "diarization draait op CPU omdat transcriptieruntime geen CUDA gebruikt" if args.enable_diarization and device != "cuda" else None
        serializable["diarization_fallback_active"] = bool(base_diarization_fallback_active or diarization_model_compatibility_fallback_active)
        serializable["diarization_fallback_reason"] = " | ".join([reason for reason in [base_diarization_fallback_reason, diarization_model_compatibility_fallback_reason] if reason]) or None
        serializable["diarization_assigned_segments"] = sum(1 for _seg in serializable.get("segments", []) if isinstance(_seg, dict) and _seg.get("speaker")) if args.enable_diarization else 0
        serializable["diarization_speaker_count"] = len({str(_seg.get("speaker")) for _seg in serializable.get("segments", []) if isinstance(_seg, dict) and _seg.get("speaker")}) if args.enable_diarization else 0
        serializable["diarization_applied"] = diarization_applied
        serializable["diarization_status"] = diarization_status
        serializable["diarization_model"] = effective_diarization_model if args.enable_diarization else None
        serializable["diarization_requested_model"] = requested_diarization_model
        serializable["diarization_effective_model"] = effective_diarization_model
        serializable["pyannote_audio_version"] = pyannote_audio_version
        serializable["diarization_model_compatibility_fallback_active"] = diarization_model_compatibility_fallback_active
        serializable["diarization_model_compatibility_fallback_reason"] = diarization_model_compatibility_fallback_reason
        serializable["diarization_warning"] = diarization_warning
        serializable["diarization_requested_acceleration"] = diarization_requested_acceleration
        serializable["diarization_effective_acceleration"] = diarization_effective_acceleration
        serializable["diarization_gpu_available"] = diarization_gpu_available
        serializable["diarization_gpu_validated"] = diarization_gpu_validated
        serializable["diarization_gpu_device_name"] = diarization_gpu_device_name
        serializable["rocm_lstm_dropout_patch_applied"] = rocm_lstm_dropout_patch_applied
        serializable["rocm_lstm_modules_patched"] = rocm_lstm_modules_patched
        serializable["rocm_lstm_dropout_original_values"] = rocm_lstm_dropout_original_values
        serializable["rocm_lstm_dropout_patch_warning"] = rocm_lstm_dropout_patch_warning
        serializable["gpu_inference_smoketest_ok"] = gpu_inference_smoketest_ok
        serializable["gpu_inference_smoketest_warning"] = gpu_inference_smoketest_warning
        serializable["diarization_runtime_python"] = diarization_runtime_python
        serializable["diarization_runtime_torch"] = diarization_runtime_torch
        serializable["diarization_runtime_torchaudio"] = diarization_runtime_torchaudio
        serializable["diarization_runtime_torch_cuda"] = diarization_runtime_torch_cuda
        serializable["diarization_runtime_torch_hip"] = diarization_runtime_torch_hip
        serializable["requested_transcription_profile"] = requested_profile
        serializable["active_transcription_profile"] = active_profile
        serializable["runtime_device"] = device
        serializable["runtime_compute_type"] = compute_type
        serializable["runtime_batch_size"] = batch_size
        serializable["gpu_fallback_active"] = gpu_fallback_active
        serializable["gpu_fallback_reason"] = gpu_fallback_reason
        serializable["captivox_schema_version"] = "captivox-transcript-v1"
        serializable["generated_at"] = utc_now_z()

        begin_phase("export")
        write_transcript_artifacts(serializable, args)
        end_phase("export")
        mark_phase("helper_finished")
        progress.complete()
        if args.timings_json:
            timings_payload = {
                "device": device,
                "compute_type": compute_type,
                "model": args.model,
                "requested_transcription_profile": requested_profile,
                "active_transcription_profile": active_profile,
                "batch_size": batch_size,
                "gpu_fallback_active": gpu_fallback_active,
                "gpu_fallback_reason": gpu_fallback_reason,
                "runtime_capabilities": json_safe(runtime_capabilities),
                "runtime_backend_probe": json_safe(runtime_backend_probe),
                "diarization_requested": bool(args.enable_diarization),
                "diarization_model": effective_diarization_model if args.enable_diarization else None,
                "diarization_requested_model": requested_diarization_model,
                "diarization_effective_model": effective_diarization_model,
                "pyannote_audio_version": pyannote_audio_version,
                "diarization_model_compatibility_fallback_active": diarization_model_compatibility_fallback_active,
                "diarization_model_compatibility_fallback_reason": diarization_model_compatibility_fallback_reason,
                "diarization_status": diarization_status,
                "diarization_applied": diarization_applied,
                "diarization_warning": diarization_warning,
                "diarization_engine": serializable.get("diarization_engine"),
                "diarization_device": serializable.get("diarization_device"),
                "diarization_gpu_backend": serializable.get("diarization_gpu_backend"),
                "diarization_fallback_active": serializable.get("diarization_fallback_active"),
                "diarization_fallback_reason": serializable.get("diarization_fallback_reason"),
                "diarization_assigned_segments": serializable.get("diarization_assigned_segments"),
                "diarization_speaker_count": serializable.get("diarization_speaker_count"),
                "diarization_requested_acceleration": serializable.get("diarization_requested_acceleration"),
                "diarization_effective_acceleration": serializable.get("diarization_effective_acceleration"),
                "diarization_gpu_available": serializable.get("diarization_gpu_available"),
                "diarization_gpu_validated": serializable.get("diarization_gpu_validated"),
                "diarization_gpu_device_name": serializable.get("diarization_gpu_device_name"),
                "diarization_runtime_python": serializable.get("diarization_runtime_python"),
                "diarization_runtime_torch": serializable.get("diarization_runtime_torch"),
                "diarization_runtime_torchaudio": serializable.get("diarization_runtime_torchaudio"),
                "diarization_runtime_torch_cuda": serializable.get("diarization_runtime_torch_cuda"),
                "diarization_runtime_torch_hip": serializable.get("diarization_runtime_torch_hip"),
                "durations_ms": {
                    "audio_load": duration_ms("audio_load_started", "audio_load_finished"),
                    "model_load": duration_ms("model_load_started", "model_load_finished"),
                    "transcribe": duration_ms("transcribe_started", "transcribe_finished"),
                    "align_load": duration_ms("align_load_started", "align_load_finished"),
                    "align": duration_ms("align_started", "align_finished"),
                    "diarization_load": duration_ms("diarization_load_started", "diarization_load_finished"),
                    "diarization": duration_ms("diarization_started", "diarization_finished"),
                    "export": duration_ms("export_started", "export_finished"),
                    "helper_total": duration_ms("helper_started", "helper_finished"),
                }
            }
            with open(args.timings_json, "w", encoding="utf-8") as f_timing:
                json.dump(json_safe(timings_payload), f_timing, ensure_ascii=False, indent=2)
            eprint(f"INFO: phase timings opgeslagen: {args.timings_json}")
        return 0
    except Exception as exc:
        progress.fail(str(exc))
        eprint(f"ERROR: transcriptie mislukt: {exc}")
        eprint(traceback.format_exc())
        return 1
    finally:
        progress.stop()
        gc.collect()
        try:
            import torch as _torch  # type: ignore
            if _torch.cuda.is_available():
                _torch.cuda.empty_cache()
        except Exception:
            pass


if __name__ == "__main__":
    raise SystemExit(main())
