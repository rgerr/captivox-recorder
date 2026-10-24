using System.Text.Json;
using Captivox.Recorder.Models;
using Captivox.Recorder.Services;

namespace Captivox.Recorder;

internal sealed class SettingsForm : Form
{
    private readonly ApplicationSettings _original;
    private readonly TextBox _outputRoot = new() { Width = 360 };
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox _model = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox _profile = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly CheckBox _transcribe = new() { Text = "Opnames automatisch uitschrijven", AutoSize = true };
    private readonly CheckBox _diarization = new() { Text = "Sprekers herkennen (wie zegt wat)", AutoSize = true };
    private readonly TextBox _token = new() { Width = 360, UseSystemPasswordChar = true };
    private readonly Label _tokenState = new() { AutoSize = true, ForeColor = Color.FromArgb(100, 116, 139) };
    private readonly NumericUpDown _minSpeakers = new() { Minimum = 0, Maximum = 20, Width = 60 };
    private readonly NumericUpDown _maxSpeakers = new() { Minimum = 0, Maximum = 20, Width = 60 };
    private readonly CheckBox _exportSrt = new() { Text = "Ook ondertitels maken (.srt)", AutoSize = true };
    private readonly CheckBox _exportVtt = new() { Text = "Ook webondertitels maken (.vtt)", AutoSize = true };
    private readonly TextBox _pythonExe = new() { Width = 360 };
    private readonly TextBox _ffmpegPath = new() { Width = 360 };
    private readonly CheckBox _minimizeToTray = new() { Text = "Bij sluiten naar het systeemvak (opnemen met Ctrl+Shift+F9 blijft werken)", AutoSize = true };
    private readonly CheckBox _startWithWindows = new() { Text = "Starten met Windows (in het systeemvak)", AutoSize = true };
    private readonly CheckBox _diagnostics = new() { Text = "Extra diagnose bij elke opname bewaren", AutoSize = true };
    private bool _deleteToken;

    public ApplicationSettings Result { get; private set; }
    public bool WorkerSettingsChanged { get; private set; }

    public SettingsForm(ApplicationSettings settings)
    {
        _original = settings;
        Result = Clone(settings);
        Text = "Instellingen";
        Icon = Branding.GetAppIconOrDefault();
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        Font = new Font("Segoe UI", 9.5F);
        ClientSize = new Size(560, 640);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildGeneralTab());
        tabs.TabPages.Add(BuildTranscriptionTab());
        tabs.TabPages.Add(BuildAdvancedTab());

        var ok = new Button { Text = "Opslaan", DialogResult = DialogResult.None, Width = 90 };
        var cancel = new Button { Text = "Annuleren", DialogResult = DialogResult.Cancel, Width = 90 };
        ok.Click += (_, _) => Accept();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(8) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.Add(tabs);
        Controls.Add(buttons);
        LoadValues();
    }

    private static ApplicationSettings Clone(ApplicationSettings settings)
        => JsonSerializer.Deserialize<ApplicationSettings>(JsonSerializer.Serialize(settings)) ?? new ApplicationSettings();

    private static FlowLayoutPanel Page()
        => new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };

    private static Label Heading(string text)
        => new() { Text = text, AutoSize = true, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), Margin = new Padding(0, 10, 0, 4) };

    private static Label Hint(string text)
        => new() { Text = text, AutoSize = true, MaximumSize = new Size(500, 0), ForeColor = Color.FromArgb(100, 116, 139), Margin = new Padding(0, 2, 0, 6) };

    private static Control WithButton(Control field, string buttonText, EventHandler onClick)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        var button = new Button { Text = buttonText, AutoSize = true };
        button.Click += onClick;
        row.Controls.Add(field);
        row.Controls.Add(button);
        return row;
    }

    private TabPage BuildGeneralTab()
    {
        var page = Page();
        page.Controls.Add(Heading("Opslagmap"));
        page.Controls.Add(WithButton(_outputRoot, "Bladeren...", (_, _) => BrowseFolder(_outputRoot)));
        page.Controls.Add(Hint("Elke opname krijgt hier een eigen map met het geluid, het transcript en een logboek. Kies een map op deze pc; een map die met de cloud synchroniseert (zoals OneDrive) stuurt je gesprekken alsnog naar buiten."));
        page.Controls.Add(Heading("Programma"));
        page.Controls.Add(_minimizeToTray);
        page.Controls.Add(_startWithWindows);
        return new TabPage("Algemeen") { Controls = { page } };
    }

    private TabPage BuildTranscriptionTab()
    {
        var page = Page();
        page.Controls.Add(_transcribe);
        page.Controls.Add(Heading("Taal van de gesprekken"));
        page.Controls.Add(_language);
        page.Controls.Add(Heading("Model"));
        page.Controls.Add(_model);
        page.Controls.Add(Hint("Grotere modellen zijn nauwkeuriger maar trager. Het model wordt de eerste keer eenmalig gedownload (tot 3 GB)."));
        page.Controls.Add(Heading("Rekenkracht"));
        page.Controls.Add(_profile);
        page.Controls.Add(Hint("Automatisch gebruikt een NVIDIA- of AMD-videokaart als die er is, en anders de processor."));

        page.Controls.Add(Heading("Sprekers"));
        page.Controls.Add(_diarization);
        page.Controls.Add(Hint("Sprekerherkenning gebruikt pyannote. Daarvoor heb je een gratis Hugging Face-account en een leestoken nodig, en accepteer je eenmalig de voorwaarden van pyannote/speaker-diarization-3.1 en pyannote/segmentation-3.0 op huggingface.co. Het token wordt versleuteld op deze pc bewaard en alleen gebruikt om de modellen te downloaden; je gesprekken blijven op je pc."));
        page.Controls.Add(WithButton(_token, "Token wissen", (_, _) => { _deleteToken = true; _token.Text = string.Empty; UpdateTokenState(); }));
        page.Controls.Add(_tokenState);
        var speakers = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        speakers.Controls.Add(new Label { Text = "Aantal sprekers: minimaal", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        speakers.Controls.Add(_minSpeakers);
        speakers.Controls.Add(new Label { Text = "maximaal", AutoSize = true, Margin = new Padding(8, 6, 4, 0) });
        speakers.Controls.Add(_maxSpeakers);
        page.Controls.Add(speakers);
        page.Controls.Add(Hint("0 betekent: laat het model het aantal zelf bepalen."));

        page.Controls.Add(Heading("Extra bestanden"));
        page.Controls.Add(_exportSrt);
        page.Controls.Add(_exportVtt);
        return new TabPage("Uitschrijven") { Controls = { page } };
    }

    private TabPage BuildAdvancedTab()
    {
        var page = Page();
        page.Controls.Add(Hint("Deze paden vult install-prereqs.ps1 zelf in. Pas ze alleen aan als je de transcriptie-omgeving ergens anders hebt staan."));
        page.Controls.Add(Heading("Python (met WhisperX)"));
        page.Controls.Add(WithButton(_pythonExe, "Kiezen...", (_, _) => BrowseFile(_pythonExe, "python.exe|python.exe")));
        page.Controls.Add(Heading("FFmpeg"));
        page.Controls.Add(WithButton(_ffmpegPath, "Kiezen...", (_, _) => BrowseFile(_ffmpegPath, "ffmpeg.exe|ffmpeg.exe")));
        page.Controls.Add(Heading("Diagnose"));
        page.Controls.Add(_diagnostics);
        page.Controls.Add(Hint("Logbestanden van de recorder staan in " + AppRuntime.GetLogDirectory() + ". Ze blijven op deze pc."));
        return new TabPage("Geavanceerd") { Controls = { page } };
    }

    private void LoadValues()
    {
        var s = Result;
        _outputRoot.Text = s.Ui.OutputRoot;
        _minimizeToTray.Checked = s.Ui.MinimizeToTray;
        _startWithWindows.Checked = s.Ui.StartWithWindows;
        _diagnostics.Checked = s.Ui.DiagnosticsMode;

        _transcribe.Checked = s.Worker.WhisperXEnabled;
        foreach (var (code, name) in SettingsStore.Languages)
        {
            _language.Items.Add(new Choice(code, name));
        }

        foreach (var (code, name) in SettingsStore.Models)
        {
            _model.Items.Add(new Choice(code, name));
        }

        foreach (var code in new[] { "auto", "nvidia-cuda", "amd-gpu", "cpu-fallback" })
        {
            _profile.Items.Add(new Choice(code, WorkerProductionProfile.GetTranscriptionProfileDisplayName(code)));
        }

        Select(_language, s.Worker.Language);
        Select(_model, s.Worker.WhisperXModel);
        Select(_profile, s.Worker.TranscriptionProfile);
        _diarization.Checked = s.Worker.DiarizationEnabled;
        _minSpeakers.Value = s.Worker.MinSpeakers ?? 0;
        _maxSpeakers.Value = s.Worker.MaxSpeakers ?? 0;
        _exportSrt.Checked = s.Worker.ExportSrt;
        _exportVtt.Checked = s.Worker.ExportVtt;
        _pythonExe.Text = s.Worker.PythonExe;
        _ffmpegPath.Text = s.Worker.FfmpegPath;
        UpdateTokenState();
        _token.TextChanged += (_, _) => UpdateTokenState();
    }

    private void UpdateTokenState()
    {
        _tokenState.Text = !string.IsNullOrWhiteSpace(_token.Text)
            ? "Nieuw token wordt bewaard bij Opslaan."
            : _deleteToken
                ? "Het token wordt gewist bij Opslaan."
                : SettingsStore.HasToken() ? "Er is een token bewaard (niet zichtbaar). Vul een nieuw in om het te vervangen." : "Nog geen token bewaard.";
    }

    private static void Select(ComboBox combo, string code)
        => combo.SelectedItem = combo.Items.Cast<Choice>().FirstOrDefault(x => x.Code == code) ?? combo.Items.Cast<Choice>().First();

    private void Accept()
    {
        var s = Result;
        var outputRoot = _outputRoot.Text.Trim();
        try
        {
            Directory.CreateDirectory(outputRoot);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Deze opslagmap kan niet worden gebruikt: " + ex.Message, "Instellingen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(_token.Text))
        {
            var token = _token.Text.Trim();
            if (!token.StartsWith("hf_", StringComparison.Ordinal) || token.Length < 20)
            {
                MessageBox.Show(this, "Dit lijkt geen Hugging Face-token (die begint met hf_).", "Instellingen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SettingsStore.SaveToken(token);
        }
        else if (_deleteToken)
        {
            SettingsStore.DeleteToken();
        }

        s.Ui.OutputRoot = outputRoot;
        s.Ui.MinimizeToTray = _minimizeToTray.Checked;
        s.Ui.StartWithWindows = _startWithWindows.Checked;
        s.Ui.DiagnosticsMode = _diagnostics.Checked;
        s.Worker.WhisperXEnabled = _transcribe.Checked;
        s.Worker.Language = ((Choice)_language.SelectedItem!).Code;
        s.Worker.WhisperXModel = ((Choice)_model.SelectedItem!).Code;
        s.Worker.TranscriptionProfile = ((Choice)_profile.SelectedItem!).Code;
        s.Worker.DiarizationEnabled = _diarization.Checked;
        s.Worker.MinSpeakers = _minSpeakers.Value > 0 ? (int)_minSpeakers.Value : null;
        s.Worker.MaxSpeakers = _maxSpeakers.Value > 0 ? (int)_maxSpeakers.Value : null;
        s.Worker.ExportSrt = _exportSrt.Checked;
        s.Worker.ExportVtt = _exportVtt.Checked;
        s.Worker.PythonExe = _pythonExe.Text.Trim();
        s.Worker.FfmpegPath = _ffmpegPath.Text.Trim();

        WorkerSettingsChanged = !string.IsNullOrWhiteSpace(_token.Text) || _deleteToken
            || JsonSerializer.Serialize(s.Worker) != JsonSerializer.Serialize(_original.Worker)
            || !string.Equals(s.Ui.OutputRoot, _original.Ui.OutputRoot, StringComparison.OrdinalIgnoreCase);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void BrowseFolder(TextBox target)
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = target.Text, UseDescriptionForTitle = true, Description = "Kies de opslagmap voor opnames" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            target.Text = dialog.SelectedPath;
        }
    }

    private void BrowseFile(TextBox target, string filter)
    {
        using var dialog = new OpenFileDialog { Filter = filter, FileName = target.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            target.Text = dialog.FileName;
        }
    }

    private sealed record Choice(string Code, string Name)
    {
        public override string ToString() => Name;
    }
}
