using ThixCalc.Core.Globalization;

namespace ThixCalc.Core.Functions;

/// <summary>The help of a function as the user sees it: localized name, a sentence what it does, the argument names.</summary>
public sealed record FunctionInfo(string Canonical, string Name, FunctionCategory Category, string Description, IReadOnlyList<string> Arguments, bool RepeatsLast)
{
    /// <summary>"SVERWEIS(Suchkriterium; Matrix; Spaltenindex; [Bereich_Verweis])".</summary>
    public string Signature(CalcLocale locale) =>
        $"{Name}({string.Join(locale.ListSeparator + " ", RepeatsLast ? Arguments.Append("…") : Arguments)})";

    /// <summary>The argument name for the argument at <paramref name="index"/> (repeating ones counted on).</summary>
    public string? ArgumentName(int index)
    {
        if (index < Arguments.Count)
        {
            return Arguments[index];
        }

        if (!RepeatsLast || Arguments.Count == 0)
        {
            return null;
        }

        var last = Arguments[^1].Trim('[', ']');
        var stem = last.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        return stem.Length < last.Length ? $"[{stem}{index + 1}]" : $"[{last}]";
    }
}

/// <summary>
/// The German help of the function library (Funktion einfügen, the list while typing, the argument tip): descriptions and
/// argument names of Excel's German version. German holds the functions people use most (they come first in the lists),
/// FunctionHelpTexts every other one; a function in neither gets its arguments named from its arity. UI-free.
/// </summary>
public static class FunctionHelp
{
    private static readonly Dictionary<string, (string Description, string[] Arguments, bool Repeats)> German = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SUM"] = ("Addiert alle Zahlen in einem Zellbereich.", ["Zahl1", "[Zahl2]"], true),
        ["AVERAGE"] = ("Gibt den Mittelwert der Argumente zurück.", ["Zahl1", "[Zahl2]"], true),
        ["COUNT"] = ("Zählt die Zellen eines Bereichs, die Zahlen enthalten.", ["Wert1", "[Wert2]"], true),
        ["COUNTA"] = ("Zählt die Zellen eines Bereichs, die nicht leer sind.", ["Wert1", "[Wert2]"], true),
        ["COUNTBLANK"] = ("Zählt die leeren Zellen eines Bereichs.", ["Bereich"], false),
        ["MAX"] = ("Gibt den größten Wert der Argumente zurück.", ["Zahl1", "[Zahl2]"], true),
        ["MIN"] = ("Gibt den kleinsten Wert der Argumente zurück.", ["Zahl1", "[Zahl2]"], true),
        ["IF"] = ("Prüft eine Bedingung und gibt je nach Ergebnis einen von zwei Werten zurück.", ["Wahrheitstest", "[Wert_wenn_wahr]", "[Wert_wenn_falsch]"], false),
        ["IFS"] = ("Prüft Bedingungen der Reihe nach und gibt den Wert der ersten wahren zurück.", ["Wahrheitstest1", "Wert_wenn_wahr1", "[Wahrheitstest2]", "[Wert_wenn_wahr2]"], true),
        ["IFERROR"] = ("Gibt einen anderen Wert zurück, wenn ein Ausdruck einen Fehler ergibt.", ["Wert", "Wert_falls_Fehler"], false),
        ["IFNA"] = ("Gibt einen anderen Wert zurück, wenn ein Ausdruck #NV ergibt.", ["Wert", "Wert_bei_NV"], false),
        ["AND"] = ("Gibt WAHR zurück, wenn alle Argumente WAHR sind.", ["Wahrheitswert1", "[Wahrheitswert2]"], true),
        ["OR"] = ("Gibt WAHR zurück, wenn ein Argument WAHR ist.", ["Wahrheitswert1", "[Wahrheitswert2]"], true),
        ["NOT"] = ("Kehrt einen Wahrheitswert um.", ["Wahrheitswert"], false),
        ["XOR"] = ("Gibt WAHR zurück, wenn eine ungerade Zahl der Argumente WAHR ist.", ["Wahrheitswert1", "[Wahrheitswert2]"], true),
        ["SWITCH"] = ("Vergleicht einen Ausdruck mit Werten und gibt das Ergebnis des ersten passenden zurück.", ["Ausdruck", "Wert1", "Ergebnis1", "[Standard_oder_Wert2]"], true),
        ["VLOOKUP"] = ("Sucht einen Wert in der ersten Spalte einer Matrix und gibt einen Wert derselben Zeile zurück.", ["Suchkriterium", "Matrix", "Spaltenindex", "[Bereich_Verweis]"], false),
        ["HLOOKUP"] = ("Sucht einen Wert in der ersten Zeile einer Matrix und gibt einen Wert derselben Spalte zurück.", ["Suchkriterium", "Matrix", "Zeilenindex", "[Bereich_Verweis]"], false),
        ["XLOOKUP"] = ("Durchsucht einen Bereich und gibt den passenden Eintrag eines zweiten Bereichs zurück.", ["Suchkriterium", "Suchmatrix", "Rückgabematrix", "[wenn_nicht_gefunden]", "[Vergleichsmodus]", "[Suchmodus]"], false),
        ["INDEX"] = ("Gibt den Wert an einer Position eines Bereichs zurück.", ["Matrix", "Zeile", "[Spalte]"], false),
        ["MATCH"] = ("Sucht einen Wert in einem Bereich und gibt seine Position zurück.", ["Suchkriterium", "Suchmatrix", "[Vergleichstyp]"], false),
        ["XMATCH"] = ("Gibt die Position eines Werts in einem Bereich zurück.", ["Suchkriterium", "Suchmatrix", "[Vergleichsmodus]", "[Suchmodus]"], false),
        ["LOOKUP"] = ("Sucht einen Wert in einem Vektor oder einer Matrix.", ["Suchkriterium", "Suchvektor", "[Ergebnisvektor]"], false),
        ["CHOOSE"] = ("Wählt einen Wert aus einer Liste nach seiner Nummer.", ["Index", "Wert1", "[Wert2]"], true),
        ["OFFSET"] = ("Gibt einen Bezug zurück, der gegenüber einem Bezug verschoben ist.", ["Bezug", "Zeilen", "Spalten", "[Höhe]", "[Breite]"], false),
        ["INDIRECT"] = ("Gibt den Bezug zurück, den ein Text beschreibt.", ["Bezug", "[A1]"], false),
        ["ROW"] = ("Gibt die Zeilennummer eines Bezugs zurück.", ["[Bezug]"], false),
        ["COLUMN"] = ("Gibt die Spaltennummer eines Bezugs zurück.", ["[Bezug]"], false),
        ["ROWS"] = ("Gibt die Anzahl der Zeilen eines Bezugs zurück.", ["Matrix"], false),
        ["COLUMNS"] = ("Gibt die Anzahl der Spalten eines Bezugs zurück.", ["Matrix"], false),
        ["FILTER"] = ("Filtert einen Bereich nach Bedingungen.", ["Matrix", "einschließen", "[wenn_leer]"], false),
        ["SORT"] = ("Sortiert den Inhalt eines Bereichs.", ["Matrix", "[Sortierindex]", "[Sortierreihenfolge]", "[nach_Spalte]"], false),
        ["UNIQUE"] = ("Gibt die eindeutigen Werte eines Bereichs zurück.", ["Matrix", "[nach_Spalte]", "[genau_einmal]"], false),
        ["SEQUENCE"] = ("Erzeugt eine Folge von Zahlen.", ["Zeilen", "[Spalten]", "[Anfang]", "[Schritt]"], false),
        ["SUMIF"] = ("Addiert die Zellen, die eine Bedingung erfüllen.", ["Bereich", "Suchkriterien", "[Summe_Bereich]"], false),
        ["SUMIFS"] = ("Addiert die Zellen, die mehrere Bedingungen erfüllen.", ["Summe_Bereich", "Kriterien_Bereich1", "Kriterien1", "[Kriterien_Bereich2]", "[Kriterien2]"], true),
        ["COUNTIF"] = ("Zählt die Zellen, die eine Bedingung erfüllen.", ["Bereich", "Suchkriterien"], false),
        ["COUNTIFS"] = ("Zählt die Zellen, die mehrere Bedingungen erfüllen.", ["Kriterien_Bereich1", "Kriterien1", "[Kriterien_Bereich2]", "[Kriterien2]"], true),
        ["AVERAGEIF"] = ("Gibt den Mittelwert der Zellen zurück, die eine Bedingung erfüllen.", ["Bereich", "Kriterien", "[Mittelwert_Bereich]"], false),
        ["AVERAGEIFS"] = ("Gibt den Mittelwert der Zellen zurück, die mehrere Bedingungen erfüllen.", ["Mittelwert_Bereich", "Kriterien_Bereich1", "Kriterien1", "[Kriterien_Bereich2]", "[Kriterien2]"], true),
        ["MAXIFS"] = ("Gibt den größten Wert der Zellen zurück, die Bedingungen erfüllen.", ["Max_Bereich", "Kriterien_Bereich1", "Kriterien1"], true),
        ["MINIFS"] = ("Gibt den kleinsten Wert der Zellen zurück, die Bedingungen erfüllen.", ["Min_Bereich", "Kriterien_Bereich1", "Kriterien1"], true),
        ["SUMPRODUCT"] = ("Multipliziert die Bereiche elementweise und addiert die Produkte.", ["Matrix1", "[Matrix2]"], true),
        ["SUBTOTAL"] = ("Gibt ein Teilergebnis einer Liste zurück (ausgeblendete Zeilen wahlweise ohne).", ["Funktion", "Bezug1", "[Bezug2]"], true),
        ["ROUND"] = ("Rundet eine Zahl auf eine Anzahl Stellen.", ["Zahl", "Anzahl_Stellen"], false),
        ["ROUNDUP"] = ("Rundet eine Zahl von null weg.", ["Zahl", "Anzahl_Stellen"], false),
        ["ROUNDDOWN"] = ("Rundet eine Zahl zu null hin.", ["Zahl", "Anzahl_Stellen"], false),
        ["INT"] = ("Rundet eine Zahl auf die nächstkleinere ganze Zahl ab.", ["Zahl"], false),
        ["TRUNC"] = ("Schneidet die Nachkommastellen ab.", ["Zahl", "[Anzahl_Stellen]"], false),
        ["ABS"] = ("Gibt den Betrag einer Zahl zurück.", ["Zahl"], false),
        ["MOD"] = ("Gibt den Rest einer Division zurück.", ["Zahl", "Divisor"], false),
        ["POWER"] = ("Gibt eine Zahl hoch einer Potenz zurück.", ["Zahl", "Potenz"], false),
        ["SQRT"] = ("Gibt die Quadratwurzel einer Zahl zurück.", ["Zahl"], false),
        ["PRODUCT"] = ("Multipliziert die Argumente.", ["Zahl1", "[Zahl2]"], true),
        ["RAND"] = ("Gibt eine Zufallszahl zwischen 0 und 1 zurück.", [], false),
        ["RANDBETWEEN"] = ("Gibt eine ganze Zufallszahl zwischen zwei Grenzen zurück.", ["Untere_Zahl", "Obere_Zahl"], false),
        ["PI"] = ("Gibt die Zahl Pi zurück.", [], false),
        ["MEDIAN"] = ("Gibt den Median der Zahlen zurück.", ["Zahl1", "[Zahl2]"], true),
        ["MODE.SNGL"] = ("Gibt den häufigsten Wert zurück.", ["Zahl1", "[Zahl2]"], true),
        ["STDEV.S"] = ("Schätzt die Standardabweichung einer Stichprobe.", ["Zahl1", "[Zahl2]"], true),
        ["STDEV.P"] = ("Berechnet die Standardabweichung der Grundgesamtheit.", ["Zahl1", "[Zahl2]"], true),
        ["VAR.S"] = ("Schätzt die Varianz einer Stichprobe.", ["Zahl1", "[Zahl2]"], true),
        ["LARGE"] = ("Gibt den k-größten Wert zurück.", ["Matrix", "k"], false),
        ["SMALL"] = ("Gibt den k-kleinsten Wert zurück.", ["Matrix", "k"], false),
        ["RANK.EQ"] = ("Gibt den Rang einer Zahl in einer Liste zurück.", ["Zahl", "Bezug", "[Reihenfolge]"], false),
        ["CONCAT"] = ("Verbindet Texte.", ["Text1", "[Text2]"], true),
        ["CONCATENATE"] = ("Verbindet Texte.", ["Text1", "[Text2]"], true),
        ["TEXTJOIN"] = ("Verbindet Texte mit einem Trennzeichen.", ["Trennzeichen", "Leer_ignorieren", "Text1", "[Text2]"], true),
        ["LEFT"] = ("Gibt die ersten Zeichen eines Texts zurück.", ["Text", "[Anzahl_Zeichen]"], false),
        ["RIGHT"] = ("Gibt die letzten Zeichen eines Texts zurück.", ["Text", "[Anzahl_Zeichen]"], false),
        ["MID"] = ("Gibt Zeichen aus der Mitte eines Texts zurück.", ["Text", "Erstes_Zeichen", "Anzahl_Zeichen"], false),
        ["LEN"] = ("Gibt die Anzahl der Zeichen eines Texts zurück.", ["Text"], false),
        ["FIND"] = ("Sucht einen Text in einem anderen (Groß-/Kleinschreibung beachtet).", ["Suchtext", "Text", "[Erstes_Zeichen]"], false),
        ["SEARCH"] = ("Sucht einen Text in einem anderen.", ["Suchtext", "Text", "[Erstes_Zeichen]"], false),
        ["SUBSTITUTE"] = ("Ersetzt Text in einem Text.", ["Text", "Alter_Text", "Neuer_Text", "[ntes_Auftreten]"], false),
        ["REPLACE"] = ("Ersetzt Zeichen an einer Stelle eines Texts.", ["Alter_Text", "Erstes_Zeichen", "Anzahl_Zeichen", "Neuer_Text"], false),
        ["TRIM"] = ("Entfernt überflüssige Leerzeichen.", ["Text"], false),
        ["UPPER"] = ("Wandelt Text in Großbuchstaben um.", ["Text"], false),
        ["LOWER"] = ("Wandelt Text in Kleinbuchstaben um.", ["Text"], false),
        ["PROPER"] = ("Schreibt jedes Wort mit großem Anfangsbuchstaben.", ["Text"], false),
        ["TEXT"] = ("Formatiert eine Zahl als Text nach einem Format.", ["Wert", "Textformat"], false),
        ["VALUE"] = ("Wandelt einen Text in eine Zahl um.", ["Text"], false),
        ["TODAY"] = ("Gibt das heutige Datum zurück.", [], false),
        ["NOW"] = ("Gibt Datum und Uhrzeit von jetzt zurück.", [], false),
        ["DATE"] = ("Gibt das Datum aus Jahr, Monat und Tag zurück.", ["Jahr", "Monat", "Tag"], false),
        ["YEAR"] = ("Gibt das Jahr eines Datums zurück.", ["Zahl"], false),
        ["MONTH"] = ("Gibt den Monat eines Datums zurück.", ["Zahl"], false),
        ["DAY"] = ("Gibt den Tag eines Datums zurück.", ["Zahl"], false),
        ["WEEKDAY"] = ("Gibt den Wochentag eines Datums zurück.", ["Zahl", "[Typ]"], false),
        ["WEEKNUM"] = ("Gibt die Kalenderwoche eines Datums zurück.", ["Fortlaufende_Zahl", "[Zahl_Typ]"], false),
        ["ISOWEEKNUM"] = ("Gibt die ISO-Kalenderwoche eines Datums zurück.", ["Datum"], false),
        ["EDATE"] = ("Gibt das Datum Monate vor oder nach einem Datum zurück.", ["Ausgangsdatum", "Monate"], false),
        ["EOMONTH"] = ("Gibt den letzten Tag des Monats vor oder nach einem Datum zurück.", ["Ausgangsdatum", "Monate"], false),
        ["DATEDIF"] = ("Berechnet den Abstand zweier Daten in Tagen, Monaten oder Jahren.", ["Ausgangsdatum", "Enddatum", "Einheit"], false),
        ["NETWORKDAYS"] = ("Gibt die Anzahl der Arbeitstage zwischen zwei Daten zurück.", ["Ausgangsdatum", "Enddatum", "[Freie_Tage]"], false),
        ["WORKDAY"] = ("Gibt das Datum nach einer Anzahl Arbeitstage zurück.", ["Ausgangsdatum", "Tage", "[Freie_Tage]"], false),
        ["TIME"] = ("Gibt die Uhrzeit aus Stunde, Minute und Sekunde zurück.", ["Stunde", "Minute", "Sekunde"], false),
        ["HOUR"] = ("Gibt die Stunde einer Uhrzeit zurück.", ["Zahl"], false),
        ["MINUTE"] = ("Gibt die Minute einer Uhrzeit zurück.", ["Zahl"], false),
        ["ISBLANK"] = ("Prüft, ob eine Zelle leer ist.", ["Wert"], false),
        ["ISNUMBER"] = ("Prüft, ob ein Wert eine Zahl ist.", ["Wert"], false),
        ["ISTEXT"] = ("Prüft, ob ein Wert ein Text ist.", ["Wert"], false),
        ["ISERROR"] = ("Prüft, ob ein Wert ein Fehler ist.", ["Wert"], false),
        ["ISNA"] = ("Prüft, ob ein Wert #NV ist.", ["Wert"], false),
        ["NA"] = ("Gibt den Fehlerwert #NV zurück.", [], false),
        ["PMT"] = ("Berechnet die Rate eines Darlehens bei konstanten Zahlungen und konstantem Zins.", ["Zins", "Zzr", "Bw", "[Zw]", "[F]"], false),
        ["FV"] = ("Gibt den Endwert einer Investition zurück.", ["Zins", "Zzr", "Rmz", "[Bw]", "[F]"], false),
        ["PV"] = ("Gibt den Barwert einer Investition zurück.", ["Zins", "Zzr", "Rmz", "[Zw]", "[F]"], false),
        ["NPV"] = ("Gibt den Kapitalwert einer Investition zurück.", ["Zins", "Wert1", "[Wert2]"], true),
        ["IRR"] = ("Gibt den internen Zinsfuß zurück.", ["Werte", "[Schätzwert]"], false),
        ["RATE"] = ("Gibt den Zinssatz einer Annuität pro Periode zurück.", ["Zzr", "Rmz", "Bw", "[Zw]", "[F]", "[Schätzwert]"], false),
        ["NPER"] = ("Gibt die Anzahl der Zahlungsperioden zurück.", ["Zins", "Rmz", "Bw", "[Zw]", "[F]"], false),
        ["TRANSPOSE"] = ("Vertauscht Zeilen und Spalten eines Bereichs.", ["Matrix"], false),
        ["HYPERLINK"] = ("Erstellt einen Link.", ["Hyperlink_Adresse", "[Freundlicher_Name]"], false),
        ["LET"] = ("Gibt Zwischenergebnissen Namen.", ["Name1", "Name_Wert1", "Berechnung_oder_Name2"], true),
    };

    /// <summary>The German names of the categories, in the order of Excel's dialog.</summary>
    public static IReadOnlyList<(FunctionCategory Category, string Name)> Categories { get; } =
    [
        (FunctionCategory.Financial, "Finanzmathematik"),
        (FunctionCategory.DateTime, "Datum & Uhrzeit"),
        (FunctionCategory.Math, "Math. & Trigonom."),
        (FunctionCategory.Statistical, "Statistik"),
        (FunctionCategory.Lookup, "Nachschlagen und Verweisen"),
        (FunctionCategory.Database, "Datenbank"),
        (FunctionCategory.Text, "Text"),
        (FunctionCategory.Logical, "Logisch"),
        (FunctionCategory.Information, "Information"),
        (FunctionCategory.Engineering, "Konstruktion"),
        (FunctionCategory.Cube, "Cube"),
        (FunctionCategory.Compatibility, "Kompatibilität"),
        (FunctionCategory.Web, "Web"),
    ];

    public static string CategoryName(FunctionCategory category) => Categories.FirstOrDefault(entry => entry.Category == category).Name ?? category.ToString();

    /// <summary>The help of one function (by canonical or localized name), or null when there is no such function.</summary>
    public static FunctionInfo? Find(string name, CalcLocale locale)
    {
        var canonical = locale.CanonicalFunctionName(name);
        return FunctionRegistry.Find(canonical) is { } definition ? InfoOf(definition, locale) : null;
    }

    /// <summary>Every function, sorted by its localized name.</summary>
    public static IReadOnlyList<FunctionInfo> All(CalcLocale locale) =>
        FunctionRegistry.All
            .Where(definition => !definition.Name.StartsWith('_') && definition.Name != "ANCHORARRAY")
            .Select(definition => InfoOf(definition, locale))
            .OrderBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>The functions whose localized name starts with <paramref name="prefix"/> (the list while typing), the common ones first.</summary>
    public static IReadOnlyList<FunctionInfo> StartingWith(string prefix, CalcLocale locale, int limit = 12) =>
        All(locale)
            .Where(info => info.Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(info => German.ContainsKey(info.Canonical) ? 0 : 1)
            .ThenBy(info => info.Name.Length)
            .ThenBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(limit)
            .ToList();

    /// <summary>Search of the Funktion-einfügen dialog: the name (German or English) or the description contains every word.</summary>
    public static IReadOnlyList<FunctionInfo> Search(string words, CalcLocale locale)
    {
        var parts = words.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return All(locale)
            .Where(info => parts.All(part => info.Name.Contains(part, StringComparison.CurrentCultureIgnoreCase)
                || info.Canonical.Contains(part, StringComparison.OrdinalIgnoreCase)
                || info.Description.Contains(part, StringComparison.CurrentCultureIgnoreCase)))
            .OrderBy(info => info.Name.Equals(words, StringComparison.CurrentCultureIgnoreCase) || info.Canonical.Equals(words, StringComparison.OrdinalIgnoreCase) ? 0
                : info.Name.StartsWith(words, StringComparison.CurrentCultureIgnoreCase) ? 1
                : info.Name.Contains(words, StringComparison.CurrentCultureIgnoreCase) ? 2 : 3)
            .ThenBy(info => German.ContainsKey(info.Canonical) ? 0 : 1)
            .ThenBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static FunctionInfo InfoOf(FunctionDefinition definition, CalcLocale locale)
    {
        var name = locale.FunctionName(definition.Name);
        if (German.TryGetValue(definition.Name, out var help) || FunctionHelpTexts.German.TryGetValue(definition.Name, out help))
        {
            return new FunctionInfo(definition.Name, name, definition.Category, help.Description, help.Arguments, help.Repeats);
        }

        // Named from the arity: Zahl1; Zahl2 … for the aggregates, Argument1 … otherwise.
        var repeats = definition.MaxArguments > 30;
        var count = repeats ? Math.Max(1, definition.MinArguments) + 1 : definition.MaxArguments;
        var arguments = Enumerable.Range(0, count).Select(index =>
        {
            var label = definition.KindOf(index) == ArgumentKind.Any ? $"Wert{index + 1}" : $"Argument{index + 1}";
            return index < definition.MinArguments ? label : $"[{label}]";
        }).ToList();
        return new FunctionInfo(definition.Name, name, definition.Category, $"Funktion der Kategorie {CategoryName(definition.Category)}.", arguments, repeats);
    }
}
