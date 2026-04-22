# Testing and Validation Guidelines (0.9.0-beta1)

This document outlines recommended tests for verifying Captivox Recorder functionality.

## 1. Installation tests

- **Installer integrity**: Verify that `CaptivoxRecorder_Setup_0.9.0-beta1.exe` downloads without corruption (check the file hash if provided).
- **Setup wizard**: Run the installer and confirm that it installs without errors and places files in the chosen directory.
- **Dependencies**: Confirm that the bundled Python runtime, WhisperX dependencies and FFmpeg are installed in the application folder.

## 2. First run tests

- **Launch**: Start the recorder from the Start Menu and confirm that it opens without error dialogs.
- **Settings file**: Check that `%LocalAppData%\CaptivoxRecorder\settings\application_settings.json` has been created with valid paths.
- **Audio devices**: Verify that the microphone and system audio devices are detected in the UI.  Change devices and ensure levels respond.

## 3. Recording and session tests

- **Record**: Start a recording, speak and play some audio on the system.  Stop recording after a minute.
- **Session folder**: Navigate to `Documents\CaptivoxSessions` and confirm that a new folder has been created with a timestamp name containing `source.wav` and `session.json`.
- **Ready handoff**: In the recorder, click **Ready**.  Verify that a `READY` file appears in the session folder.

## 4. Worker and transcription tests

- **Worker detection**: After clicking Ready, wait for the worker to process the session.  The status in the library should change from "In Progress" to "Transcribed".
- **Transcript output**: Confirm that `transcript.json` and `transcript.vtt` are created.  Open them to check that the content matches your speech.
- **Alignment**: In the session review workspace, click on words in the transcript and ensure playback jumps to the correct timestamp.

## 5. Structured insights and actions (beta)

- **Summary generation**: For a test meeting with clear decisions and actions, verify that the **Insights** pane shows a summarised narrative.
- **Action extraction**: Check that the **Actions** pane lists decisions, to‑dos and assignments as bullet points.  Note that this feature is still under validation and may not capture all actions.

## 6. Diagnostics export

- **Export**: Use the **Export diagnostics** function in the session review.  Confirm that it creates a ZIP archive containing logs and transcripts.
- **Anonymisation**: The diagnostics archive should not include raw audio or sensitive data.  Inspect the contents to verify.

## 7. Microsoft 365 integration tests

- **Account linking**: Add a Microsoft account via Settings.  Ensure that the login flow appears in your default browser and completes successfully.
- **Context enrichment**: After linking, create a test meeting on your calendar, then record and mark as ready.  Verify that the session details (subject, attendees) appear in the library and review pages.

## 8. Multiple account tests

- **Link additional account**: Add a second Microsoft account and ensure you can select which account to use for context.
- **Account preferences**: Check that the application remembers which account was used for previous sessions.

## 9. Local AI settings tests

- **Hardware estimate**: In **Settings** → **AI**, run the hardware estimation tool.  It should display recommended models.
- **Model selection**: Change the Whisper model or language model to a different setting and verify that the worker respects the choice.

## 10. Error handling tests

- **No microphone**: Disable your microphone and attempt to record.  The application should display an error without crashing.
- **Large files**: Record a long meeting (>1 hour) and ensure that the worker still processes it.
- **Interrupted transcription**: Simulate closing the recorder while the worker is running.  The worker should continue processing in the background.

## 11. Internationalisation tests

- **UI languages**: Set your Windows language to a non‑English language (e.g. Dutch) and verify that Captivox displays labels and buttons in that language.
- **Transcript languages**: Record a session in another language.  WhisperX should transcribe it correctly (subject to model support).

## 12. Security tests

- **File permissions**: Verify that session folders are only accessible to the current user by default.
- **No telemetry**: Use a network monitor to confirm that Captivox does not send recordings or transcripts to remote servers unless a cloud provider is enabled.

These guidelines are not exhaustive; feel free to expand them based on your environment and use cases.
