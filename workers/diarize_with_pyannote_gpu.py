import argparse
import gc
import importlib.metadata
import json
import os
import re
import sys
import traceback
import warnings
import wave
from pathlib import Path
from typing import Any, Dict, List, Optional

from pyannote_gpu_compat import apply_torchaudio_compatibility

warnings.filterwarnings("ignore", category=UserWarning, module=r"pyannote\.audio\.core\.io")
warnings.filterwarnings("ignore", message=r"(?s).*torchcodec is not installed correctly.*", category=UserWarning)
warnings.filterwarnings("ignore", message=r".*TensorFloat-32 \(TF32\) has been disabled.*")
os.environ.setdefault("TORCH_FORCE_NO_WEIGHTS_ONLY_LOAD", "1")
os.environ.setdefault("PYANNOTE_METRICS_ENABLED", "0")


def eprint(*args, **kwargs):
    kwargs.setdefault("flush", True)
    print(*args, file=sys.stderr, **kwargs)


def package_version(package_name: str) -> str:
    try:
        return importlib.metadata.version(package_name)
    except Exception:
        return "unknown"


def write_json(path: str, payload: Dict[str, Any]) -> None:
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, ensure_ascii=False, indent=2)


def sanitize_error(exc: Exception) -> str:
    raw = str(exc) if exc is not None else ""
    raw = re.sub(r"https://huggingface\.co/[^\s]+", "https://huggingface.co/[redacted]", raw)
    raw = re.sub(r"[A-Za-z]:\\[^\r\n]+", "[REDACTED_LOCAL_PATH]", raw)
    raw = re.sub(r"hf_[A-Za-z0-9_\-]+", "hf_[redacted]", raw)
    return raw or "Onbekende GPU-diarization-fout."


def install_huggingface_hub_token_compatibility() -> None:
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
        eprint(f"WARN: huggingface_hub compatibiliteit niet toegepast: {sanitize_error(exc)}")


def configure_torch_checkpoint_compatibility(torch_module: Any) -> None:
    try:
        from omegaconf.dictconfig import DictConfig
        from omegaconf.listconfig import ListConfig

        serialization = getattr(torch_module, "serialization", None)
        add_safe_globals = getattr(serialization, "add_safe_globals", None) if serialization is not None else None
        if callable(add_safe_globals):
            add_safe_globals([DictConfig, ListConfig])
    except Exception as exc:
        eprint(f"WARN: torch checkpoint compatibility setup overgeslagen: {sanitize_error(exc)}")


def apply_rocm_lstm_inference_patch(pipeline: Any, torch_module: Any, requested_acceleration: str) -> Dict[str, Any]:
    """Disable LSTM dropout during AMD ROCm inference.

    Pyannote 3.1 contains LSTM modules with non-zero dropout. On Windows ROCm
    this can route inference through an MIOpen dropout kernel that tries to JIT
    compile hiprand/rocrand headers that are not present in the pip sidecar.
    Dropout is not active for evaluation inference, so setting the module
    attribute to 0.0 avoids the broken ROCm code path without changing the
    diarization model weights or CPU/NVIDIA behavior.
    """
    info: Dict[str, Any] = {
        "rocm_lstm_dropout_patch_requested": requested_acceleration == "amd-rocm",
        "rocm_lstm_dropout_patch_applied": False,
        "rocm_lstm_modules_patched": 0,
        "rocm_lstm_dropout_original_values": [],
        "rocm_lstm_dropout_patch_warning": None,
    }
    if requested_acceleration != "amd-rocm":
        return info
    try:
        hip_version = getattr(getattr(torch_module, "version", None), "hip", None)
        if not hip_version:
            info["rocm_lstm_dropout_patch_warning"] = "AMD ROCm patch overgeslagen: torch.version.hip ontbreekt."
            return info

        nn_module = getattr(torch_module, "nn", None)
        lstm_cls = getattr(nn_module, "LSTM", None) if nn_module is not None else None
        module_cls = getattr(nn_module, "Module", None) if nn_module is not None else None
        if lstm_cls is None or module_cls is None:
            info["rocm_lstm_dropout_patch_warning"] = "AMD ROCm patch overgeslagen: torch.nn.LSTM niet beschikbaar."
            return info

        seen_objects: set[int] = set()
        seen_modules: set[int] = set()
        stack: List[tuple[str, Any]] = [("pipeline", pipeline)]
        patched: List[Dict[str, Any]] = []
        scanned = 0

        while stack and scanned < 10000:
            name, obj = stack.pop()
            oid = id(obj)
            if oid in seen_objects:
                continue
            seen_objects.add(oid)
            scanned += 1

            if isinstance(obj, module_cls):
                for module_name, module in obj.named_modules():
                    mid = id(module)
                    if mid in seen_modules:
                        continue
                    seen_modules.add(mid)
                    if isinstance(module, lstm_cls):
                        try:
                            old_dropout = float(getattr(module, "dropout", 0.0) or 0.0)
                        except Exception:
                            old_dropout = 0.0
                        if old_dropout > 0.0:
                            try:
                                setattr(module, "dropout", 0.0)
                                patched.append({"path": f"{name}.{module_name}".rstrip("."), "dropout": old_dropout})
                            except Exception as patch_exc:
                                patched.append({"path": f"{name}.{module_name}".rstrip("."), "dropout": old_dropout, "error": sanitize_error(patch_exc)})

            if isinstance(obj, dict):
                for key, value in obj.items():
                    if not isinstance(value, (str, bytes, int, float, bool, type(None))):
                        stack.append((f"{name}.{key}", value))
            elif isinstance(obj, (list, tuple, set)):
                for index, value in enumerate(obj):
                    if not isinstance(value, (str, bytes, int, float, bool, type(None))):
                        stack.append((f"{name}[{index}]", value))
            else:
                values = getattr(obj, "__dict__", None)
                if isinstance(values, dict):
                    for key, value in values.items():
                        if not isinstance(value, (str, bytes, int, float, bool, type(None))):
                            stack.append((f"{name}.{key}", value))

        info["rocm_lstm_modules_patched"] = len([item for item in patched if "error" not in item])
        info["rocm_lstm_dropout_original_values"] = patched
        info["rocm_lstm_dropout_patch_applied"] = info["rocm_lstm_modules_patched"] > 0
        if not patched:
            info["rocm_lstm_dropout_patch_warning"] = "Geen LSTM-modules met dropout > 0 gevonden in pyannote-pipeline."
        return info
    except Exception as exc:
        info["rocm_lstm_dropout_patch_warning"] = sanitize_error(exc)
        return info


def run_gpu_inference_smoketest(pipeline: Any, torch_module: Any, requested_acceleration: str) -> Dict[str, Any]:
    """Run a minimal pyannote inference pass to catch ROCm/MIOpen failures.

    The previous smoke test only loaded the model and moved it to GPU. The ROCm
    failure observed in v7.9.17/v7.12.1 occurs during the first inference call,
    so this test intentionally executes a short deterministic waveform.
    """
    payload: Dict[str, Any] = {
        "gpu_inference_smoketest_ok": None,
        "gpu_inference_smoketest_warning": None,
    }
    if requested_acceleration == "cpu":
        payload["gpu_inference_smoketest_ok"] = "not_required"
        return payload
    try:
        generator = torch_module.Generator(device="cpu")
        generator.manual_seed(7919)
        waveform = (torch_module.rand((1, 16000 * 6), generator=generator, dtype=torch_module.float32) - 0.5) * 0.01
        with torch_module.inference_mode():
            pipeline({"waveform": waveform.cpu(), "sample_rate": 16000})
        payload["gpu_inference_smoketest_ok"] = True
        return payload
    except Exception as exc:
        payload["gpu_inference_smoketest_ok"] = False
        payload["gpu_inference_smoketest_warning"] = sanitize_error(exc)
        raise RuntimeError(f"GPU inference-smoketest faalde: {sanitize_error(exc)}") from exc


def install_torchaudio_compatibility() -> None:
    try:
        apply_torchaudio_compatibility()
    except Exception as exc:
        eprint(f"WARN: torchaudio compatibility shim niet toegepast: {sanitize_error(exc)}")


def detect_torch_runtime(requested_acceleration: str) -> Dict[str, Any]:
    import torch

    cuda = getattr(torch, "cuda", None)
    hip_version = getattr(getattr(torch, "version", None), "hip", None)
    cuda_version = getattr(getattr(torch, "version", None), "cuda", None)
    cuda_available = bool(cuda is not None and cuda.is_available())
    device_name = None
    device_count = 0
    if cuda_available:
        try:
            device_count = int(cuda.device_count())
            device_name = str(cuda.get_device_name(0)) if device_count > 0 else None
        except Exception as exc:
            raise RuntimeError(f"torch.cuda device query faalde: {sanitize_error(exc)}") from exc

    backend = "rocm" if hip_version else ("cuda" if cuda_version else None)
    if requested_acceleration == "cpu":
        return {
            "torch_version": package_version("torch"),
            "torchaudio_version": package_version("torchaudio"),
            "pyannote_audio_version": package_version("pyannote.audio"),
            "cuda_available": cuda_available,
            "cuda_device_count": device_count,
            "device_name": device_name,
            "gpu_backend": None,
            "torch_cuda_version": cuda_version,
            "torch_hip_version": hip_version,
            "python_version": sys.version.split()[0],
            "device": "cpu",
        }
    if requested_acceleration == "amd-rocm" and backend != "rocm":
        raise RuntimeError(f"AMD ROCm gevraagd, maar deze PyTorch-runtime meldt backend={backend or 'none'}.")
    if requested_acceleration == "nvidia-cuda" and backend != "cuda":
        raise RuntimeError(f"NVIDIA CUDA gevraagd, maar deze PyTorch-runtime meldt backend={backend or 'none'}.")
    if not cuda_available:
        raise RuntimeError("torch.cuda.is_available() is false; GPU-diarization is niet beschikbaar in deze runtime.")

    return {
        "torch_version": package_version("torch"),
        "torchaudio_version": package_version("torchaudio"),
        "pyannote_audio_version": package_version("pyannote.audio"),
        "cuda_available": cuda_available,
        "cuda_device_count": device_count,
        "device_name": device_name,
        "gpu_backend": backend,
        "torch_cuda_version": cuda_version,
        "torch_hip_version": hip_version,
        "python_version": sys.version.split()[0],
    }


def load_waveform(wav_path: str):
    import numpy as np
    import torch

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


def annotation_to_segments(annotation_like) -> List[Dict[str, Any]]:
    if hasattr(annotation_like, "exclusive_speaker_diarization"):
        ann = annotation_like.exclusive_speaker_diarization
    elif hasattr(annotation_like, "speaker_diarization"):
        ann = annotation_like.speaker_diarization
    else:
        ann = annotation_like

    rows: List[Dict[str, Any]] = []
    if hasattr(ann, "itertracks"):
        for turn, _, speaker in ann.itertracks(yield_label=True):
            rows.append({"start": float(turn.start), "end": float(turn.end), "speaker": str(speaker)})
        return rows
    raise TypeError(f"Onbekend diarization-object: {type(annotation_like)!r} / inner={type(ann)!r}")


def load_pipeline(model_id: str, hf_token: Optional[str]):
    install_huggingface_hub_token_compatibility()
    install_torchaudio_compatibility()
    from pyannote.audio import Pipeline

    attempts = []
    if hf_token:
        attempts.append(("token", {"token": hf_token}))
        attempts.append(("use_auth_token", {"use_auth_token": hf_token}))
    attempts.append(("cache/no-token", {}))
    last_exc: Optional[Exception] = None
    for label, kwargs in attempts:
        try:
            pipeline = Pipeline.from_pretrained(model_id, **kwargs)
            eprint(f"INFO: pyannote GPU-sidecar pipeline geladen via {label}")
            return pipeline
        except TypeError as exc:
            if "unexpected" in str(exc).lower():
                last_exc = exc
                continue
            raise
    if last_exc:
        raise last_exc
    raise RuntimeError("Diarization pipeline kon niet worden geladen.")


def run_self_test(args) -> int:
    payload: Dict[str, Any] = {
        "success": False,
        "requested_acceleration": args.acceleration,
        "checks": {},
        "messages": [],
    }
    try:
        import torch
        configure_torch_checkpoint_compatibility(torch)
        runtime = detect_torch_runtime(args.acceleration)
        payload.update(runtime)
        payload["checks"]["torch_gpu_ok"] = True if args.acceleration != "cpu" else "not_required"
        payload["messages"].append("PyTorch GPU-runtime beschikbaar." if args.acceleration != "cpu" else "Pyannote CPU-runtime beschikbaar.")
        if args.model_smoke_test:
            hf_token = os.environ.get("HF_TOKEN") or os.environ.get("HUGGINGFACE_TOKEN")
            if not hf_token:
                raise RuntimeError("Model-smoketest vraagt HF_TOKEN via environment; token wordt niet als argument geaccepteerd.")
            pipeline = load_pipeline(args.model, hf_token)
            patch_info = apply_rocm_lstm_inference_patch(pipeline, torch, args.acceleration)
            payload.update(patch_info)
            if args.acceleration != "cpu":
                pipeline.to(torch.device("cuda"))
                payload["messages"].append("Pyannote-model geladen en naar GPU verplaatst.")
                inference_smoke = run_gpu_inference_smoketest(pipeline, torch, args.acceleration)
                payload.update(inference_smoke)
                payload["checks"]["gpu_inference_smoketest_ok"] = True
                payload["messages"].append("Pyannote GPU-inference smoketest geslaagd.")
            else:
                payload["messages"].append("Pyannote-model geladen op CPU-runtime.")
                payload["checks"]["gpu_inference_smoketest_ok"] = "not_required"
            payload["checks"]["pyannote_model_load_ok"] = True
            del pipeline
        else:
            payload["checks"]["pyannote_model_load_ok"] = "skipped"
        payload["success"] = True
        print("GPU_DIARIZATION_SELFTEST_JSON:" + json.dumps(payload, ensure_ascii=False))
        return 0
    except Exception as exc:
        payload["error"] = sanitize_error(exc)
        payload["traceback"] = sanitize_error(Exception(traceback.format_exc()))
        print("GPU_DIARIZATION_SELFTEST_JSON:" + json.dumps(payload, ensure_ascii=False))
        eprint(f"ERROR: GPU-diarization self-test faalde: {payload['error']}")
        return 1


def run_diarization(args) -> int:
    payload: Dict[str, Any] = {
        "status": "failed",
        "applied": False,
        "requested_acceleration": args.acceleration,
        "effective_acceleration": None,
        "model": args.model,
        "requested_model": args.model,
        "effective_model": args.model,
        "diarization_segments": [],
        "warning": None,
    }
    try:
        hf_token = os.environ.get("HF_TOKEN") or os.environ.get("HUGGINGFACE_TOKEN")
        if not hf_token:
            raise RuntimeError("GPU-diarization vraagt HF_TOKEN via environment; token wordt niet als argument geaccepteerd.")
        import torch
        configure_torch_checkpoint_compatibility(torch)
        runtime = detect_torch_runtime(args.acceleration)
        payload.update(runtime)
        if args.acceleration == "cpu":
            payload["effective_acceleration"] = "cpu"
            payload["device"] = "cpu"
        else:
            payload["effective_acceleration"] = "amd-rocm" if runtime.get("gpu_backend") == "rocm" else "nvidia-cuda"
            payload["device"] = "cuda"
        pipeline = load_pipeline(args.model, hf_token)
        patch_info = apply_rocm_lstm_inference_patch(pipeline, torch, args.acceleration)
        payload.update(patch_info)
        if args.acceleration != "cpu":
            pipeline.to(torch.device("cuda"))
        waveform = load_waveform(args.input_wav)
        kwargs: Dict[str, Any] = {}
        if args.min_speakers and args.min_speakers > 0:
            kwargs["min_speakers"] = int(args.min_speakers)
        if args.max_speakers and args.max_speakers > 0:
            kwargs["max_speakers"] = int(args.max_speakers)
        diarization_output = pipeline(waveform, **kwargs)
        segments = annotation_to_segments(diarization_output)
        payload["diarization_segments"] = segments
        payload["speaker_count"] = len({str(item.get("speaker")) for item in segments if item.get("speaker")})
        payload["status"] = "completed" if segments else "empty"
        payload["applied"] = bool(segments)
        write_json(args.output_json, payload)
        try:
            del pipeline
            del diarization_output
            del waveform
            torch.cuda.empty_cache() if args.acceleration != "cpu" else None
        except Exception:
            pass
        gc.collect()
        return 0 if segments else 2
    except Exception as exc:
        payload["warning"] = sanitize_error(exc)
        payload["traceback"] = sanitize_error(Exception(traceback.format_exc()))
        write_json(args.output_json, payload)
        eprint(f"ERROR: GPU-diarization faalde: {payload['warning']}")
        return 1


def main() -> int:
    parser = argparse.ArgumentParser(description="Captivox pyannote GPU diarization sidecar")
    parser.add_argument("--input-wav", default="", help="16kHz mono WAV input for diarization")
    parser.add_argument("--output-json", default="", help="Output JSON for diarization segments")
    parser.add_argument("--model", default="pyannote/speaker-diarization-3.1")
    parser.add_argument("--acceleration", choices=["amd-rocm", "nvidia-cuda", "cpu"], required=True)
    parser.add_argument("--min-speakers", type=int, default=0)
    parser.add_argument("--max-speakers", type=int, default=0)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--model-smoke-test", action="store_true")
    args = parser.parse_args()

    if args.self_test:
        return run_self_test(args)
    if not args.input_wav or not args.output_json:
        eprint("ERROR: --input-wav en --output-json zijn verplicht buiten self-test.")
        return 2
    return run_diarization(args)


if __name__ == "__main__":
    sys.exit(main())
