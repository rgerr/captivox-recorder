# Security Policy

## Supported version

At this stage, only the latest public beta release is considered supported:

- `0.9.0-beta1`

## Reporting a vulnerability

A dedicated security contact e-mail address is not published yet.

Until that address is available:

- **do not** post sensitive vulnerability details in a public GitHub issue
- use public issues only for non-sensitive security-related questions
- wait for the dedicated security contact to be added here before sending sensitive disclosure details

This file will be updated as soon as the dedicated security reporting address is available.

## Scope

Security reports should focus on issues such as:

- unintended exposure of recordings, transcripts, or diagnostics
- insecure handling of local session data
- unsafe logging or export behaviour
- privilege / path / installer issues on Windows
- secrets or credentials accidentally stored in output or configuration

## Notes

Captivox is a local-first Windows application.  
The core workflow is intentionally filesystem-driven and should remain transparent and inspectable.
