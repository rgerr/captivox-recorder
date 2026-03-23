# Captivox Recorder

![Captivox Banner](captivox-banner.png)

**Technical Preview – Windows only** • **Not a final release** • **No installer available yet**

Captivox Recorder is a local Windows meeting recorder with a session-folder workflow and integrated WhisperX transcription. It captures both microphone and system audio, writes a structured session folder, and hands off to a worker process for offline AI-powered transcription. This approach provides predictable, reproducible meeting capture for downstream processing while keeping your audio and transcripts local.

## Why Captivox Recorder?

Most recording tools are generic audio recorders or rely on cloud-based services. Captivox Recorder was built to solve a specific problem: reliable meeting capture for an offline, session-based transcription pipeline. Key principles guiding its design:

- **Local-first**: your recordings and transcripts stay on your machine.
- **Microphone + system audio**: capture both sides of the conversation.
- **Session folder output**: every recording is organized into a dedicated folder with predictable artifacts.
- **Structured handoff**: a simple `READY` marker file signals to a worker to start transcription.
- **Stability over feature bloat**: focus on core recording and transcription flow rather than extraneous UI.

## Current status

This repository is a **technical preview**. It contains the source code and assets for Captivox Recorder, along with installation scripts and a build process. It does **not** provide a signed Windows installer or licensing backend yet. Use it for development, testing and collaboration; it is not a production release.

## What works today

- Windows-only recording (Windows 10/11).
- Microphone and system audio capture via loopback.
- Mixed audio output to the session folder.
- Session folder output with a `READY` file to trigger transcription.
- Integration with WhisperX for transcription (via worker).
- Basic UI: Recorder panel, Status/Worker monitor, Session library.
- Settings for WhisperX/FFmpeg/Python paths.
- `install-prereqs.ps1` script to set up dependencies.
- `build-win-x64.ps1` script to build the .NET application.
- Optional integration with Ollama for local post-processing (preview).
- Hooks for structured exports, tags, filtering and library chat (in progress).

## Architecture

```
Recorder -> SessionFolder -> READY -> Worker -> WhisperXHelper -> Transcript
```

1. **Recorder** captures microphone and system audio.
2. A **session folder** is created for each recording; audio is written to this folder.
3. When recording is complete, a file named `READY` signals that the folder is ready for transcription.
4. A **worker** process monitors for new sessions, claims them by creating a `CLAIMED` file, and runs WhisperX to generate a transcript.
5. The **WhisperX helper** handles diarization and writes the transcript back into the session folder.
6. The session folder now contains audio files and a `transcript.json`.

## Requirements

- Windows 10/11.
- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download) (for building).
- [Python 3.10](https://www.python.org/downloads/).
- [FFmpeg](https://ffmpeg.org/download.html).
- [WhisperX](https://github.com/m-bain/whisperX) and a Hugging Face token for diarization.
- Optional: [Ollama](https://ollama.ai/) for local LLM post-processing.

All dependencies can be installed via the provided PowerShell script:

```powershell
# From the repository root
.\install-prereqs.ps1
```

## Quick start

1. Clone this repository and switch to the project directory:

   ```powershell
   git clone https://github.com/rgerr/captivox-recorder.git
   cd captivox-recorder
   ```

2. Install prerequisites:

   ```powershell
   .\install-prereqs.ps1
   ```

3. Build and run the recorder:

   ```powershell
   .\build-win-x64.ps1
   # The built application is in out/Release
   .\out\Release\Captivox.Recorder.exe
   ```

4. Start the worker (in a separate terminal):

   ```powershell
   python .\worker\run_worker.py
   ```

5. Record a meeting. When you stop recording, a `READY` file will appear in the session folder; the worker will pick it up and produce a transcript.

## Build from source

To build from source manually:

```powershell
dotnet publish src/Captivox.Recorder -c Release -r win-x64 -o out/Release /p:PublishSingleFile=true /p:SelfContained=true
```

This produces a self-contained `Captivox.Recorder.exe` in `out/Release`. A signed installer is planned for a future release.

## Roadmap

1. **Stabilization and bug fixing** – finalize audio capture and transcription workflow.
2. **UX polish** – improve UI, session library management, tagging and filtering.
3. **Installer and signing** – produce a Windows installer with code signing and automatic updates.
4. **Feature expansion** – advanced export formats, AI-assisted summaries, library chat, cross-platform support.

## Limitations

- Windows-only; no support for macOS or Linux at this time.
- No signed installer – you must build from source.
- Licensing and activation backend is not implemented.
- Diagnostics and error handling are still being refined.
- Use at your own risk; this is pre-release software.

## License and usage

This repository is provided for review, collaboration, and evaluation. **All rights reserved.** It is not open-source software. Redistribution, modification, or commercial use requires permission from the authors. See the `LICENSE` file for more details.

---

*Captivox Recorder is built for predictable, local meeting capture and transcription. Feedback and contributions are welcome as we prepare for a full release.*
