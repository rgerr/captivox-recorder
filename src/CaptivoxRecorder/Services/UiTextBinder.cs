using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace LocalCaptureAgent.Services;

// Zet de teksten van een venster in de taal van de interface, ook teksten die later worden gezet of onderdelen die later
// worden toegevoegd: een vaste Nederlandse tekst (of een ingevuld sjabloon) wordt meteen vervangen door de vertaling.
// Invoervakken blijven van de gebruiker. Lijsten (keuzelijsten) tonen de vertaling, maar houden hun eigen waarden, zodat de
// code die de keuze terugleest gewoon blijft werken. Met Nederlands als taal doet dit niets.
internal static class UiTextBinder
{
    private sealed class State
    {
        public string? LastOutput;
        public bool Busy;
    }

    private static readonly ConditionalWeakTable<object, State> Bound = new();

    // Onderdelen die alleen inhoud van de gebruiker tonen (namen, titels, tekst uit een transcript): niet vertalen.
    public const string RawTextMarker = "ui-text-raw";

    public static void Attach(Control? root)
    {
        if (root is null || UiText.IsDutch) return;
        Bind(root);
    }

    public static void Attach(ToolStripItem? item)
    {
        if (item is null || UiText.IsDutch) return;
        BindItem(item);
    }

    // Vangnet voor vensters die nergens anders gekoppeld worden: elk open venster een keer (daarna kost het niets).
    public static void AttachOpenForms()
    {
        if (UiText.IsDutch) return;
        foreach (Form form in Application.OpenForms)
        {
            if (!Bound.TryGetValue(form, out _)) Bind(form);
        }
    }

    private static bool IsRaw(Control control)
        => string.Equals(control.AccessibleDescription, RawTextMarker, StringComparison.Ordinal);

    private static void Bind(Control control)
    {
        if (!Bound.TryGetValue(control, out _))
        {
            var state = new State();
            Bound.Add(control, state);
            switch (control)
            {
                case TextBoxBase textBoxBase:
                    if (textBoxBase is TextBox textBox && !string.IsNullOrEmpty(textBox.PlaceholderText))
                    {
                        textBox.PlaceholderText = UiText.T(textBox.PlaceholderText);
                    }

                    // Een alleen-lezen vak toont meestal inhoud (een transcript), soms een vaste melding ("Transcript
                    // laden..."): alleen een tekst die precies in de tabel staat, wordt vertaald.
                    if (textBoxBase.ReadOnly && !IsRaw(control))
                    {
                        ApplyControl(control, state);
                        control.TextChanged += OnControlTextChanged;
                    }

                    break;
                case NumericUpDown or DateTimePicker or WebBrowser:
                    break;
                case ListControl list:
                    // ComboBox en ListBox: alleen de weergave; SelectedItem en Items blijven de eigen waarden.
                    list.FormattingEnabled = true;
                    list.Format += OnListFormat;
                    break;
                default:
                    if (!IsRaw(control))
                    {
                        ApplyControl(control, state);
                        control.TextChanged += OnControlTextChanged;
                    }

                    break;
            }

            // Wat een schermlezer voorleest.
            if (!string.IsNullOrEmpty(control.AccessibleName)) control.AccessibleName = UiText.T(control.AccessibleName);

            if (control is ListView listView)
            {
                foreach (ColumnHeader header in listView.Columns) header.Text = UiText.T(header.Text);
                foreach (ListViewGroup group in listView.Groups) group.Header = UiText.T(group.Header);
            }

            if (control is ToolStrip strip)
            {
                foreach (ToolStripItem item in strip.Items) BindItem(item);
                strip.ItemAdded += (_, e) => { if (e.Item is { } added) BindItem(added); };
            }

            if (control.ContextMenuStrip is { } menu) Bind(menu);
            control.ContextMenuStripChanged += (_, _) =>
            {
                if (control.ContextMenuStrip is { } changed) Bind(changed);
            };
            control.ControlAdded += (_, e) =>
            {
                if (e.Control is not null) Bind(e.Control);
            };
        }

        foreach (Control child in control.Controls) Bind(child);
    }

    private static void BindItem(ToolStripItem item)
    {
        if (Bound.TryGetValue(item, out _)) return;
        var state = new State();
        Bound.Add(item, state);
        if (!string.Equals(item.AccessibleDescription, RawTextMarker, StringComparison.Ordinal))
        {
            ApplyItem(item, state);
            item.TextChanged += OnItemTextChanged;
            if (!string.IsNullOrEmpty(item.ToolTipText)) item.ToolTipText = UiText.T(item.ToolTipText);
        }

        if (item is ToolStripDropDownItem dropDown)
        {
            foreach (ToolStripItem child in dropDown.DropDownItems) BindItem(child);
            dropDown.DropDown.ItemAdded += (_, e) => { if (e.Item is { } added) BindItem(added); };
        }
    }

    private static void OnControlTextChanged(object? sender, EventArgs e)
    {
        if (sender is Control control && Bound.TryGetValue(control, out var state)) ApplyControl(control, state);
    }

    private static void OnItemTextChanged(object? sender, EventArgs e)
    {
        if (sender is ToolStripItem item && Bound.TryGetValue(item, out var state)) ApplyItem(item, state);
    }

    private static void ApplyControl(Control control, State state)
    {
        if (state.Busy) return;
        var text = control.Text;
        // Wat we zelf net zetten, niet nog eens vertalen.
        if (string.IsNullOrEmpty(text) || string.Equals(text, state.LastOutput, StringComparison.Ordinal)) return;
        // Wat de gebruiker in een vak kan zetten (een transcript), niet in stukjes vertalen: alleen een vaste melding.
        if (control is TextBoxBase textBox && !textBox.ReadOnly) return;
        var translated = control is TextBoxBase ? UiText.Exactly(text) : UiText.T(text);
        state.LastOutput = translated;
        if (string.Equals(translated, text, StringComparison.Ordinal)) return;
        state.Busy = true;
        try
        {
            control.Text = translated;
        }
        finally
        {
            state.Busy = false;
        }
    }

    private static void ApplyItem(ToolStripItem item, State state)
    {
        if (state.Busy) return;
        var text = item.Text;
        if (string.IsNullOrEmpty(text) || string.Equals(text, state.LastOutput, StringComparison.Ordinal)) return;
        var translated = UiText.T(text);
        state.LastOutput = translated;
        if (string.Equals(translated, text, StringComparison.Ordinal)) return;
        state.Busy = true;
        try
        {
            item.Text = translated;
        }
        finally
        {
            state.Busy = false;
        }
    }

    private static void OnListFormat(object? sender, ListControlConvertEventArgs e)
    {
        if (e.DesiredType != typeof(string) || e.Value is null) return;
        var text = e.Value as string ?? Convert.ToString(e.Value, System.Globalization.CultureInfo.CurrentCulture);
        if (string.IsNullOrEmpty(text)) return;
        var translated = UiText.Exactly(text);
        if (!string.Equals(translated, text, StringComparison.Ordinal)) e.Value = translated;
    }
}
