# Privacy

Captivox Recorder runs entirely on your own PC. There is no account, no Captivox server and no telemetry.

## What stays on your PC

| Data | Where | Who can access it |
| --- | --- | --- |
| Recordings (`audio_mix.wav`) and transcripts | `%USERPROFILE%\Captivox\Opnames` (configurable) | you and the PC's administrators |
| Settings and logs | `%LOCALAPPDATA%\CaptivoxRecorderFree` | you and the PC's administrators |

Logs contain technical information (device names, times, error messages, folders), not the content of your conversations. The notifications shown in the app (which include a conversation's name) are kept for 7 days in the same folder.

If you choose a storage folder that syncs to the cloud (OneDrive, Dropbox, a network drive), your recordings will leave your PC through that service. The default folder does not sync.

## When Captivox uses the internet

- **During installation**: the installer (or `install-prereqs.ps1` in the portable version) downloads:
  - Python 3.10 from the python-build-standalone project, on GitHub;
  - FFmpeg 8.1 (the gyan.dev build), on GitHub;
  - the Python packages (WhisperX and its dependencies) from PyPI, and with an NVIDIA graphics card PyTorch's CUDA build from download.pytorch.org;
  - the speech models: Whisper from Hugging Face, with an AMD graphics card also the whisper.cpp model for the fast option, from Hugging Face, and the model for the timestamps in your language (Dutch from Hugging Face; English, German, French, Spanish and Italian from download.pytorch.org).
- **The first time you switch to another language**: the model for the timestamps in that language, from the same places.
- **While transcribing**: when the PC is online, the model libraries may ask Hugging Face whether a model has a newer version. Only technical information goes along, such as the model name and library versions.
- **Optional, advanced**: `tools\Build-WhisperCppVulkanRuntime.ps1` downloads whisper.cpp from GitHub. The normal installation does not use it.

No recordings or transcripts are ever sent along with any of this. Once installed, Captivox also works without an internet connection.

## Deleting data

- One recording: on *Overleggen* (Meetings), open the conversation's menu and choose **Naar de Prullenbak** (Move to Recycle Bin), or delete its folder.
- Everything: delete the program folder, `%LOCALAPPDATA%\CaptivoxRecorderFree` and your storage folder. The transcription runtime is in `%LOCALAPPDATA%\CaptivoxRecorderFree\runtime`; downloaded models are in `%USERPROFILE%\.cache\huggingface` and `%USERPROFILE%\.cache\torch`.

## Recording conversations

A recording with voices and names contains personal data of everyone in the conversation. Let the others know that you are recording and why, don't keep recordings longer than you need to, and share them only with people who really need them. Captivox does not delete anything by itself, unless you turn that on under *Instellingen* (Settings), *Opslag en bewaren* (Storage and retention).
