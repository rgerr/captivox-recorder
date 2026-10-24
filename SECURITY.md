# Security

## Supported versions

Only the latest release receives security fixes.

| Version | Supported |
| --- | --- |
| 2.0.x | yes |
| 1.1.x | no |
| 1.0.x | no |
| 0.9.0-beta1 and older | no |

## Reporting a vulnerability

Please do **not** report a vulnerability in a public issue. Use GitHub's private reporting instead: the **Security** tab, **Report a vulnerability** button in this repository. Only the maintainer can see your report.

Include the version, what you did, what happened and what the impact could be. Don't send real recordings or transcripts; a made-up example is enough.

You will get a first response within a week. A confirmed vulnerability is fixed in a new release and mentioned in the [CHANGELOG](CHANGELOG.md), with credit to the reporter if they wish.

## What counts

For example:

- recordings, transcripts or logs ending up where they don't belong, or being readable by other users of the same PC;
- paths, file names or settings that let someone run code (for example through the worker or `install-prereqs.ps1`);
- network traffic that should not be there according to [PRIVACY.md](PRIVACY.md).

## Design

- Everything runs locally under your own Windows account; no administrator rights are needed.
- The recorder starts the worker itself, using `-ExecutionPolicy Bypass` only for that one script.
- A recording is an ordinary folder: you can always see what has been stored.
