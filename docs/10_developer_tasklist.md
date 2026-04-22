# Developer Tasklist

This document tracks outstanding tasks and priorities for the Captivox Recorder project following the v0.9.0-beta1 release.

## High priority

- Confirm installer portability on multiple Windows 11 machines; fix any path or runtime issues discovered.
- Investigate and fix known UI bugs affecting the session library and insights display.
- Refine multi-account matching logic for linked Microsoft 365 accounts.
- Improve hardware capability estimate and adjust transcription parameters accordingly.
- Resolve `NETSDK1137` and nullable warnings in the C# project build.
- Harden the worker bootstrap script (`install-prereqs.ps1`) for error handling and logging.

## Medium priority

- Explore adding Llama or Mistral as alternative local models; evaluate performance and licensing.
- Add cross-session search and recall features to the library.
- Implement editable structured insights with ability to accept or reject suggestions.
- Create export profiles (Markdown, HTML, Word, JSON) for transcripts and insights.
- Add sort and filter options to the single-session and library UI.

## Low priority / backlog

- Begin work on cross-platform support (macOS, Linux) once Windows version stabilises.
- Research integration with other calendar providers beyond Microsoft 365.
- Investigate GPU acceleration options for WhisperX and pyannote on supported hardware.
- Design automation workflows for task creation and follow-up emails.

## Task tracking

For each task, create a corresponding GitHub issue or project card to allow tracking and discussion. Assign tasks to milestones according to the phase outlined in the implementation plan.
