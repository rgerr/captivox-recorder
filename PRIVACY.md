# Privacy

Captivox Recorder werkt volledig op je eigen pc. Er is geen account, geen server van Captivox en geen telemetrie.

## Wat er op je pc blijft

| Gegeven | Waar | Wie kan erbij |
| --- | --- | --- |
| Opnames (`audio_mix.wav`) en transcripten | `%USERPROFILE%\Captivox\Opnames` (instelbaar) | jij en beheerders van de pc |
| Instellingen en logboeken | `%LOCALAPPDATA%\CaptivoxRecorderFree` | jij en beheerders van de pc |
| Hugging Face-token (optioneel) | `%LOCALAPPDATA%\CaptivoxRecorderFree\secrets`, versleuteld met Windows DPAPI | alleen jouw Windows-account op deze pc |

Logboeken bevatten technische gegevens (apparaatnamen, tijden, foutmeldingen, mappen), geen gespreksinhoud.

Kies je als opslagmap een map die met de cloud synchroniseert (OneDrive, Dropbox, een netwerkschijf), dan gaan je opnames via die dienst wel van je pc af. De standaardmap synchroniseert niet.

## Wanneer Captivox internet gebruikt

- **Bij de installatie**: `install-prereqs.ps1` haalt Python-pakketten op bij PyPI en kan Python en FFmpeg installeren via `winget`.
- **Bij de eerste transcriptie**: de spraakmodellen komen van Hugging Face (Whisper, het uitlijnmodel voor de taal, en met sprekerherkenning de modellen van pyannote, met jouw token).
- **Met de AMD-route**: `tools\Build-WhisperCppVulkanRuntime.ps1` haalt whisper.cpp op bij GitHub en een model bij Hugging Face.

Daarbij gaan geen opnames of transcripten mee. Daarna werkt Captivox ook zonder internet.

## Verwijderen

- Een opname: kies **Naar prullenbak** in Captivox, of gooi de map weg.
- Alles: verwijder de programmamap, `%LOCALAPPDATA%\CaptivoxRecorderFree` en je opslagmap. De transcriptie-omgeving staat in `%LOCALAPPDATA%\CaptivoxRecorderFree\runtime`; gedownloade modellen staan in `%USERPROFILE%\.cache\huggingface` en `%USERPROFILE%\.cache\torch`.

## Opnemen van gesprekken

Een opname met stemmen en namen bevat persoonsgegevens van iedereen in het gesprek. Laat de anderen weten dat je opneemt en waarvoor, bewaar opnames niet langer dan nodig en deel ze alleen met wie ze echt nodig heeft. Captivox ruimt niets vanzelf op: dat bepaal je zelf.

---

**In English**: Captivox Recorder runs entirely on your PC. No account, no server, no telemetry. Recordings and transcripts stay in `%USERPROFILE%\Captivox\Opnames`; settings and logs in `%LOCALAPPDATA%\CaptivoxRecorderFree`. The optional Hugging Face token is encrypted with Windows DPAPI. Internet is only used to install the runtime and to download models once; no audio or text is uploaded.
