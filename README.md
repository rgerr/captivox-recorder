# Captivox Recorder

Captivox Recorder is a local Windows meeting recorder designed for reliable downstream transcription with WhisperX. It captures microphone input and system audio, writes a structured session folder, and produces output that fits a session-based transcription workflow.

## Why Captivox Recorder

Most recording tools are built for generic audio capture. Captivox Recorder is built for a different goal: predictable meeting capture for transcription pipelines.

**Key design principles**

- Local-first recording on Windows
- Microphone and system audio capture
- Session-based output with traceable artifacts
- Predictable handoff to WhisperX
- Stability and reproducibility over feature bloat

## Current scope

Captivox Recorder currently focuses on:

- Windows-only recording
- Microphone capture
- System audio capture via loopback
- Mixed audio output
- Session folder output
- Integration with WhisperX-oriented processing

## Session output

A successful recording produces a session folder such as:

```
C:\recordings\meeting-2026-03-12-0830
  audio_mix.wav
  manifest.json
  READY
```

This makes the recorder suitable for session-based downstream processing.

## Project status

Captivox Recorder is under active development. The current focus is not broad feature expansion, but robust session handling, reliable output, and integration with the transcription workflow.
