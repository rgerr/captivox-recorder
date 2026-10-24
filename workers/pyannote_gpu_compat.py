"""Compatibility shims for pyannote.audio on TorchAudio 2.9+ GPU sidecars.

AMD ROCm 7.2.1 on Windows currently pairs PyTorch/TorchAudio 2.9.x with the
ROCm wheels. pyannote.audio 3.3.x still imports a few TorchAudio symbols that
were deprecated in 2.8 and removed in 2.9. Captivox feeds pyannote a prepared
16 kHz mono waveform, so these shims are limited to import/runtime compatibility
and do not change the audio signal or diarization model.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Any


def apply_torchaudio_compatibility() -> None:
    """Patch removed TorchAudio API surface before importing pyannote.audio.

    Keep this intentionally small: only legacy attributes that pyannote/speechbrain
    may touch on import or when inspecting audio metadata are provided.
    """
    try:
        import torchaudio  # type: ignore
    except Exception:
        return

    if not hasattr(torchaudio, "list_audio_backends"):
        torchaudio.list_audio_backends = lambda: ['soundfile']  # type: ignore[attr-defined]
    if not hasattr(torchaudio, "get_audio_backend"):
        torchaudio.get_audio_backend = lambda: 'soundfile'  # type: ignore[attr-defined]
    if not hasattr(torchaudio, "set_audio_backend"):
        torchaudio.set_audio_backend = lambda *args, **kwargs: None  # type: ignore[attr-defined]

    if not hasattr(torchaudio, "AudioMetaData"):
        audio_metadata: Any | None = None
        try:
            from torchaudio._backend.common import AudioMetaData as _AudioMetaData  # type: ignore
            audio_metadata = _AudioMetaData
        except Exception:
            @dataclass(frozen=True)
            class _AudioMetaData:  # type: ignore[no-redef]
                sample_rate: int
                num_frames: int
                num_channels: int
                bits_per_sample: int | None = None
                encoding: str | None = None
            audio_metadata = _AudioMetaData
        torchaudio.AudioMetaData = audio_metadata  # type: ignore[attr-defined]

    if not hasattr(torchaudio, "info"):
        def _info(path: str, *args, **kwargs):
            try:
                import soundfile as sf  # type: ignore
                with sf.SoundFile(path) as handle:
                    return torchaudio.AudioMetaData(  # type: ignore[attr-defined]
                        sample_rate=int(handle.samplerate),
                        num_frames=int(len(handle)),
                        num_channels=int(handle.channels),
                        bits_per_sample=None,
                        encoding=str(getattr(handle, "format", "UNKNOWN")),
                    )
            except Exception as exc:
                raise RuntimeError(f"torchaudio.info compatibility shim failed: {exc}") from exc
        torchaudio.info = _info  # type: ignore[attr-defined]
