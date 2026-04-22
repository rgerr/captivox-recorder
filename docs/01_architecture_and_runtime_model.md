# Captivox Recorder Architecture and Runtime Model (0.9.0-beta1)

## Overview

Captivox Recorder is a local‑first Windows application for capturing meeting audio and producing high‑quality transcripts.  The application runs entirely on the user's machine and does not send audio or transcripts to cloud services by default.  All audio, intermediate files and transcripts live in a single **session folder** that is easy to inspect, copy or back up.

The product design separates the C# recording front‑end from the Python‑based transcription back‑end.  The front‑end captures microphone and system audio, writes files to disk and collects metadata about the meeting.  The back‑end (the **worker**) watches for new sessions, runs voice activity detection and segmentation, transcribes the audio with [WhisperX](https://github.com/m-bain/whisperx), and writes transcript artifacts back into the same session folder.  This separation provides transparency, easier debugging and simpler upgrades of either component.

## Components

### Recorder

The recorder is a .NET 8 Windows desktop application.  When you start a recording it opens the microphone and the system audio channels and writes them to a `source.wav` file in a new session folder under your Documents.  The recorder also writes a `session.json` file containing the start time, duration, audio device details and other metadata.  The UI shows recording levels, elapsed time and device selection, and allows you to start/stop recording with a single button.  After stopping a recording you can mark the session as "Ready" for transcription with a second button; this writes a `READY` marker file into the session folder.

### Session folder

Each recording lives in its own subfolder, for example:

    Documents\CaptivoxSessions\
      2024-04-21T12-00-00Z\
        source.wav          # raw microphone + system audio
        session.json        # metadata about the session
        READY               # marker file created when user is ready to transcribe
        worker.log          # log of the transcription process
        transcript.json     # JSON representation of transcript segments
        transcript.vtt      # WebVTT file for video players
        actions.md          # structured actions and decisions extracted from the meeting (beta)
        insights.md         # summarised insights (beta)

You own these files; you can copy them, rename them or archive them.

### Worker

The worker is a Python 3.10 process that runs separately from the recorder UI.  It monitors the session folders for new `READY` markers.  When a new ready session is detected, the worker:

1. Reads `source.wav`.
2. Performs voice activity detection (VAD) using [pyannote‑audio](https://github.com/pyannote/pyannote-audio) to split the audio into speech segments.
3. Transcribes each segment with [WhisperX](https://github.com/m-bain/whisperx), optionally using a GPU or CPU.
4. Aligns words and produces a high‑quality timestamped transcript.
5. Writes `transcript.json`, `transcript.vtt` and logs to the session folder.
6. Invokes optional post‑processing to generate **structured insights** (key takeaways, actions, decisions) using a local language model or configured cloud provider.

The worker runs in a dedicated Python virtual environment packaged with the application.  The local AI settings let you choose which Whisper model to use and whether to run the additional structured‑insights pass locally or via a remote API.

### Library and review UI

Once a transcript is available, the session appears in the library view.  The library shows each session with its date, duration and status.  Selecting a session opens the **single‑session review** workspace where you can browse the transcript, play back audio, navigate by speaker or chapter, see generated actions/insights and add your own notes.  The transcript remains synced to the audio; clicking on text jumps playback.

### Microsoft 365 integration

Captivox can optionally enrich sessions with meeting context from Microsoft 365.  When you link a Microsoft account, the recorder can fetch the meeting subject, attendees and location from your calendar.  During review, the transcript can be cross‑referenced with Outlook or Teams to highlight who was speaking.  Linking is done via an interactive browser login and uses the Microsoft Graph API.  You can link multiple accounts and choose which calendars or mailboxes to query.

### Provider and model selection

Advanced users can choose between **local** and **cloud** providers for the structured‑insights pass.  The local provider uses your own hardware and a local model (for example `qwen2.5:7b-instruct`).  Cloud providers may offer richer language models but may require an API key.  You can also choose which Whisper model to use for transcription and whether to download or update models.  A hardware‑estimation tool suggests an appropriate model based on your CPU or GPU.

## Flow summary

1. Start a recording in the Recorder UI.
2. Speak through the meeting; the application captures both microphone and system audio.
3. Stop the recording.
4. Click **Ready**.  A `READY` file appears in the session folder.
5. The worker detects the ready session and begins transcription.
6. Review the transcript and structured insights once the worker finishes.
7. (Optional) Export diagnostics or copy the session folder for support.

## Data ownership and privacy

Captivox is designed to keep your recordings private.  The application runs offline by default; nothing is sent to the cloud unless you explicitly enable a remote provider.  Audio, transcripts and all derived artifacts are stored locally in session folders.  You may delete, archive or encrypt these folders at your discretion.
