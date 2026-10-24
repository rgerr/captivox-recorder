# Wijzigingen

## 1.1.0 (2026-10-24)

- Sprekerherkenning (pyannote) zit niet meer in de gratis versie. Transcripten tonen alleen de tijd per stuk tekst.
- Een in versie 1.0.0 bewaard Hugging Face-token wordt bij het starten verwijderd; het is niet meer nodig.
- De optie `-PreparePyannoteCommunityRuntime` van `install-prereqs.ps1` is vervallen.

## 1.0.0 (2026-10-22)

Eerste vrije versie onder de GPL-3.0, met broncode.

- Opnemen van microfoon en pc-geluid tegelijk (WASAPI), met niveaumeters en een spraakvriendelijke mix naar 16 kHz mono.
- Uitschrijven op de eigen pc met WhisperX, optioneel met sprekerherkenning (pyannote 3.1); NVIDIA (CUDA), AMD (Vulkan) of de processor.
- Bestaande audio- en videobestanden toevoegen en laten uitschrijven.
- Opnamelijst met transcript: kopieren, opslaan als .txt, .srt of .vtt, naam geven, opnieuw uitschrijven, naar de prullenbak.
- Bewaking tijdens de opname: stille microfoon, wegvallend geluidsapparaat, bijna volle opname.
- Systeemvak en sneltoets Ctrl+Shift+F9.
- Eigen gegevensmap (`%LOCALAPPDATA%\CaptivoxRecorderFree`) en opslagmap in het gebruikersprofiel.
- Installatieprogramma (`setup.exe`, per gebruiker, zonder beheerdersrechten) dat aan het eind de transcriptie-omgeving kan installeren, met een snelkoppeling om die later te herstellen; daarnaast een zip zonder installatie.
- `install-prereqs.ps1` installeert de transcriptie-omgeving zonder beheerdersrechten; de recorder neemt de paden die het script instelt meteen over, ook als hij al open staat.

Vervangt de technische preview 0.9.0-beta1.
