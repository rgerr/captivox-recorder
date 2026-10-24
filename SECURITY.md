# Beveiliging

## Ondersteunde versies

Alleen de nieuwste release krijgt beveiligingsfixes.

| Versie | Ondersteund |
| --- | --- |
| 1.1.x | ja |
| 1.0.x | nee |
| 0.9.0-beta1 en ouder | nee |

## Een kwetsbaarheid melden

Meld een kwetsbaarheid **niet** in een openbaar issue. Gebruik de privemelding van GitHub: tab **Security**, knop **Report a vulnerability** in deze repository. Alleen de beheerder ziet je melding.

Vermeld de versie, wat je deed, wat er gebeurde en wat de gevolgen kunnen zijn. Stuur geen echte opnames of transcripten mee; een nagebootst voorbeeld is genoeg.

Je krijgt binnen een week een eerste reactie. Een bevestigde kwetsbaarheid wordt in een nieuwe release opgelost en in de [CHANGELOG](CHANGELOG.md) vermeld, met dank aan de melder als die dat wil.

## Waar het om gaat

Bijvoorbeeld:

- opnames, transcripten of logboeken die terechtkomen waar ze niet horen, of die anderen op dezelfde pc kunnen lezen;
- paden, bestandsnamen of instellingen waarmee iemand code kan laten uitvoeren (bijvoorbeeld via de worker of `install-prereqs.ps1`);
- netwerkverkeer dat er volgens [PRIVACY.md](PRIVACY.md) niet hoort te zijn.

## Ontwerp

- Alles draait lokaal onder het eigen Windows-account; beheerdersrechten zijn niet nodig.
- De recorder start de worker zelf, met `-ExecutionPolicy Bypass` alleen voor dat ene script.
- Een opname is een gewone map: je kunt altijd zien wat er bewaard is.

---

**In English**: please report vulnerabilities privately through GitHub (*Security*, *Report a vulnerability*), not in a public issue, and do not attach real recordings. Only the latest 1.1.x release is supported.
