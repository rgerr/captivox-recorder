# Current State (0.9.0-beta1)

## Status Summary

Captivox Recorder v0.9.0-beta1 is a donationware public beta. This means the core functionality is available without payment and we are seeking feedback to improve the product before a formal release.

## Confirmed features

- **Recording and session folders** – The app records microphone and system audio and writes each session to its own folder, including metadata and audio files.
- **READY handoff** – The app places a marker when recordings are ready for processing; the worker uses this to start transcription.
- **WhisperX transcription** – Transcripts are generated locally using the WhisperX pipeline. Voice activity detection and diarisation (pyannote) are built in.
- **Single-session review workspace** – You can review and edit transcripts in a dedicated library view with scrollable actions.
- **Structured insights** – The worker generates structured summaries of decisions, actions and key questions. These are displayed in the session view.
- **Diagnostics** – A diagnostics export is available to help debug issues (logs, system information, model metadata).
- **Provider choice** – Users can choose between local and cloud providers for enrichment (e.g. Qwen for local model).
- **Microsoft 365 integration (beta)** – The app can link to Microsoft 365 / Exchange Online via interactive login and access meeting context (calendar and optionally mail). You can link multiple M365 accounts.
- **Local AI settings** – The settings panel shows an estimate of hardware capability, allows selection of the active local model and includes a model download interface.
- **UI localisation** – UI strings are translatable and the current beta includes English and Dutch localisations.

## Known limitations / open items

- **Multi-account M365 matching** – Matching sessions to meetings across multiple linked accounts is still being validated.
- **Shared mailbox / calendar support** – Scenarios with shared mailboxes and calendars have not been fully tested.
- **Hardware estimate accuracy** – The accuracy of the hardware capability estimate and its impact on transcription settings needs further testing on diverse machines.
- **Search and recall** – Cross-session search and recall are not yet implemented.
- **Automation and export profiles** – Automation based on transcripts and custom export profiles are planned but not available.
- **Cross-platform support** – The app is currently Windows-only; other platforms are not supported in this beta.

## Feedback

We welcome bug reports and feature requests via GitHub issues. Please include your environment details and steps to reproduce.

## Planned work

See the [Implementation plan](09_implementation_plan.md) for details on upcoming tasks and roadmap.
