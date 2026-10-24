# Third-party components

Captivox Recorder builds on the work of others. This file lists what the free edition ships, and what its installer downloads and sets up on your PC, with the licence of each. The full licence texts are available from those projects.

## In CaptivoxRecorder.exe and the installer

| Component | Version | Licence | Source |
| --- | --- | --- | --- |
| .NET runtime and Windows Forms (self-contained) | 8.0 | MIT | https://github.com/dotnet/runtime, https://github.com/dotnet/winforms |
| NAudio | 2.2.1 | MIT | https://github.com/naudio/NAudio |
| Microsoft.Toolkit.Uwp.Notifications (Windows Community Toolkit) | 7.1.3 | MIT | https://github.com/CommunityToolkit/WindowsCommunityToolkit |
| System.Drawing.Common | 8.0.29 | MIT | https://github.com/dotnet/winforms |
| System.Management | 8.0.0 | MIT | https://github.com/dotnet/runtime |
| Inno Setup (the installer) | 6 | Inno Setup License | https://jrsoftware.org/files/is/license.txt |

## Downloaded and set up during installation (not bundled)

The installer runs `install-prereqs.ps1`, which downloads these into the app's own data folder. Each download has a fixed version; Python, FFmpeg and the whisper.cpp model are checked against a SHA-256 checksum.

| Component | Version | Licence | Source |
| --- | --- | --- | --- |
| Python, from python-build-standalone (`install_only_stripped`) | 3.10.22 (build 20261003) | PSF-2.0 for CPython; the build includes third-party libraries such as OpenSSL (Apache-2.0), SQLite (public domain), zlib, bzip2, libffi, XZ and Tcl/Tk, each under its own licence | https://github.com/astral-sh/python-build-standalone, licence notes: https://gregoryszorc.com/docs/python-build-standalone/main/running.html#licensing |
| FFmpeg "full" build by gyan.dev | 8.1 | GPL-3.0 (the build includes GPL-licensed libraries); its licence is placed next to `ffmpeg.exe` as `LICENSE.txt` | build: https://www.gyan.dev/ffmpeg/builds/ and https://github.com/GyanD/codexffmpeg/releases/tag/8.1, source code: https://ffmpeg.org/download.html |
| whisper.cpp (inside that FFmpeg build, as the `whisper` audio filter used by the fast AMD route) | as built into FFmpeg 8.1 | MIT | https://github.com/ggml-org/whisper.cpp |

### Python packages

From PyPI. WhisperX, pyannote.audio, PyTorch, torchaudio, NumPy, pandas and ffmpeg-python are pinned in `workers\requirements.windows.lock.txt`; the other packages are dependencies whose version pip resolves during setup (the versions below are those of a test installation). With an NVIDIA card, torch and torchaudio come from download.pytorch.org instead (the CUDA 12.8 build). The exact list of everything installed is written to `workers\requirements.windows.realized.lock.txt` in the program folder after setup.

| Component | Version | Licence | Source |
| --- | --- | --- | --- |
| WhisperX | 3.7.4 | BSD-2-Clause | https://github.com/m-bain/whisperX |
| faster-whisper | 1.2.1 | MIT | https://github.com/SYSTRAN/faster-whisper |
| CTranslate2 | 4.8.3 | MIT | https://github.com/OpenNMT/CTranslate2 |
| PyTorch and torchaudio | 2.8.0 | BSD-3-Clause | https://github.com/pytorch/pytorch, https://github.com/pytorch/audio |
| PyTorch CUDA build (NVIDIA cards only), which includes NVIDIA CUDA libraries such as cuBLAS and cuDNN | 2.8.0+cu128 | BSD-3-Clause for PyTorch; the NVIDIA libraries under NVIDIA's licence terms | https://download.pytorch.org/whl/cu128, https://docs.nvidia.com/cuda/eula/ |
| NumPy | 2.0.2 | BSD-3-Clause | https://numpy.org |
| pandas | 2.2.3 | BSD-3-Clause | https://pandas.pydata.org |
| pyannote.audio (a WhisperX dependency; in the free edition only used to detect where speech is, not to recognise speakers) | 3.3.2 | MIT | https://github.com/pyannote/pyannote-audio |
| PyAV (a faster-whisper dependency), which bundles FFmpeg 7.1 libraries built with GPL components such as x264 | 15.1.0 | BSD-3-Clause for PyAV; the bundled FFmpeg libraries as a whole GPL-2.0-or-later | https://github.com/PyAV-Org/PyAV |
| Hugging Face transformers, huggingface_hub, tokenizers, safetensors | 5.20, 2.3, 0.23, 0.8 | Apache-2.0 | https://github.com/huggingface |
| SpeechBrain, Lightning, torchmetrics | 1.1.2, 2.6.6, 1.9.0 | Apache-2.0 | https://github.com/speechbrain/speechbrain, https://github.com/Lightning-AI |
| ONNX Runtime | 1.23.2 | MIT | https://github.com/microsoft/onnxruntime |
| SciPy, scikit-learn | 1.15.3, 1.7.2 | BSD-3-Clause | https://scipy.org, https://scikit-learn.org |
| NLTK, sentencepiece, requests | 3.10, 0.2, 2.34 | Apache-2.0 | https://www.nltk.org, https://github.com/google/sentencepiece, https://github.com/psf/requests |
| ffmpeg-python | 0.2.0 | Apache-2.0 | https://github.com/kkroening/ffmpeg-python |
| certifi, tqdm | 2026.10.23, 4.70 | MPL-2.0 (tqdm: MPL-2.0 and MIT) | https://github.com/certifi/python-certifi, https://github.com/tqdm/tqdm |
| matplotlib | 3.10.9 | Matplotlib License (PSF-based) | https://matplotlib.org |
| All other dependencies | see the realized lock file | MIT, BSD, Apache-2.0, PSF or ISC | https://pypi.org |

## Speech models (downloaded during setup or on first use)

| Model | Used for | Licence | Source |
| --- | --- | --- | --- |
| Whisper (OpenAI) large-v2, converted for faster-whisper | transcription on an NVIDIA card | MIT | https://huggingface.co/Systran/faster-whisper-large-v2 |
| Whisper (OpenAI) large-v3-turbo, converted for faster-whisper | transcription on the processor | MIT | https://huggingface.co/mobiuslabsgmbh/faster-whisper-large-v3-turbo |
| Whisper (OpenAI) large-v3-turbo, whisper.cpp format (`ggml-large-v3-turbo-q5_0.bin`) | the fast option on an AMD card (FFmpeg with Vulkan) | MIT | https://huggingface.co/ggerganov/whisper.cpp |
| Speech detection (segmentation) model bundled with WhisperX, from pyannote | finding the parts with speech | MIT | https://github.com/m-bain/whisperX |
| wav2vec2 alignment model, Dutch (`jonatasgrosman/wav2vec2-large-xlsr-53-dutch`) | timestamps for Dutch | Apache-2.0 | https://huggingface.co/jonatasgrosman/wav2vec2-large-xlsr-53-dutch |
| wav2vec 2.0 alignment model, English (torchaudio `WAV2VEC2_ASR_BASE_960H`) | timestamps for English | MIT | https://download.pytorch.org (torchaudio pipelines) |
| VoxPopuli alignment models, French, German, Spanish and Italian (torchaudio `VOXPOPULI_ASR_BASE_10K_*`) | timestamps for those languages | CC BY-NC 4.0 (non-commercial) | https://download.pytorch.org (torchaudio pipelines), https://github.com/facebookresearch/voxpopuli |

The names and logos of these projects belong to their owners. Captivox Recorder is not affiliated with them.
