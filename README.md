# Captivox Recorder

**0.9.0-beta1 Windows**

Captivox Recorder is a local-first Windows meeting recorder and transcription workspace.
It records microphone and system audio, writes a structured session folder, and hands off transcription through a filesystem-driven workflow:

`recorder -> session folder -> READY -> worker -> transcription runner -> WhisperX -> transcript`

The session folder remains the source of truth. Audio, transcripts, and derived outputs stay on your machine.

## What Captivox does today

### Current beta capabilities

- Record **microphone + system audio**
- Write each recording to its own **session folder**
- Use an explicit **READY** handoff to the worker
- Run local transcription through **WhisperX**
- Keep transcript processing and derived AI output separated
- Show a calmer **single-session review** workspace in the library
- Surface **direct transcript actions** in the session detail area
- Generate **structured insights** from completed sessions
- Export **diagnostics**
- Support **local/cloud provider choice** for derived enrichment
- Support **Microsoft 365 / Exchange Online** meeting context via interactive browser login
- Keep multiple linked Microsoft 365 accounts locally
- Offer per-account source selection for calendars and optional mailbox context
- Support **local AI settings** with:
  - hardware estimate
  - active local model
  - separate local model download flow
- Provide visible **UI localisation / language support**

### Still being validated / not final yet

The following areas exist in the product direction, but should not be presented as fully mature yet:

- full runtime validation of **multiple Microsoft 365 accounts** on representative tenants
- confirmed best-match behaviour across multiple linked accounts on real sessions
- shared mailbox / shared calendar validation
- broader plausibility validation of the **AI hardware estimate** across different systems
- final semantic quality of actions / decisions as a mature end layer
- richer cross-session search, recall, and broader automation

## Product direction

Captivox is not meant to become a generic cloud meeting workspace.
The direction is:

1. stronger single-session review
2. stronger structured insights
3. better contextual search and recall
4. automation on top of the same local workflow

## Installation

### Installer

Download the latest installer from the [Releases](https://github.com/rgerr/captivox-recorder/releases) page and run it on Windows.

### Source code

The source code is **not published** in this repository at this time.
This repository is currently used as the public product and release page for installer-based distribution.

## Donations

Captivox is currently distributed as donationware.

If you want to support development, you can donate here:
[PayPal donation link](https://www.paypal.com/donate/?business=J8L5Z2UP4K6HL&no_recurring=0&currency_code=EUR)

## Feedback

If you find a bug or want to request an improvement, please use the GitHub issue templates in this repository.

## Security

Please read [SECURITY.md](SECURITY.md) before reporting security issues.

## Privacy

Captivox is designed as a local-first product.
See [PRIVACY.md](PRIVACY.md) for the current privacy note.
