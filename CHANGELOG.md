# Changelog

## 2.0.1 (2026-10-24)

- **Interface in four languages**: English (the default for new installations), Dutch, German and French. The installer is in the same four languages and passes the language you choose to the app on a new installation; an existing installation stays in Dutch. Change the language under *Settings*, *General* (it takes effect after a restart; a button restarts Captivox for you). Dates and times follow the language of the interface. Transcription is available in Dutch, English, German, French, Spanish, Italian, Chinese and Japanese, chosen separately from the interface language.
- **New installer**: one setup that downloads and sets up everything by itself, without administrator rights: a private Python, FFmpeg, WhisperX and the speech models, with a progress bar (about 5 GB, 10 to 30 minutes). It checks the free disk space first. No PowerShell window, no `winget` and no repair shortcut anymore: to repair, run the setup again.
- **Graphics cards are set up automatically**: NVIDIA via CUDA. With an AMD card Captivox now transcribes on the processor by default, which is more accurate; the fast route through the graphics card (FFmpeg's whisper filter with Vulkan, prepared and tested during setup) is a choice under *Settings*, *Recording*. The manual AMD step is gone.
- Transcripts no longer contain lines without speech, such as `***`, `...` or `[BLANK_AUDIO]`.
- Without a saved choice, Captivox now picks the Windows default devices for the microphone and for the sound of the others (it used to pick the first playback device in the list).
- Starting a recording no longer shows a technical check: only a short question when something needs your attention, such as a playback device that is not the Windows default. The details go to the log.
- *Today* shows the first steps as long as there are no recordings yet.
- The installer shows the Captivox logo, the real disk space needed and what happens after copying the files.
- **The processor stays usable**: without a usable graphics card, transcription runs at low priority with a limited number of threads (two logical cores stay free) and with the faster `large-v3-turbo` model (int8). This also applies when the graphics card route falls back to the processor. The processor model can be changed in the extended settings (*WhisperX model (processor)*).
- **Captivox Pro page**: a page in the app about the coming paid edition.
- Worker fixes: the private FFmpeg is put first on the PATH of the transcription process (WhisperX starts `ffmpeg` itself); the AMD route no longer fails because the use of the graphics card was never confirmed; the worker self-test works again.

## 2.0.0 (2026-10-24)

A redesign of the app, with a clearer layout.

- A fixed set of pages on the left: *Vandaag* (Today), *Overleggen* (Meetings), *Meldingen* (Notifications) and *Instellingen* (Settings), with the record button always at the top.
- *Vandaag*: a big record button, what needs your attention (a failed or interrupted recording) and the latest notifications.
- *Overleggen*: all your conversations with search and a date range, the transcript right next to them, and for each conversation: save as text or subtitles, rename, transcribe again or delete. Importing audio lives here too.
- *Meldingen*: the app's latest notifications, also after a restart.
- *Instellingen*: recording, notifications, storage and retention, and advanced settings (with the older, extended screens).
- Show or hide the window with Ctrl+Shift+F10.
- With an AMD graphics card but no prepared AMD route, Captivox now simply transcribes on the processor (CPU) instead of stopping.
- The data folder (`%LOCALAPPDATA%\CaptivoxRecorderFree`), the storage folder and the installer stay the same; 2.0.0 installs over 1.x.

## 1.1.1 (2026-10-24)

- `install-prereqs.ps1` no longer sets any speaker recognition settings and no longer reports anything about them.

## 1.1.0 (2026-10-24)

- Speaker recognition (pyannote) has been removed from the free version. Transcripts now show only the timestamp for each segment.
- A Hugging Face token saved by version 1.0.0 is removed at startup; it is no longer needed.
- The `-PreparePyannoteCommunityRuntime` option of `install-prereqs.ps1` has been removed.

## 1.0.0 (2026-10-22)

First free version under the GPL-3.0, with source code.

- Records microphone and PC audio at the same time (WASAPI), with level meters and a speech-friendly mix to 16 kHz mono.
- Transcribes on your own PC with WhisperX, optionally with speaker recognition (pyannote 3.1); on NVIDIA (CUDA), AMD (Vulkan) or the processor (CPU).
- Import existing audio and video files and have them transcribed.
- Recording list with transcript: copy, save as .txt, .srt or .vtt, rename, transcribe again, move to the Recycle Bin.
- Monitoring during recording: silent microphone, audio device disappearing, recording nearly full.
- System tray icon and the Ctrl+Shift+F9 hotkey.
- Its own data folder (`%LOCALAPPDATA%\CaptivoxRecorderFree`) and a storage folder in your user profile.
- Installer (`setup.exe`, per user, no administrator rights needed) that can install the transcription runtime at the end, with a shortcut to repair it later; plus a zip that needs no installation.
- `install-prereqs.ps1` installs the transcription runtime without administrator rights; the recorder picks up the paths the script sets right away, even if it is already open.

Replaces technical preview 0.9.0-beta1.
