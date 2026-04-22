# Installation and Configuration (0.9.0-beta1)

Captivox Recorder is distributed as a Windows installer.  The source code is not currently published.  Follow these steps to install and configure the application.

## System requirements

- Windows 11 (preferred) or Windows 10 (64‑bit).
- A working microphone and (optionally) a speaker output to record system audio.
- 4 GB RAM minimum; a GPU is recommended for faster transcription.
- Sufficient disk space for recordings (each hour of audio uses roughly 60 MB).

## Installing Captivox

1. Download the latest installer (`CaptivoxRecorder_Setup_0.9.0-beta1.exe`) from the [Releases](https://github.com/rgerr/captivox-recorder/releases) page.
2. Double‑click the installer to start the setup wizard.
3. Accept the licence agreement (GPL‑based open distribution with donationware).
4. Choose a destination folder (the default is `%LocalAppData%\Programs\Captivox Recorder`).
5. Click **Install**.  The installer will:
   - unpack the recorder executable and assets,
   - install a private Python runtime and required libraries for WhisperX,
   - place a bundled copy of FFmpeg for audio encoding/decoding,
   - create a start menu shortcut and optional desktop shortcut.
6. Once the installation completes, click **Finish** to launch the recorder.

## Running the application

After installation, run **Captivox Recorder** from the Start Menu or by launching `CaptivoxRecorder.exe` from the install directory.  The first time it runs, it creates a configuration file at:

    %LocalAppData%\CaptivoxRecorder\settings\application_settings.json

The recorder automatically detects the bundled Python and FFmpeg and sets the appropriate paths.

## Updating Captivox

To upgrade to a new version:

1. Download the new installer from the Releases page.
2. Run it; it will detect the existing installation and perform an in‑place upgrade.
3. Your session folders and settings will be preserved.

## Uninstalling Captivox

To remove the application:

1. Open **Add or Remove Programs** in Windows Settings.
2. Find **Captivox Recorder** and choose **Uninstall**.
3. You can delete any remaining session folders under your Documents if you no longer need them.

## Configuration options

Advanced users can adjust settings in `application_settings.json`.  Key fields include:

- `worker.pythonExe`: path to the Python executable used by the worker.  Set automatically by the installer.
- `worker.model`: identifier or path of the Whisper model (e.g. "qwen2.5:7b-instruct").
- `worker.provider`: "local" or "cloud".  Determines how the structured‑insights pass runs.
- `provider.baseUrl`: URL of the remote provider (if using a cloud provider).
- `local.modelDir`: directory where local models are stored.
- `recordings.rootDir`: where to store session folders (default: `Documents\CaptivoxSessions`).

Example:

    {
      "worker": {
        "pythonExe": "C:\\Users\\YourName\\AppData\\Local\\Programs\\Captivox Recorder\\python\\python.exe",
        "model": "qwen2.5:7b-instruct",
        "provider": "local"
      },
      "recordings": {
        "rootDir": "C:\\Users\\YourName\\Documents\\CaptivoxSessions"
      },
      "provider": {
        "baseUrl": null
      }
    }

You can edit this file with a text editor while the application is not running.  Always back up before making changes.

## Linking Microsoft 365 accounts

To enrich transcripts with calendar context:

1. Open **Settings** → **Accounts** in the recorder.
2. Click **Add Microsoft account**.  A browser window will open for you to log in via Microsoft Graph.
3. Grant the requested permissions to read your calendar.
4. Repeat for additional accounts.
5. In the library view, you can choose which accounts to use for each session.

## Advanced installation (developers)

Building from source is currently not supported for the public beta.  The application and worker code are not published; please use the installer above.
