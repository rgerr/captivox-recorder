using System.Drawing;
using LocalCaptureAgent.Controls;

namespace LocalCaptureAgent
{
    // De pagina Captivox Pro in de gratis versie: kort wat de betaalde versie toevoegt, hoe het overstappen gaat en een knop
    // naar de projectpagina. Of Pro al te krijgen is en waar de knop heen gaat, staat in ProOffer.
    public sealed partial class MainForm
    {
        private const string ShellNavProOffer = "pro";
        private TabPage? _proOfferTabPage;

        private static readonly string[] ProOfferFeatures =
        {
            "Sprekerherkenning: wie zei wat, en sprekers een naam geven.",
            "Verslag en samenvatting met besluiten en acties die je zelf bevestigt, elk met het letterlijke citaat uit het gesprek.",
            "Een actielijst, en Outlook-taken voor de acties die je bevestigt.",
            "Koppeling met je Outlook-agenda en hulp bij het voorbereiden van overleggen.",
            "Organisaties strikt gescheiden en versleuteld bewaard.",
        };

        private TabPage BuildProOfferPage()
        {
            var page = ShellNewPage("Captivox Pro");
            _proOfferTabPage = page;
            var wrapping = new List<Label>();

            Label Wrap(Label label)
            {
                wrapping.Add(label);
                return label;
            }

            var root = ShellStack();
            root.Padding = new Padding(4, 4, 12, 12);
            ShellStackAdd(root, ShellLabel("Captivox Pro", 18F, FontStyle.Bold, ShellPalette.Text, new Padding(0, 0, 0, 6)));
            ShellStackAdd(root, Wrap(ShellLabel("Meer uit je overleggen halen, op dezelfde manier als deze gratis versie: alles draait op je eigen pc en je opnames blijven van jou. Privacy voorop.", 10F, FontStyle.Regular, ShellPalette.TextSecondary, new Padding(0, 0, 0, 14))));

            var featuresCard = ShellNewCard();
            var features = ShellStack();
            ShellStackAdd(features, ShellSectionTitle("Wat Pro toevoegt"));
            foreach (var feature in ProOfferFeatures)
            {
                var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 8) };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                row.Controls.Add(new ShellIconTile { Glyph = ShellIcon.Check, TileColor = ShellPalette.AccentSoft, ForeColor = ShellPalette.Accent, Size = new Size(28, 28), Margin = new Padding(0, 0, 12, 0) }, 0, 0);
                row.Controls.Add(Wrap(ShellLabel(feature, 10.5F, FontStyle.Regular, ShellPalette.Text, new Padding(0, 4, 0, 0))), 1, 0);
                ShellStackAdd(features, row);
            }

            ShellStackAdd(features, Wrap(ShellLabel("Alles werkt lokaal, zonder cloud en zonder account.", 9.75F, FontStyle.Regular, ShellPalette.TextSecondary, new Padding(0, 6, 0, 0))));
            featuresCard.Controls.Add(features);
            ShellStackAdd(root, featuresCard);

            var statusCard = ShellNewCard();
            var status = ShellStack();
            ShellStackAdd(status, ShellSectionTitle("Stand"));
            ShellStackAdd(status, Wrap(ShellLabel(ProOffer.StatusText, 11F, FontStyle.Bold, ShellPalette.Text, new Padding(0, 0, 0, 6))));
            ShellStackAdd(status, Wrap(ShellLabel("Stap je over, dan neemt Pro je opnames uit deze gratis versie over; de gratis versie blijft gewoon werken.", 10F, FontStyle.Regular, ShellPalette.TextSecondary, new Padding(0, 0, 0, 12))));
            var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
            var notify = ShellButton(ProOffer.ButtonText, primary: true, (_, _) => OpenExternalUrl(ProOffer.Url));
            notify.Margin = new Padding(0);
            SetUiToolTip(notify, ProOffer.Url);
            actions.Controls.Add(notify);
            ShellStackAdd(status, actions);
            statusCard.Controls.Add(status);
            ShellStackAdd(root, statusCard);

            page.Controls.Add(root);
            // Lange regels breken af op de breedte van de pagina (hooguit een prettige leesbreedte).
            void FitLabels()
            {
                var width = Math.Max(LogicalToDeviceUnits(260), Math.Min(page.ClientSize.Width - LogicalToDeviceUnits(160), LogicalToDeviceUnits(760)));
                foreach (var label in wrapping) label.MaximumSize = new Size(width, 0);
            }

            page.Resize += (_, _) => FitLabels();
            FitLabels();
            return page;
        }
    }
}

namespace LocalCaptureAgent.Services
{
    // Vertalingen (Nederlands, Engels, Duits, Frans) van de pagina Captivox Pro en van ProOffer. Hier en niet in de andere
    // tabellen: alleen deze pagina mag in de gratis versie de onderdelen van de betaalde versie noemen.
    internal static partial class UiText
    {
        static partial void AddProOfferTexts()
        {
            Add("Sprekerherkenning: wie zei wat, en sprekers een naam geven.", "Speaker recognition: who said what, and naming the speakers.", "Sprechererkennung: wer was gesagt hat, und Sprecher benennen.", "Reconnaissance des intervenants : qui a dit quoi, et nommer les intervenants.");
            Add("Verslag en samenvatting met besluiten en acties die je zelf bevestigt, elk met het letterlijke citaat uit het gesprek.", "Minutes and summary with decisions and actions you confirm yourself, each with the literal quote from the conversation.", "Protokoll und Zusammenfassung mit Beschlüssen und Aufgaben, die Sie selbst bestätigen, jeweils mit dem wörtlichen Zitat aus dem Gespräch.", "Compte rendu et résumé avec des décisions et des actions que vous confirmez vous-même, chacune avec la citation exacte de la conversation.");
            Add("Een actielijst, en Outlook-taken voor de acties die je bevestigt.", "An action list, and Outlook tasks for the actions you confirm.", "Eine Aufgabenliste und Outlook-Aufgaben für die Aufgaben, die Sie bestätigen.", "Une liste d'actions, et des tâches Outlook pour les actions que vous confirmez.");
            Add("Koppeling met je Outlook-agenda en hulp bij het voorbereiden van overleggen.", "Connection with your Outlook calendar and help preparing meetings.", "Verbindung mit Ihrem Outlook-Kalender und Hilfe bei der Vorbereitung von Besprechungen.", "Connexion à votre agenda Outlook et aide à la préparation des réunions.");
            Add("Organisaties strikt gescheiden en versleuteld bewaard.", "Organizations kept strictly separate and stored encrypted.", "Organisationen strikt getrennt und verschlüsselt gespeichert.", "Organisations strictement séparées et stockées de façon chiffrée.");
            Add("Meer uit je overleggen halen, op dezelfde manier als deze gratis versie: alles draait op je eigen pc en je opnames blijven van jou. Privacy voorop.", "Get more out of your meetings, the same way as this free version: everything runs on your own PC and your recordings stay yours. Privacy first.", "Holen Sie mehr aus Ihren Besprechungen heraus, genau wie mit dieser kostenlosen Version: Alles läuft auf Ihrem eigenen PC und Ihre Aufnahmen gehören Ihnen. Datenschutz an erster Stelle.", "Tirez davantage de vos réunions, comme avec cette version gratuite : tout fonctionne sur votre propre PC et vos enregistrements restent les vôtres. La confidentialité avant tout.");
            Add("Wat Pro toevoegt", "What Pro adds", "Was Pro hinzufügt", "Ce que Pro ajoute");
            Add("Alles werkt lokaal, zonder cloud en zonder account.", "Everything works locally, without a cloud and without an account.", "Alles funktioniert lokal, ohne Cloud und ohne Konto.", "Tout fonctionne en local, sans cloud et sans compte.");
            Add("Stand", "Status", "Stand", "État");
            Add("Stap je over, dan neemt Pro je opnames uit deze gratis versie over; de gratis versie blijft gewoon werken.", "If you switch, Pro takes over your recordings from this free version; the free version keeps working.", "Wenn Sie wechseln, übernimmt Pro Ihre Aufnahmen aus dieser kostenlosen Version; die kostenlose Version funktioniert weiter.", "Si vous passez à Pro, il reprend vos enregistrements de cette version gratuite ; la version gratuite continue de fonctionner.");
            Add("Captivox Pro is te krijgen.", "Captivox Pro is available.", "Captivox Pro ist erhältlich.", "Captivox Pro est disponible.");
            Add("Captivox Pro komt binnenkort.", "Captivox Pro is coming soon.", "Captivox Pro kommt bald.", "Captivox Pro arrive bientôt.");
            Add("Captivox Pro bekijken", "View Captivox Pro", "Captivox Pro ansehen", "Découvrir Captivox Pro");
            Add("Houd me op de hoogte", "Keep me posted", "Auf dem Laufenden halten", "Me tenir informé");
        }
    }
}
