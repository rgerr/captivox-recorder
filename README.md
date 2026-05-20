# Captivox Recorder

**v7.25.1 – local‑first meeting recorder and personal assistant**  
Captivox Recorder turns your Windows machine into a personal meeting and action‑tracking assistant. It captures microphone and system audio, writes each recording to a unique session folder and runs local transcription through WhisperX. Everything stays on your machine. No cloud uploads, no Graph APIs.

## What Captivox does as of v7.25.1

- Records microphone + system audio and writes each recording to its own session folder.
- Uses an explicit READY hand‑off before the worker begins transcription.
- Runs local transcription through WhisperX. Derived AI outputs remain separate from raw transcripts.
- Shows a calmer **single‑session review** workspace in the library.
- Surfaces **direct transcript insights** in the session detail area.
- Generates **structured session insights** from completed sessions.
- Exports **diagnostics** such as audio levels and diarization quality.

### Personal assistant features

- **Local MCP service** (`captivox-local`) for tasks like finding sessions, retrieving transcripts, searching multiple sessions, or retrieving open actions, agreements, decisions and questions.
- **Meeting preparation** via `prepare_meeting_context`: identify your next meeting, gather previous discussions, open actions and relevant context from your documents and Outlook Classic calendar/mail.
- **Day and week overview** combining your agenda, confirmed actions, Outlook tasks and flagged mails.
- **Action inbox with retention**: review proposed actions, confirm or reject them, export to Outlook tasks and mark them complete. Completed or rejected items disappear from your list after a short retention period.
- **Context documents**: index local folders (including synced OneDrive/SharePoint) and search them for meeting prep. v7.28 will add support for reading Word comments, tracked changes and PDF annotations.
- **Stable MCP path support**: easily configure Claude Desktop to use a persistent Captivox MCP binary for reliable local integration.

### Outlook Classic integration

Captivox integrates with Outlook Classic via COM. It can read your agenda, find matching meetings, search mail metadata and flagged mails, and create draft tasks/emails on demand. Writing or sending mail only happens after you explicitly approve it; deletion and modification of existing items are never automated. We deliberately avoid Graph/New Outlook APIs for a local‑first workflow.

### Product direction

Captivox continues to prioritise a local‑first architecture and explicit user control. The roadmap includes document feedback extraction (Word comments, PDF annotations) in v7.28 and reliable briefing packs in v7.29+. Teams chat integration and Graph API access are currently out of scope. See the [`docs/`](docs) directory for details.

### Installation

Currently this is an internal tool. We publish preview builds under Releases for Windows x64. To try Captivox:

1. Download the latest `CaptivoxRecorder_v7_25_1_..._bundle.zip` from the releases page.
2. Extract it to a folder on your computer (no installer required).
3. Run `CaptivoxRecorder.exe`.
4. In Claude Desktop, configure the local MCP path to point at `Captivox.LocalMcp.exe` in the `publish` subfolder of your extraction.

See `docs/install_guide.md` for step‑by‑step instructions.

### Source code

This repository contains the recorder GUI, local MCP service and test scripts. The code is published for transparency and collaboration. Issues and pull requests are welcome, but please note that the roadmap is driven by internal use cases and may change without notice.

### Security & privacy

Captivox is local‑first. We do not send your audio, transcripts or context to any cloud service by default. Outlook integration uses Classic COM rather than Graph. See [PRIVACY.md](PRIVACY.md) and [SECURITY.md](SECURITY.md) for details.

---

_Captivox Recorder is a work in progress. v7.25.1 introduces a stable MCP path and an action inbox with retention; v7.28 will add document feedback extraction; v7.29 will add reliable briefing packs._
