# Onderdelen van anderen

Captivox Recorder bouwt voort op het werk van anderen. Hieronder staat wat het gebruikt en onder welke licentie. De volledige licentieteksten staan bij die projecten.

## In CaptivoxRecorder.exe

| Onderdeel | Licentie | Bron |
| --- | --- | --- |
| .NET 8 runtime en Windows Forms | MIT | https://github.com/dotnet/runtime, https://github.com/dotnet/winforms |
| NAudio 2.2.1 | MIT | https://github.com/naudio/NAudio |
| System.Management 8.0.0 | MIT | https://github.com/dotnet/runtime |

## Geinstalleerd door install-prereqs.ps1 (niet meegeleverd)

| Onderdeel | Licentie | Bron |
| --- | --- | --- |
| Python 3.10 | PSF License | https://www.python.org |
| WhisperX 3.7.4 | BSD-2-Clause | https://github.com/m-bain/whisperX |
| faster-whisper en CTranslate2 | MIT | https://github.com/SYSTRAN/faster-whisper, https://github.com/OpenNMT/CTranslate2 |
| pyannote.audio 3.3.2 | MIT | https://github.com/pyannote/pyannote-audio |
| PyTorch en torchaudio 2.8.0 | BSD-3-Clause | https://github.com/pytorch/pytorch |
| NumPy, pandas | BSD-3-Clause | https://numpy.org, https://pandas.pydata.org |
| ffmpeg-python | Apache-2.0 | https://github.com/kkroening/ffmpeg-python |
| FFmpeg (via winget, Gyan.FFmpeg) | LGPL-2.1 of GPL, afhankelijk van de build | https://ffmpeg.org |
| whisper.cpp (alleen AMD-route, wordt gebouwd) | MIT | https://github.com/ggml-org/whisper.cpp |

## Modellen (eenmalig gedownload bij gebruik)

| Model | Licentie | Bron |
| --- | --- | --- |
| Whisper (OpenAI), via faster-whisper | MIT | https://huggingface.co/Systran |
| wav2vec2-uitlijnmodellen per taal | Apache-2.0 of MIT, per model | https://huggingface.co |
| pyannote/speaker-diarization-3.1, pyannote/segmentation-3.0 | MIT, voorwaarden accepteren op Hugging Face | https://huggingface.co/pyannote |

De namen en logo's van deze projecten zijn van hun eigenaren. Captivox Recorder is niet aan hen verbonden.
