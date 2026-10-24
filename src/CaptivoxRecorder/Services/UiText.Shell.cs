namespace LocalCaptureAgent.Services;

// Vertalingen (Nederlands, Engels, Duits, Frans): Vandaag, Overleggen, Meldingen, de opnamepagina, de bovenbalk, het
// systeemvak en de meldingen van Windows.
internal static partial class UiText
{
    private static void AddShellTexts()
    {
        // ---------- Menu en bovenbalk ----------
        Add("Vandaag", "Today", "Heute", "Aujourd'hui");
        Add("Overleggen", "Meetings", "Besprechungen", "Réunions");
        Add("Meldingen", "Notifications", "Benachrichtigungen", "Notifications");
        Add("Instellingen", "Settings", "Einstellungen", "Paramètres");
        Add("Geen problemen gezien", "No problems found", "Keine Probleme erkannt", "Aucun problème");
        Add("Kies je apparaten", "Choose your devices", "Geräte auswählen", "Choisissez vos appareils");
        Add("Klaar om op te nemen", "Ready to record", "Bereit zur Aufnahme", "Prêt à enregistrer");
        Add("Probleem met de opname", "Problem with the recording", "Problem mit der Aufnahme", "Problème d'enregistrement");
        Add("Het bestand is vol; stop en start een nieuwe opname.", "The file is full; stop and start a new recording.", "Die Datei ist voll; beenden Sie die Aufnahme und starten Sie eine neue.", "Le fichier est plein ; arrêtez et démarrez un nouvel enregistrement.");
        Add("De opname is gestopt; stop en start opnieuw.", "The recording has stopped; stop and start again.", "Die Aufnahme wurde unterbrochen; beenden Sie sie und starten Sie neu.", "L'enregistrement s'est arrêté ; arrêtez et redémarrez.");
        Add("Opname loopt", "Recording", "Aufnahme läuft", "Enregistrement en cours");
        Add("Geluid wordt bewaakt", "Sound is being monitored", "Ton wird überwacht", "Le son est surveillé");
        Add("Opname afronden", "Finishing recording", "Aufnahme wird abgeschlossen", "Finalisation de l'enregistrement");
        Add("Even geduld", "Please wait", "Einen Moment bitte", "Veuillez patienter");
        Add("Apparaten laden", "Loading devices", "Geräte werden geladen", "Chargement des appareils");
        Add("Kies eerst je apparaten", "Choose your devices first", "Wählen Sie zuerst Ihre Geräte", "Choisissez d'abord vos appareils");
        Add("Klik hier of ga naar Instellingen, Opnemen", "Click here or go to Settings, Recording", "Hier klicken oder Einstellungen, Aufnahme öffnen", "Cliquez ici ou allez dans Paramètres, Enregistrement");
        Add("Microfoon: {0}  |  Geluid van anderen: {1}", "Microphone: {0}  |  Others: {1}", "Mikrofon: {0}  |  Ton der anderen: {1}", "Micro : {0}  |  Son des autres : {1}");
        Add("Microfoon en geluid: {0}", "Microphone and sound: {0}", "Mikrofon und Ton: {0}", "Micro et son : {0}");
        Add("Microfoon: {0}  |  Anderen: {1}", "Microphone: {0}  |  Others: {1}", "Mikrofon: {0}  |  Andere: {1}", "Micro : {0}  |  Autres : {1}");
        Add("Het bestand is bijna vol: {0}", "The file is almost full: {0}", "Die Datei ist fast voll: {0}", "Le fichier est presque plein : {0}");
        Add("nog {0} min", "{0} min left", "noch {0} Min.", "encore {0} min");
        Add("Geen geluid van de anderen {0}", "No sound from the others {0}", "Kein Ton von den anderen {0}", "Aucun son des autres {0}");
        Add("Je microfoon is stil {0}", "Your microphone is silent {0}", "Ihr Mikrofon ist still {0}", "Votre micro est silencieux {0}");
        Add("of meer", "or more", "oder mehr", "ou plus");
        Add("open", "open", "offen", "ouverts");

        // ---------- Systeemvak ----------
        Add("Captivox: probleem met de opname", "Captivox: problem with the recording", "Captivox: Problem mit der Aufnahme", "Captivox : problème d'enregistrement");
        Add("Captivox: opname loopt ({0} min)", "Captivox: recording ({0} min)", "Captivox: Aufnahme läuft ({0} Min.)", "Captivox : enregistrement en cours ({0} min)");
        Add("Captivox: 1 ding wacht op je", "Captivox: 1 item needs you", "Captivox: 1 Eintrag wartet auf Sie", "Captivox : 1 élément vous attend");
        Add("Captivox: {0} dingen wachten op je", "Captivox: {0} items need you", "Captivox: {0} Einträge warten auf Sie", "Captivox : {0} éléments vous attendent");
        Add("Captivox: klaar om op te nemen", "Captivox: ready to record", "Captivox: bereit zur Aufnahme", "Captivox : prêt à enregistrer");
        Add("1 ding wacht op je", "1 item needs you", "1 Eintrag wartet auf Sie", "1 élément vous attend");
        Add("{0} dingen wachten op je", "{0} items need you", "{0} Einträge warten auf Sie", "{0} éléments vous attendent");
        Add("Niets wacht op je", "Nothing needs you", "Nichts wartet auf Sie", "Rien ne vous attend");
        Add("Venster tonen", "Show window", "Fenster anzeigen", "Afficher la fenêtre");
        Add("Venster verbergen", "Hide window", "Fenster ausblenden", "Masquer la fenêtre");
        Add("Opname starten", "Start recording", "Aufnahme starten", "Démarrer l'enregistrement");
        Add("Opname stoppen", "Stop recording", "Aufnahme beenden", "Arrêter l'enregistrement");
        Add("Opname afronden...", "Finishing recording...", "Aufnahme wird abgeschlossen...", "Finalisation de l'enregistrement...");
        Add("Opnamemap openen", "Open recordings folder", "Aufnahmeordner öffnen", "Ouvrir le dossier des enregistrements");
        Add("Afsluiten", "Exit", "Beenden", "Quitter");
        Add("Opname gestart.", "Recording started.", "Aufnahme gestartet.", "Enregistrement démarré.");
        Add("Opname afgerond.", "Recording finished.", "Aufnahme abgeschlossen.", "Enregistrement terminé.");
        Add("Applicatie draait in de systeemtray.", "Captivox is still running in the system tray.", "Captivox läuft im Infobereich weiter.", "Captivox continue de tourner dans la zone de notification.");

        // ---------- Vandaag ----------
        Add("Goedemorgen", "Good morning", "Guten Morgen", "Bonjour");
        Add("Goedemiddag", "Good afternoon", "Guten Tag", "Bon après-midi");
        Add("Goedenavond", "Good evening", "Guten Abend", "Bonsoir");
        Add("Aan de slag", "Getting started", "Erste Schritte", "Premiers pas");
        Add("Welkom bij {0}. Zo maak je je eerste opname:", "Welcome to {0}. Here is how to make your first recording:", "Willkommen bei {0}. So erstellen Sie Ihre erste Aufnahme:", "Bienvenue dans {0}. Voici comment faire votre premier enregistrement :");
        Add("1. Kies bij Instellingen, Opnemen je microfoon en het apparaat waarop je de anderen hoort.", "1. In Settings, Recording, choose your microphone and the device you hear the others on.", "1. Wählen Sie unter Einstellungen, Aufnahme Ihr Mikrofon und das Gerät, über das Sie die anderen hören.", "1. Dans Paramètres, Enregistrement, choisissez votre micro et l'appareil sur lequel vous entendez les autres.");
        Add("{0} (Ctrl+Shift+F9)", "{0} (Ctrl+Shift+F9)", "{0} (Strg+Umschalt+F9)", "{0} (Ctrl+Maj+F9)");
        Add("2. Druk op Opname starten (of Ctrl+Shift+F9).", "2. Press Start recording (or Ctrl+Shift+F9).", "2. Klicken Sie auf Aufnahme starten (oder Strg+Umschalt+F9).", "2. Cliquez sur Démarrer l'enregistrement (ou Ctrl+Maj+F9).");
        Add("3. Of voeg een bestaand bestand toe via Overleggen, Audio importeren.", "3. Or add an existing file via Meetings, Import audio.", "3. Oder fügen Sie eine vorhandene Datei über Besprechungen, Audio importieren hinzu.", "3. Ou ajoutez un fichier existant via Réunions, Importer un audio.");
        Add("Apparaten kiezen", "Choose devices", "Geräte auswählen", "Choisir les appareils");
        Add("Naar Overleggen", "Go to Meetings", "Zu den Besprechungen", "Aller aux réunions");
        Add("Nog niets voor vandaag.", "Nothing for today yet.", "Für heute noch nichts.", "Rien pour aujourd'hui.");
        Add("Wacht op jou", "Needs you", "Wartet auf Sie", "Vous attend");
        Add("Wacht op jou ({0})", "Needs you ({0})", "Wartet auf Sie ({0})", "Vous attend ({0})");
        Add("Open Overleggen om te zien wat er klaarstaat.", "Open Meetings to see what is ready.", "Öffnen Sie Besprechungen, um zu sehen, was bereitsteht.", "Ouvrez Réunions pour voir ce qui est prêt.");
        Add("1 opname vraagt aandacht", "1 recording needs attention", "1 Aufnahme erfordert Aufmerksamkeit", "1 enregistrement demande votre attention");
        Add("{0} opnames vragen aandacht", "{0} recordings need attention", "{0} Aufnahmen erfordern Aufmerksamkeit", "{0} enregistrements demandent votre attention");
        Add("Mislukt of onderbroken; je kunt opnieuw laten uitschrijven", "Failed or interrupted; you can transcribe it again", "Fehlgeschlagen oder unterbrochen; Sie können erneut transkribieren lassen", "Échoué ou interrompu ; vous pouvez relancer la transcription");
        Add("Bekijken", "View", "Ansehen", "Voir");
        Add("Niets wacht op je. Mooi.", "Nothing needs you. Nice.", "Nichts wartet auf Sie. Gut so.", "Rien ne vous attend. Parfait.");
        Add("Laatste meldingen", "Latest notifications", "Neueste Benachrichtigungen", "Dernières notifications");
        Add("Nog geen meldingen.", "No notifications yet.", "Noch keine Benachrichtigungen.", "Aucune notification pour l'instant.");
        Add("Alle meldingen", "All notifications", "Alle Benachrichtigungen", "Toutes les notifications");
        Add("De opname stopt en wordt bewaard. Daarna kies je onder Instellingen, Opnemen het apparaat waar Teams op afspeelt, en start je een nieuwe opname.", "The recording stops and is saved. Then choose the device Teams plays on under Settings, Recording, and start a new recording.", "Die Aufnahme wird beendet und gespeichert. Wählen Sie dann unter Einstellungen, Aufnahme das Gerät, auf dem Teams abspielt, und starten Sie eine neue Aufnahme.", "L'enregistrement s'arrête et est conservé. Choisissez ensuite dans Paramètres, Enregistrement l'appareil sur lequel Teams joue le son, puis démarrez un nouvel enregistrement.");
        Add("Opname nu stoppen?", "Stop the recording now?", "Aufnahme jetzt beenden?", "Arrêter l'enregistrement maintenant ?");
        Add("Ander apparaat kiezen", "Choose another device", "Anderes Gerät wählen", "Choisir un autre appareil");
        Add("Opnemen werkt wel; uitschrijven kan pas weer na het herstellen.", "Recording still works; transcription is available again once this is repaired.", "Die Aufnahme funktioniert; die Transkription erst wieder nach der Reparatur.", "L'enregistrement fonctionne ; la transcription sera à nouveau possible après la réparation.");

        // ---------- Opname loopt ----------
        Add("Geluid", "Sound", "Ton", "Son");
        Add("Jouw microfoon", "Your microphone", "Ihr Mikrofon", "Votre micro");
        Add("Geluid van de anderen (Teams)", "Sound from the others (Teams)", "Ton der anderen (Teams)", "Son des autres (Teams)");
        Add("Hoor jij de anderen wel? Dan neemt Captivox het verkeerde apparaat op. Stop de opname, kies onder Instellingen, Opnemen het apparaat waar Teams op afspeelt en start opnieuw.", "Can you hear the others? Then Captivox is recording the wrong device. Stop the recording, choose the device Teams plays on under Settings, Recording, and start again.", "Hören Sie die anderen? Dann nimmt Captivox das falsche Gerät auf. Beenden Sie die Aufnahme, wählen Sie unter Einstellungen, Aufnahme das Gerät, auf dem Teams abspielt, und starten Sie neu.", "Vous entendez les autres ? Alors Captivox enregistre le mauvais appareil. Arrêtez l'enregistrement, choisissez dans Paramètres, Enregistrement l'appareil sur lequel Teams joue le son, puis recommencez.");
        Add("Het is stil, klopt", "It is quiet, that's right", "Es ist still, das stimmt", "C'est normal, c'est silencieux");
        Add("Stand van de opname", "Recording status", "Stand der Aufnahme", "État de l'enregistrement");
        Add("Na het stoppen", "After stopping", "Nach dem Beenden", "Après l'arrêt");
        Add("1. Uitschrijven (op deze pc, na elkaar)", "1. Transcribe (on this PC, one at a time)", "1. Transkribieren (auf diesem PC, nacheinander)", "1. Transcription (sur ce PC, l'une après l'autre)");
        Add("2. Klaar: je krijgt een melding en vindt de tekst onder Overleggen", "2. Done: you get a notification and find the text under Meetings", "2. Fertig: Sie erhalten eine Benachrichtigung und finden den Text unter Besprechungen", "2. Terminé : vous recevez une notification et trouvez le texte dans Réunions");
        Add("Niet actief", "Not active", "Nicht aktiv", "Inactif");
        Add("Gestopt", "Stopped", "Beendet", "Arrêté");
        Add("Geen geluid {0}", "No sound {0}", "Kein Ton {0}", "Aucun son {0}");
        Add("Wacht op geluid", "Waiting for sound", "Warte auf Ton", "En attente de son");
        Add("Geluid komt binnen", "Sound coming in", "Ton kommt an", "Le son arrive");
        Add("Even stil ({0}s)", "Quiet for a moment ({0}s)", "Kurz still ({0} s)", "Silence ({0} s)");
        Add("Stil (bevestigd)", "Silent (confirmed)", "Still (bestätigt)", "Silencieux (confirmé)");
        Add("Het bestand is vol: nieuwe audio wordt niet meer opgeslagen. Stop en start een nieuwe opname.", "The file is full: new audio is no longer saved. Stop and start a new recording.", "Die Datei ist voll: neuer Ton wird nicht mehr gespeichert. Beenden Sie die Aufnahme und starten Sie eine neue.", "Le fichier est plein : le nouvel audio n'est plus enregistré. Arrêtez et démarrez un nouvel enregistrement.");
        Add("De opname is gestopt: {0}. Stop en start opnieuw.", "The recording has stopped: {0}. Stop and start again.", "Die Aufnahme wurde unterbrochen: {0}. Beenden Sie sie und starten Sie neu.", "L'enregistrement s'est arrêté : {0}. Arrêtez et redémarrez.");
        Add("onbekende fout", "unknown error", "unbekannter Fehler", "erreur inconnue");
        Add("De opname loopt en het geluid komt binnen.", "Recording, and sound is coming in.", "Die Aufnahme läuft und der Ton kommt an.", "L'enregistrement est en cours et le son arrive.");
        Add("De opname loopt.", "Recording.", "Die Aufnahme läuft.", "L'enregistrement est en cours.");
        Add("Ruimte in het bestand: nog ongeveer {0} uur {1} minuten.", "Space left in the file: about {0} h {1} min.", "Platz in der Datei: noch etwa {0} Std. {1} Min.", "Espace restant dans le fichier : environ {0} h {1} min.");
        Add("Er loopt geen opname.", "No recording in progress.", "Es läuft keine Aufnahme.", "Aucun enregistrement en cours.");
        Add("Let op: nog maar {0:0.0} GB vrij op {1}.", "Note: only {0:0.0} GB free on {1}.", "Achtung: nur noch {0:0.0} GB frei auf {1}.", "Attention : plus que {0:0.0} Go libres sur {1}.");
        Add("Schijfruimte: {0:0} GB vrij op {1}.", "Disk space: {0:0} GB free on {1}.", "Speicherplatz: {0:0} GB frei auf {1}.", "Espace disque : {0:0} Go libres sur {1}.");

        // ---------- Meldingen ----------
        Add("De meldingen van Captivox op deze pc in de laatste drie dagen.", "Captivox notifications on this PC from the last three days.", "Die Benachrichtigungen von Captivox auf diesem PC aus den letzten drei Tagen.", "Les notifications de Captivox sur ce PC au cours des trois derniers jours.");
        Add("Geen meldingen in de laatste drie dagen.", "No notifications in the last three days.", "Keine Benachrichtigungen in den letzten drei Tagen.", "Aucune notification au cours des trois derniers jours.");
        Add("Meldingen, {0} nieuw", "Notifications, {0} new", "Benachrichtigungen, {0} neu", "Notifications, {0} nouvelles");
        Add("Instellingen openen", "Open settings", "Einstellungen öffnen", "Ouvrir les paramètres");

        // ---------- Overleggen ----------
        Add("Lijst met overleggen", "List of meetings", "Liste der Besprechungen", "Liste des réunions");
        Add("Zoek op titel, persoon of wat er gezegd is", "Search title, person or words", "Titel, Person oder Text suchen", "Titre, personne ou mots");
        Add("Zoeken in overleggen", "Search meetings", "Besprechungen durchsuchen", "Rechercher dans les réunions");
        Add("Zoeken", "Search", "Suchen", "Rechercher");
        Add("Periode", "Period", "Zeitraum", "Période");
        Add("Afgelopen 7 dagen", "Last 7 days", "Letzte 7 Tage", "7 derniers jours");
        Add("Afgelopen 30 dagen", "Last 30 days", "Letzte 30 Tage", "30 derniers jours");
        Add("Afgelopen 90 dagen", "Last 90 days", "Letzte 90 Tage", "90 derniers jours");
        Add("Alles", "All", "Alle", "Tout");
        Add("Audio importeren", "Import audio", "Audio importieren", "Importer un audio");
        Add("Bezig met laden...", "Loading...", "Wird geladen...", "Chargement...");
        Add("Laden lukte niet; de details staan in het logboek.", "Loading failed; the details are in the log.", "Laden fehlgeschlagen; Details stehen im Protokoll.", "Le chargement a échoué ; les détails sont dans le journal.");
        Add("Nog geen overleggen opgenomen.", "No meetings recorded yet.", "Noch keine Besprechungen aufgenommen.", "Aucune réunion enregistrée pour l'instant.");
        Add("Niets vraagt je aandacht. Mooi.", "Nothing needs your attention. Nice.", "Nichts erfordert Ihre Aufmerksamkeit. Gut so.", "Rien ne demande votre attention. Parfait.");
        Add("Niets gevonden voor deze zoekopdracht.", "Nothing found for this search.", "Für diese Suche wurde nichts gefunden.", "Aucun résultat pour cette recherche.");
        Add("Geen overleggen in deze periode.", "No meetings in this period.", "Keine Besprechungen in diesem Zeitraum.", "Aucune réunion sur cette période.");
        Add("Kies links een overleg.", "Choose a meeting on the left.", "Wählen Sie links eine Besprechung.", "Choisissez une réunion à gauche.");
        Add("Opnieuw uitschrijven", "Transcribe again", "Erneut transkribieren", "Relancer la transcription");
        Add("Meer acties", "More actions", "Weitere Aktionen", "Plus d'actions");
        Add("Uitgeschreven tekst", "Transcript", "Transkript", "Transcription");
        Add("Hier ging iets mis. Met Opnieuw uitschrijven probeert Captivox het nog een keer.", "Something went wrong here. With Transcribe again, Captivox tries once more.", "Hier ist etwas schiefgegangen. Mit Erneut transkribieren versucht Captivox es noch einmal.", "Un problème est survenu. Avec Relancer la transcription, Captivox réessaie.");
        Add("Hier ging iets mis; de details staan in de map van de opname.", "Something went wrong here; the details are in the recording's folder.", "Hier ist etwas schiefgegangen; die Details stehen im Ordner der Aufnahme.", "Un problème est survenu ; les détails se trouvent dans le dossier de l'enregistrement.");
        Add("Nog niets beschikbaar. Is het overleg al uitgeschreven?", "Nothing available yet. Has the meeting been transcribed?", "Noch nichts verfügbar. Wurde die Besprechung schon transkribiert?", "Rien de disponible pour l'instant. La réunion a-t-elle été transcrite ?");
        Add("Map openen", "Open folder", "Ordner öffnen", "Ouvrir le dossier");
        Add("Uitgeschreven tekst opslaan", "Save transcript", "Transkript speichern", "Enregistrer la transcription");
        Add("Ondertitels opslaan (voor de meeste videospelers)", "Save subtitles (for most video players)", "Untertitel speichern (für die meisten Videoplayer)", "Enregistrer les sous-titres (pour la plupart des lecteurs vidéo)");
        Add("Ondertitels opslaan (voor webspelers)", "Save subtitles (for web players)", "Untertitel speichern (für Webplayer)", "Enregistrer les sous-titres (pour les lecteurs web)");
        Add("Titel wijzigen", "Change title", "Titel ändern", "Modifier le titre");
        Add("Naar de Prullenbak", "Move to Recycle Bin", "In den Papierkorb", "Mettre à la corbeille");
        Add("Dat lukte niet", "That did not work", "Das hat nicht geklappt", "Cela n'a pas fonctionné");
        Add("1 deelnemer", "1 participant", "1 Teilnehmer", "1 participant");
        Add("{0} deelnemers", "{0} participants", "{0} Teilnehmer", "{0} participants");

        // ---------- Regels in de lijst van Overleggen (MeetingListService) ----------
        Add("Opname mislukt", "Recording failed", "Aufnahme fehlgeschlagen", "Échec de l'enregistrement");
        Add("Uitschrijven mislukt", "Transcription failed", "Transkription fehlgeschlagen", "Échec de la transcription");
        Add("Wordt uitgeschreven", "Transcribing", "Wird transkribiert", "Transcription en cours");
        Add("Opname onderbroken", "Recording interrupted", "Aufnahme unterbrochen", "Enregistrement interrompu");
        Add("Wacht op uitschrijven", "Waiting to be transcribed", "Wartet auf Transkription", "En attente de transcription");
        Add("Nog niet verwerkt", "Not processed yet", "Noch nicht verarbeitet", "Pas encore traité");
        Add("Klaar", "Done", "Fertig", "Terminé");
        Add("Opname zonder titel", "Untitled recording", "Aufnahme ohne Titel", "Enregistrement sans titre");
        Add("vandaag {0}", "today {0}", "heute {0}", "aujourd'hui {0}");
        Add("gisteren {0}", "yesterday {0}", "gestern {0}", "hier {0}");
        Add("{0} min", "{0} min", "{0} Min.", "{0} min");
        Add("{0} uur {1} min", "{0} h {1} min", "{0} Std. {1} Min.", "{0} h {1} min");
        Add("{0} uur", "{0} h", "{0} Std.", "{0} h");

        // ---------- Meldingen van Windows ----------
        Add("Transcript gereed", "Transcript ready", "Transkript bereit", "Transcription prête");
        Add("Sessie {0} is afgerond. Open direct het transcript of de sessiemap.", "Session {0} is finished. Open the transcript or the session folder.", "Sitzung {0} ist abgeschlossen. Öffnen Sie direkt das Transkript oder den Sitzungsordner.", "La session {0} est terminée. Ouvrez la transcription ou le dossier de la session.");
        Add("Open transcript", "Open transcript", "Transkript öffnen", "Ouvrir la transcription");
        Add("Open map", "Open folder", "Ordner öffnen", "Ouvrir le dossier");
        Add("Sessiemap openen mislukt", "Opening the session folder failed", "Sitzungsordner konnte nicht geöffnet werden", "Impossible d'ouvrir le dossier de la session");
        Add("Transcript openen mislukt", "Opening the transcript failed", "Transkript konnte nicht geöffnet werden", "Impossible d'ouvrir la transcription");

        // ---------- Starten (Program) ----------
        Add("Er is een onverwachte fout opgetreden. Zie logbestand in:{0}", "An unexpected error occurred. See the log file in:{0}", "Ein unerwarteter Fehler ist aufgetreten. Siehe Protokolldatei in:{0}", "Une erreur inattendue s'est produite. Consultez le journal dans :{0}");
        Add("Onverwachte fout", "Unexpected error", "Unerwarteter Fehler", "Erreur inattendue");
        Add("{0} kon niet starten. Zie logbestand in:{1}", "{0} could not start. See the log file in:{1}", "{0} konnte nicht gestartet werden. Siehe Protokolldatei in:{1}", "{0} n'a pas pu démarrer. Consultez le journal dans :{1}");
        Add("{0} draait al, maar reageerde niet binnen 10 seconden. Mogelijk is hij nog aan het opstarten of druk bezig. Probeer het over een minuut opnieuw.", "{0} is already running but did not respond within 10 seconds. It may still be starting up or busy. Try again in a minute.", "{0} läuft bereits, hat aber nicht innerhalb von 10 Sekunden reagiert. Vielleicht startet es noch oder ist beschäftigt. Versuchen Sie es in einer Minute erneut.", "{0} est déjà en cours d'exécution mais n'a pas répondu dans les 10 secondes. Il est peut-être encore en train de démarrer ou occupé. Réessayez dans une minute.");
        Add("Blijft dit gebeuren, controleer dan eerst of er een opname loopt voordat je het bestaande proces via Taakbeheer stopt.", "If this keeps happening, first check whether a recording is in progress before you stop the existing process in Task Manager.", "Wenn das weiterhin passiert, prüfen Sie zuerst, ob eine Aufnahme läuft, bevor Sie den vorhandenen Prozess im Task-Manager beenden.", "Si cela se reproduit, vérifiez d'abord qu'aucun enregistrement n'est en cours avant d'arrêter le processus existant dans le Gestionnaire des tâches.");
    }
}
