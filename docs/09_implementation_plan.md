# Implementation Plan

## Overview

This document outlines the implementation plan for the Captivox Recorder project as it evolves from the v0.9.0-beta1 donationware release toward a stable 1.0 product. The focus remains on local-first operation, structured insights, and eventual automation. The plan is subject to change based on feedback and testing.

## Phase 1 – Stabilization of current beta

- Complete testing on diverse Windows 11 systems to confirm portability.
- Validate multi-account matching for Microsoft 365.
- Improve the hardware capability estimate logic by benchmarking on a wider range of machines.
- Fix outstanding UI and workflow bugs reported by beta users.
- Refine structured insights output and action classification based on user feedback.
- Expand documentation and user help resources.

## Phase 2 – Enhanced insights and search

- Introduce cross-session search and recall across the session library.
- Provide richer insights with decisions grouped by category and references back to transcript segments.
- Allow editing of insights and export in common formats (e.g. JSON, CSV).
- Add filter and sort controls to the library view.

## Phase 3 – Automation and export profiles

- Build automation capabilities (e.g. task creation, follow-up reminders) based on recognized actions or decisions in transcripts.
- Allow users to define export profiles for transcripts and insights (e.g. Markdown, HTML, Word).
- Add customizable prompts for summarization and action extraction.

## Phase 4 – Platform and model expansion

- Investigate cross-platform support (e.g. macOS, Linux) without compromising the local-first approach.
- Integrate additional local models beyond Qwen (e.g. Llama, Mistral) if they offer better performance or languages.
- Provide the option to run large models via cloud while keeping control over transcript privacy.

## Notes

- Each phase will be revisited and reprioritised based on user feedback and technical feasibility.
- Donation-based funding will inform how aggressively new features can be developed.
