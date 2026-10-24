using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LocalCaptureAgent.Services;

// De taal van de interface: Engels (standaard), Nederlands, Duits of Frans. Dit staat los van de taal van de opnames
// (Ui.LanguageProfile, de transcriptietaal). De teksten in de code zijn Nederlands; de vertalingen staan per onderwerp in
// de tabellen UiText.*.cs (een regel per tekst: Nederlands, Engels, Duits, Frans). Een tekst met {0}, {1}, ... is een
// sjabloon: UiText.F vult hem in, en UiTextBinder herkent een al ingevulde Nederlandse tekst aan het sjabloon.
// De taal wordt bij het starten gekozen; een andere taal geldt vanaf de volgende start.
internal static partial class UiText
{
    public const string DefaultLanguage = "en";
    public static readonly IReadOnlyList<string> SupportedLanguages = new[] { "en", "nl", "de", "fr" };

    // Vertalingen per Nederlandse brontekst: [Engels, Duits, Frans].
    private static readonly Dictionary<string, string[]> Exact = new(StringComparer.Ordinal);
    private static readonly List<(Regex Pattern, string[] Targets, int Literal)> Templates = new();
    private static readonly object TableLock = new();
    private static volatile bool _tablesLoaded;

    // Wat al eens is opgezocht (ook wat niet te vertalen bleek); begrensd, want sommige teksten veranderen elke seconde.
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    private static int _targetIndex = 0;
    private static string? _auditPath;
    private static readonly ConcurrentDictionary<string, byte> AuditSeen = new(StringComparer.Ordinal);

    public static string Language { get; private set; } = DefaultLanguage;

    // Datums en tijden volgen de taal van de interface (24-uursklok, ook in het Engels).
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-GB");

    public static bool IsDutch => Language == "nl";

    // Datumnotaties per taal ("zaterdag 24 oktober", "Saturday 24 October", "Samstag, 24. Oktober", "samedi 24 octobre").
    public static string DayMonthFormat => Language == "de" ? "dddd, d. MMMM" : "dddd d MMMM";
    public static string ShortDayMonthFormat => Language == "de" ? "ddd, d. MMM" : "ddd d MMM";
    public static string DayMonthYearFormat => Language == "de" ? "d. MMM yyyy" : "d MMM yyyy";

    public static string Normalize(string? language)
    {
        var value = (language ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length > 2 && (value[2] == '-' || value[2] == '_')) value = value[..2];
        return SupportedLanguages.Contains(value) ? value : DefaultLanguage;
    }

    public static bool IsSupported(string? language)
        => SupportedLanguages.Contains((language ?? string.Empty).Trim().ToLowerInvariant());

    // Elke taal in haar eigen naam, zodat iedereen zijn taal terugvindt.
    public static string DisplayName(string language) => Normalize(language) switch
    {
        "nl" => "Nederlands",
        "de" => "Deutsch",
        "fr" => "Fran\u00e7ais",
        _ => "English",
    };

    public static void SetLanguage(string? language)
    {
        Language = Normalize(language);
        _targetIndex = Language switch { "de" => 1, "fr" => 2, _ => 0 };
        Culture = CultureInfo.GetCultureInfo(Language switch { "nl" => "nl-NL", "de" => "de-DE", "fr" => "fr-FR", _ => "en-GB" });
        Cache.Clear();
        // Alleen voor het nakijken van de vertalingen: teksten zonder vertaling naar dit bestand.
        var audit = Environment.GetEnvironmentVariable("CAPTIVOX_UI_TEXT_AUDIT");
        _auditPath = string.IsNullOrWhiteSpace(audit) ? null : audit.Trim();
    }

    // Een vaste tekst in de taal van de interface.
    public static string T(string? text) => Translate(text, TranslateMode.Full);

    // Een sjabloon met {0}, {1}, ... in de taal van de interface, ingevuld met de cultuur van de interface.
    public static string F(string format, params object?[] args)
    {
        var template = IsDutch ? format : LookupExact(format) ?? format;
        if (!IsDutch && ReferenceEquals(template, format)) RecordMissing(format);
        try
        {
            return string.Format(Culture, template, args);
        }
        catch (FormatException)
        {
            return string.Format(Culture, format, args);
        }
    }

    // Alleen een tekst die precies in de tabel staat (voor lijsten met apparaten en andere namen).
    public static string Exactly(string? text) => Translate(text, TranslateMode.ExactOnly);

    public enum TranslateMode
    {
        Full,
        ExactOnly,
    }

    public static string Translate(string? text, TranslateMode mode)
    {
        if (string.IsNullOrEmpty(text) || IsDutch) return text ?? string.Empty;
        if (!HasLetter(text)) return text;
        EnsureTables();
        var key = mode == TranslateMode.Full ? text : "\u0001" + text;
        if (Cache.TryGetValue(key, out var cached)) return cached;

        string result;
        if (mode == TranslateMode.ExactOnly)
        {
            result = LookupExact(text) ?? LookupTrimmed(text) ?? text;
        }
        else
        {
            result = TranslateLine(text) ?? TranslateLines(text) ?? text;
            if (ReferenceEquals(result, text)) RecordMissing(text);
        }

        if (Cache.Count > 4000) Cache.Clear();
        Cache[key] = result;
        return result;
    }

    private static bool HasLetter(string text)
    {
        foreach (var c in text)
        {
            if (char.IsLetter(c)) return true;
        }

        return false;
    }

    private static string? LookupExact(string text)
    {
        EnsureTables();
        return Exact.TryGetValue(text, out var targets) ? targets[_targetIndex] : null;
    }

    // Spaties of een dubbele punt aan het eind horen niet bij de vertaling ("Map: " en "Map").
    private static string? LookupTrimmed(string text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && char.IsWhiteSpace(text[start])) start++;
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
        if (end <= start) return null;
        var lead = text[..start];
        var trail = text[end..];
        var core = text[start..end];
        if (lead.Length > 0 || trail.Length > 0)
        {
            if (LookupExact(core) is { } whole) return lead + whole + trail;
        }

        // Een leesteken aan het eind dat niet in de tabel staat ("licht." of "geen onderbroken opnames;").
        if (core.Length > 1 && core[^1] is ':' or ';' or '.' && LookupExact(core[..^1].TrimEnd()) is { } label)
        {
            var mark = core[^1];
            var spaced = Language == "fr" && mark is ':' or ';';
            return lead + label.TrimEnd('.') + (spaced ? " " : string.Empty) + mark + trail;
        }

        return null;
    }

    // Een regel: precies, zonder randen, als ingevuld sjabloon, of zin voor zin.
    private static string? TranslateLine(string text)
    {
        var direct = LookupExact(text) ?? LookupTrimmed(text) ?? MatchTemplate(text);
        if (direct is not null) return direct;
        return TranslateSentences(text) ?? TranslateSegments(text);
    }

    private static readonly Regex SegmentSplit = new(@"\s+[·|]\s+", RegexOptions.CultureInvariant);

    // Delen met " · " of " | " ertussen (zoals "GPU gevonden: ... · Systeemgeheugen: 16 GB"): elk deel apart; wat niet
    // te vertalen is (een naam, een getal), blijft staan.
    private static string? TranslateSegments(string text)
    {
        var parts = SegmentSplit.Split(text);
        if (parts.Length < 2) return null;
        var separators = SegmentSplit.Matches(text);
        var builder = new StringBuilder();
        var changed = false;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var translated = LookupExact(part) ?? LookupTrimmed(part) ?? MatchTemplate(part) ?? TranslateSentences(part);
            if (translated is not null) changed = true;
            builder.Append(translated ?? part);
            if (i < separators.Count) builder.Append(separators[i].Value);
        }

        return changed ? builder.ToString() : null;
    }

    private static string? MatchTemplate(string text)
    {
        foreach (var (pattern, targets, _) in Templates)
        {
            var match = pattern.Match(text);
            if (!match.Success) continue;
            var args = new object[match.Groups.Count - 1];
            for (var i = 1; i < match.Groups.Count; i++)
            {
                var value = match.Groups["a" + (i - 1).ToString(CultureInfo.InvariantCulture)].Value;
                // Een ingevuld deel kan zelf een vaste tekst zijn ("Status: Klaar").
                args[i - 1] = LookupExact(value) ?? value;
            }

            try
            {
                return string.Format(CultureInfo.InvariantCulture, targets[_targetIndex], args);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        return null;
    }

    private static readonly Regex SentenceSplit = new(@"(?<=[.!?:;])\s+(?=\S)", RegexOptions.CultureInvariant);

    // Losse zinnen die achter elkaar zijn geplakt ("Opname bewaard. Captivox schrijft het overleg nu uit.", of een vaste zin
    // met een foutmelding van Windows erachter): elke zin apart. Wat niet in de tabel staat (een foutmelding, een pad),
    // blijft zoals het is; alleen als er niets te vertalen viel, blijft het geheel staan.
    private static string? TranslateSentences(string text)
    {
        var parts = SentenceSplit.Split(text);
        if (parts.Length < 2) return null;
        var separators = SentenceSplit.Matches(text);
        var builder = new StringBuilder();
        var changed = false;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var translated = LookupExact(part) ?? LookupTrimmed(part) ?? MatchTemplate(part);
            if (translated is not null) changed = true;
            else if (LooksLikeWords(part)) RecordMissing(part);
            builder.Append(translated ?? part);
            if (i < separators.Count) builder.Append(separators[i].Value);
        }

        return changed ? builder.ToString() : null;
    }

    // Meerdere regels: elke regel apart. Alleen als er minstens een regel vertaald is.
    private static string? TranslateLines(string text)
    {
        if (text.IndexOf('\n') < 0) return null;
        var lines = text.Split('\n');
        var changed = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var carriage = line.EndsWith('\r');
            var core = carriage ? line[..^1] : line;
            if (core.Trim().Length == 0 || !HasLetter(core)) continue;
            var translated = TranslateLine(core);
            if (translated is null)
            {
                RecordMissing(core);
                continue;
            }

            lines[i] = carriage ? translated + "\r" : translated;
            changed = true;
        }

        return changed ? string.Join("\n", lines) : null;
    }

    // Woorden (geen getal, pad, tijd of losse naam zonder spatie).
    private static bool LooksLikeWords(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length < 2 || !HasLetter(trimmed)) return false;
        if (trimmed.Contains('\\') || trimmed.Contains('/') || trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
        return trimmed.Contains(' ');
    }

    private static void RecordMissing(string text)
    {
        if (_auditPath is null) return;
        var trimmed = text.Trim();
        if (trimmed.Length < 2 || !HasLetter(trimmed) || trimmed.Contains('\\') || !AuditSeen.TryAdd(text, 0)) return;
        try
        {
            File.AppendAllText(_auditPath, text.Replace("\r", "\\r").Replace("\n", "\\n") + Environment.NewLine, Encoding.UTF8);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void EnsureTables()
    {
        if (_tablesLoaded) return;
        lock (TableLock)
        {
            if (_tablesLoaded) return;
            AddShellTexts();
            AddSettingsTexts();
            AddClassicTexts();
            AddServiceTexts();
            AddProOfferTexts();
            AddProTexts();
            // Het meest specifieke sjabloon eerst ("Captivox: opname loopt ({0} min)" voor "{0} min").
            Templates.Sort((a, b) => b.Literal.CompareTo(a.Literal));
            _tablesLoaded = true;
        }
    }

    // Een tekst: Nederlands (bron), Engels, Duits, Frans. Met {0}, {1}, ... ook een sjabloon.
    private static void Add(string dutch, string english, string german, string french)
    {
        var targets = new[] { english, german, french };
        Exact[dutch] = targets;
        if (dutch.Contains('{') && PlaceholderPattern.IsMatch(dutch))
        {
            // Ook herkennen in een al ingevulde tekst, behalve heel algemene sjablonen ("{0} min", "{0} uur"): die alleen via F,
            // anders zou elke tekst die op " min" eindigt worden vertaald.
            var literal = PlaceholderPattern.Replace(dutch, string.Empty).Length;
            if (literal >= MinimumTemplateLiteral) Templates.Add((BuildTemplatePattern(dutch), targets, literal));
        }
    }

    private const int MinimumTemplateLiteral = 8;

    private static readonly Regex PlaceholderPattern = new(@"\{(\d+)(?::[^}]*)?\}", RegexOptions.CultureInvariant);

    private static Regex BuildTemplatePattern(string template)
    {
        var builder = new StringBuilder("^");
        var last = 0;
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in PlaceholderPattern.Matches(template))
        {
            builder.Append(Regex.Escape(template[last..match.Index]));
            var name = "a" + match.Groups[1].Value;
            builder.Append(used.Add(name) ? "(?<" + name + ">.+?)" : @"\k<" + name + ">");
            last = match.Index + match.Length;
        }

        builder.Append(Regex.Escape(template[last..]));
        builder.Append('$');
        return new Regex(builder.ToString(), RegexOptions.CultureInvariant | RegexOptions.Singleline);
    }

    // De tabellen: elk in een eigen bestand. De Pro-tabellen bestaan alleen in Pro.
    static partial void AddProOfferTexts();
    static partial void AddProTexts();
}
