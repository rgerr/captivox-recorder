# Captivox Recorder

![Captivox Recorder](captivox-banner.png)

**Gratis Windows-programma dat je gesprekken opneemt en ze op je eigen pc uitschrijft.** Captivox neemt tegelijk je microfoon en het geluid van de pc op (Teams, Zoom, Google Meet of een ander programma), en zet het gesprek daarna op de achtergrond om in tekst met [WhisperX](https://github.com/m-bain/whisperX). Er gaat niets naar de cloud.

- Versie: **2.0.0** (oktober 2026)
- Licentie: [GPL-3.0](LICENSE)
- Voor: Windows 10 en 11 (64-bit), Nederlandstalige interface

> In English: see [the summary at the bottom](#in-english).

## Wat het doet

- **Overzichtelijk**: links *Vandaag*, *Overleggen*, *Meldingen* en *Instellingen*. Vandaag toont wat aandacht nodig heeft en een grote knop om op te nemen; Overleggen is de lijst van al je gesprekken met zoeken en de uitgeschreven tekst ernaast.
- **Opnemen**: jouw microfoon en het pc-geluid tegelijk, met een niveaumeter per bron. Elke opname krijgt een eigen map.
- **Spraakvriendelijke mix**: beide bronnen worden samengevoegd tot een heldere opname (16 kHz mono), afgestemd op spraakherkenning.
- **Uitschrijven**: automatisch na het stoppen, met Whisper (standaard `large-v2`). Kies de taal van het gesprek: Nederlands, Engels, Duits, Frans, Spaans of Italiaans.
- **Videokaart**: NVIDIA (CUDA) en AMD (Vulkan) worden herkend; zonder geschikte kaart werkt alles op de processor.
- **Bestanden toevoegen**: een bestaande opname (wav, mp3, m4a, aac, mp4) uitschrijven via *Audio importeren* op Overleggen.
- **Na afloop**: transcript lezen, kopieren, opslaan als `.txt`, `.srt` of `.vtt`, een naam geven, opnieuw uitschrijven met andere instellingen, of naar de prullenbak.
- **Meldingen**: een Windows-melding als een gesprek is uitgeschreven of als er iets misging; de laatste meldingen staan ook in het programma.
- **Onopvallend**: draait in het systeemvak; starten en stoppen met **Ctrl+Shift+F9**, venster tonen of verbergen met **Ctrl+Shift+F10**. Kan desgewenst met Windows mee starten.
- **Bewaakt de opname**: waarschuwt als de microfoon minutenlang stil is, als een geluidsapparaat wegvalt of als de opname te groot wordt.
- **Opruimen** (optioneel, standaard uit): oude opnames en transcripten na een ingesteld aantal dagen weggooien.

## Privacy in het kort

- Geen account, geen telemetrie, geen cloud. Opnames en transcripten staan in je eigen gebruikersmap (`%USERPROFILE%\Captivox\Opnames`).
- Internet is alleen nodig bij de installatie (Python-pakketten en FFmpeg) en om de spraakmodellen eenmalig te downloaden van Hugging Face.

Meer in [PRIVACY.md](PRIVACY.md).

**Wees open als je opneemt.** Laat de anderen in het gesprek weten dat je opneemt en waarvoor. Een opname met stemmen en namen is een persoonsgegeven; bewaar hem niet langer dan nodig en deel hem niet zomaar.

## Wat je nodig hebt

- Windows 10 of 11, 64-bit.
- Ongeveer 10 GB vrije ruimte: ongeveer 3 GB voor de transcriptie-omgeving, 4 tot 5 GB voor de modellen, en ruimte voor je opnames (ongeveer 2 MB per minuut na het mixen).
- Internet tijdens de installatie en de eerste transcriptie.
- Liefst een NVIDIA- of AMD-videokaart. Op alleen de processor werkt het ook, maar dan kan uitschrijven langer duren dan het gesprek zelf. Kies dan in Instellingen het model `medium` of `small`.

## Installeren

1. **Download** `CaptivoxRecorder-2.0.0-setup.exe` onder [Releases](https://github.com/rgerr/captivox-recorder/releases). Controleer desgewenst de SHA-256 met het `.sha256`-bestand ernaast:
   ```powershell
   Get-FileHash .\CaptivoxRecorder-2.0.0-setup.exe -Algorithm SHA256
   ```
2. **Installeren**: dubbelklik het bestand. Het programma is niet digitaal ondertekend; Windows SmartScreen kan daarom waarschuwen. Kies dan *Meer informatie* en *Toch uitvoeren*. Beheerdersrechten zijn niet nodig: Captivox komt in `%LOCALAPPDATA%\Programs\CaptivoxRecorderFree`, met een snelkoppeling in het Startmenu.
3. **Transcriptie-omgeving**: laat aan het eind *Transcriptie-omgeving nu installeren* aangevinkt (eenmalig, 10 tot 30 minuten, ongeveer 1 GB download). Er opent een PowerShell-venster. Ontbreken Python 3.10 of FFmpeg, dan vraagt het of het ze via `winget` mag installeren; antwoord met Enter. Sluit het venster als het klaar is. Later opnieuw of herstellen kan via Startmenu, *Captivox Recorder (gratis)*, *Transcriptie-omgeving installeren of herstellen*. Met een AMD-videokaart voer je daarna eenmalig in PowerShell, in de programmamap, `.\install-prereqs.ps1 -PrepareAmdGpuRuntime` uit.
4. **Kies je apparaten**: in *Instellingen*, *Opnemen*: de microfoon en het apparaat waarop je de anderen hoort (meestal je headset of luidsprekers). Druk daarna op **Opname starten**.

Liever zonder installatie? Download dan `CaptivoxRecorder-2.0.0-win-x64.zip`, pak hem uit in een map waar je zelf mag schrijven, voer in PowerShell in die map `Get-ChildItem -Recurse | Unblock-File` uit en daarna `powershell -NoProfile -ExecutionPolicy Bypass -File .\install-prereqs.ps1`, en start `CaptivoxRecorder.exe`.

Verwijderen gaat via Windows, *Apps*, *Captivox Recorder (gratis versie)*. Je opnames blijven staan; Captivox vraagt of de transcriptie-omgeving en de instellingen ook weg mogen.

De eerste transcriptie duurt langer: dan worden de modellen gedownload (Whisper large-v2 ongeveer 3 GB, het uitlijnmodel voor de taal ongeveer 1 GB).


## Zo werkt het

```
CaptivoxRecorder.exe                      workers\captivox_transcription_worker.ps1
  neemt microfoon + pc-geluid op (WASAPI)    kijkt naar mappen met een READY-bestand
  mixt naar audio_mix.wav (16 kHz mono)  ->  claimt de opname
  schrijft manifest.json en READY            draait WhisperX via Python             
                                             schrijft transcript.txt en transcript.json
```

Elke opname is een gewone map, die je zelf kunt bekijken, kopieren of weggooien:

| Bestand | Inhoud |
| --- | --- |
| `audio_mix.wav` | de opname |
| `manifest.json` | stand van de opname en de transcriptie |
| `session.log` | logboek van de opname |
| `transcript.txt` | het uitgeschreven gesprek |
| `transcript.json` | het transcript met tijden per stuk tekst |
| `exports\` | ondertitels (`.srt`, `.vtt`), als je die aanzet |

Instellingen en logboeken van het programma staan in `%LOCALAPPDATA%\CaptivoxRecorderFree`.

## Zelf bouwen

Je hebt de [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) nodig, en voor het installatieprogramma [Inno Setup 6](https://jrsoftware.org/isinfo.php) (of bouw met `-SkipInstaller`).

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Het resultaat komt in `dist\`: een map en een zip met een zelfstandig `CaptivoxRecorder.exe` (geen .NET-installatie nodig), de worker en de hulpscripts, en het installatieprogramma `CaptivoxRecorder-<versie>-setup.exe`, elk met een SHA-256.

| Map | Inhoud |
| --- | --- |
| `src\CaptivoxRecorder` | de recorder (C#, .NET 8, WinForms, [NAudio](https://github.com/naudio/NAudio)) |
| `workers` | de transcriptieworker (PowerShell en Python) |
| `tools` | hulpscripts voor de AMD-route (whisper.cpp met Vulkan) |
| `install-prereqs.ps1` | installeert de transcriptie-omgeving |
| `installer` | het installatieprogramma (Inno Setup) |

## Bekende beperkingen

- De oudere schermen (uitgebreide instellingen en bibliotheek) staan nog onder *Instellingen*, *Geavanceerd*.
- Alleen Windows; de interface is Nederlands.
- Het programma is niet digitaal ondertekend en werkt zichzelf niet bij. Nieuwe versies verschijnen onder Releases.
- Een opname kan ongeveer 3 uur duren (de grens van een WAV-bestand bij 48 kHz stereo). Captivox waarschuwt een kwartier van tevoren; start daarna een nieuwe opname.
- Kies je tijdens een opname een ander geluidsapparaat in Windows, dan neemt Captivox nog het oude op. Kies het juiste apparaat voor je start.
- De gratis versie zet niet in de tekst wie wat zei (geen sprekerherkenning).

## Vragen, fouten en ideeen

Gebruik [Issues](https://github.com/rgerr/captivox-recorder/issues). Zet er geen opnames, transcripten of andere gevoelige gegevens in. Een beveiligingsprobleem meld je niet in een issue; zie [SECURITY.md](SECURITY.md).

## Licentie

Copyright (c) 2026 rgerr.

Captivox Recorder is vrije software: je mag hem gebruiken, bestuderen, aanpassen en verspreiden onder de voorwaarden van de [GNU General Public License versie 3](LICENSE) of (naar keuze) een latere versie. De software komt **zonder enige garantie**.

De onderdelen van anderen die Captivox gebruikt, staan met hun licenties in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## In English

Captivox Recorder is a free (GPL-3.0) Windows app that records your microphone and your PC's audio at the same time (for Teams, Zoom, Meet or any other call), and transcribes the conversation **locally** with WhisperX (no speaker diarization in the free version). Nothing is sent to the cloud; no account and no telemetry. The user interface is in Dutch.

Quick start: download `CaptivoxRecorder-2.0.0-setup.exe` from Releases and run it (no admin rights needed); keep *Transcriptie-omgeving nu installeren* checked at the end to set up the transcription runtime (Python 3.10, WhisperX, FFmpeg). A portable zip is available as well. Build from source with the .NET 8 SDK and Inno Setup 6: `.\build.ps1`.
