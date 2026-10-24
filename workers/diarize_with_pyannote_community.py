import argparse
import importlib.metadata
import json
import os
import re
import sys
from pathlib import Path

os.environ.setdefault("PYANNOTE_METRICS_ENABLED", "0")


def sanitize_error(exc: Exception) -> str:
    raw = str(exc) if exc else ""
    raw = re.sub(r"hf_[A-Za-z0-9]{20,}", "[REDACTED_HF_TOKEN]", raw)
    raw = re.sub(r"[A-Za-z]:\\[^\r\n]+", "[REDACTED_LOCAL_PATH]", raw)
    raw = re.sub(r"https://huggingface\.co/[^\s]+", "https://huggingface.co/[redacted]", raw)
    return raw or "Onbekende pyannote community-1 sidecar-fout."


def annotation_to_segments(annotation_like):
    if hasattr(annotation_like, "exclusive_speaker_diarization"):
        annotation = annotation_like.exclusive_speaker_diarization
        source = "exclusive_speaker_diarization"
    elif hasattr(annotation_like, "speaker_diarization"):
        annotation = annotation_like.speaker_diarization
        source = "speaker_diarization"
    else:
        annotation = annotation_like
        source = "annotation"

    rows = []
    if hasattr(annotation, "itertracks"):
        for turn, _, speaker in annotation.itertracks(yield_label=True):
            rows.append({"start": float(turn.start), "end": float(turn.end), "speaker": str(speaker)})
    return rows, source


def main() -> int:
    parser = argparse.ArgumentParser(description="Captivox pyannote community-1 CPU sidecar")
    parser.add_argument("--input-wav", required=False)
    parser.add_argument("--output-json", required=False)
    parser.add_argument("--model", default="pyannote/speaker-diarization-community-1")
    parser.add_argument("--self-test", action="store_true", help="Valideer imports en pyannote.audio major versie; laad geen Hugging Face-model.")
    parser.add_argument("--min-speakers", type=int, default=0)
    parser.add_argument("--max-speakers", type=int, default=0)
    args = parser.parse_args()

    token = os.environ.get("HF_TOKEN") or os.environ.get("HUGGINGFACE_TOKEN")
    payload = {
        "status": "failed",
        "model": args.model,
        "device": "cpu",
        "diarization_segments": [],
        "pyannote_audio_version": None,
        "warning": None,
    }

    try:
        payload["pyannote_audio_version"] = importlib.metadata.version("pyannote.audio")
        major = int(str(payload["pyannote_audio_version"]).split(".")[0])
        if major < 4:
            raise RuntimeError(f"pyannote.audio major versie {payload['pyannote_audio_version']} is te oud voor community-1; verwacht 4.x.")

        if args.self_test:
            # Bewust lichtgewicht: geen Pipeline-import en geen model-load tijdens bootstrap.
            # De echte community-1 model-load hoort bij een sessie of expliciete runtime-test,
            # niet bij install-prereqs.
            payload.update({"status": "completed", "warning": None, "segment_count": 0, "speaker_count": 0})
            if args.output_json:
                Path(args.output_json).write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
            print(f"pyannote community-1 sidecar metadata self-test OK: pyannote.audio={payload['pyannote_audio_version']}", flush=True)
            return 0

        from pyannote.audio import Pipeline
        import torch

        if not args.output_json:
            raise RuntimeError("--output-json ontbreekt voor diarization-run.")
        if not token:
            raise RuntimeError("HF_TOKEN ontbreekt voor pyannote community-1 sidecar.")
        if not args.input_wav or not Path(args.input_wav).exists():
            raise RuntimeError(f"Input-WAV ontbreekt: {args.input_wav}")

        pipeline = Pipeline.from_pretrained(args.model, token=token)
        pipeline.to(torch.device("cpu"))

        diarize_kwargs = {}
        if args.min_speakers > 0:
            diarize_kwargs["min_speakers"] = args.min_speakers
        if args.max_speakers > 0:
            diarize_kwargs["max_speakers"] = args.max_speakers

        output = pipeline(args.input_wav, **diarize_kwargs)
        segments, source = annotation_to_segments(output)
        payload.update({
            "status": "completed",
            "source": source,
            "diarization_segments": segments,
            "speaker_count": len({row["speaker"] for row in segments}),
            "segment_count": len(segments),
            "warning": None,
        })
        Path(args.output_json).write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
        return 0
    except Exception as exc:
        payload["warning"] = sanitize_error(exc)
        if args.output_json:
            try:
                Path(args.output_json).write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
            except Exception:
                pass
        print(payload["warning"], file=sys.stderr, flush=True)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
