using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixContacts.Core;
using ThixContacts.Editor;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Pim.Model;
using ThixOffice.Pim.Rules;

namespace ThixContacts.Dialogs;

/// <summary>"Kategorisieren": a category to give or take away – one of those there are (with its colour) or a new one.</summary>
public sealed class CategoryDialog : OfficeDialog
{
    public CategoryDialog(IReadOnlyList<string> categories, bool remove)
        : base(remove ? "Kategorie entfernen" : "Kategorisieren", 420)
    {
        Remove = remove;
        Choice = new AutoCompleteBox { ItemsSource = categories, FilterMode = AutoCompleteFilterMode.ContainsOrdinal, Watermark = "Name der Kategorie", MinimumPrefixLength = 0 };
        AutomationProperties.SetName(Choice, "Kategorie");
        var list = new WrapPanel();
        foreach (var category in categories)
        {
            var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
            dot.Use(Border.BackgroundProperty, CategoryColors.TokenOf(category));
            var button = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { dot, new TextBlock { Text = category } } }, Margin = new Thickness(0, 0, 6, 6) };
            button.Click += (_, _) => Choice.Text = category;
            list.Children.Add(button);
        }

        var form = new MatFormField { Label = "Kategorie", Content = Choice };
        FormErrors.SetField(form, "Category");
        OkButton.Content = new IconLabel { Icon = remove ? "x" : "tag", Text = remove ? "Entfernen" : "Zuweisen" };
        SetBody(new StackPanel { Spacing = 10, Children = { form, list } });
        Opened += (_, _) => Choice.Focus();
    }

    public bool Remove { get; }

    public AutoCompleteBox Choice { get; }

    public string Category => (Choice.Text ?? "").Trim();

    protected override bool Apply()
    {
        if (PersonRules.CategoryProblem(Category) is { } problem)
        {
            FormErrors.ShowError(this, "Category", problem);
            return false;
        }

        return true;
    }
}

/// <summary>"Mitglieder hinzufügen": the contacts that are not in the group, searchable, several can be chosen.</summary>
public sealed class MembersDialog : OfficeDialog
{
    private readonly IReadOnlyList<Person> _candidates;

    public MembersDialog(ContactsSession session, PersonGroup group)
        : base($"Mitglieder hinzufügen – {group.Name}", 460)
    {
        var members = group.Data.Members.ToHashSet();
        _candidates = session.Query(new ContactQuery()).Where(p => !members.Contains(p.Id)).ToList();
        Search = new TextBox { Watermark = "Kontakte durchsuchen" };
        AutomationProperties.SetName(Search, "Kontakte durchsuchen");
        List = new ListBox
        {
            SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle,
            Height = 320,
            ItemTemplate = new FuncDataTemplate<Person>((p, _) => new TextBlock { Text = p is null ? "" : p.Name + (p.Email.Length > 0 ? " · " + p.Email : "") }),
            ItemsSource = _candidates,
        };
        AutomationProperties.SetName(List, "Kontakte");
        Search.TextChanged += (_, _) => List.ItemsSource = _candidates.Where(p => PersonRules.Matches(p.Data, Search.Text)).ToList();
        OkButton.Content = new IconLabel { Icon = "plus", Text = "Hinzufügen" };
        SetBody(new StackPanel { Spacing = 8, Children = { Search, List } });
        Opened += (_, _) => Search.Focus();
    }

    public TextBox Search { get; }

    public ListBox List { get; }

    public IReadOnlyList<string> Chosen => List.SelectedItems?.OfType<Person>().Select(p => p.Id).ToList() ?? [];

    protected override bool Apply() => Chosen.Count > 0;
}

/// <summary>
/// "Dubletten": the sets of contacts that look like the same person (the same name, e-mail address or phone number); "Zusammenführen …" opens the
/// merge of the chosen set.
/// </summary>
public sealed class DuplicatesDialog : OfficeDialog
{
    public DuplicatesDialog(IReadOnlyList<IReadOnlyList<Person>> sets)
        : base("Dubletten", 560)
    {
        Sets = sets;
        List = new ListBox
        {
            Height = 340,
            ItemsSource = sets,
            ItemTemplate = new FuncDataTemplate<IReadOnlyList<Person>>((set, _) => new StackPanel
            {
                Margin = new Thickness(0, 4),
                Children =
                {
                    new TextBlock { Text = set is null ? "" : string.Join("  ·  ", set.Select(p => p.Name)), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = set is null ? "" : string.Join("  ·  ", set.Select(p => p.Email.Length > 0 ? p.Email : p.Phone)), TextTrimming = TextTrimming.CharacterEllipsis }.Use(TextBlock.ForegroundProperty, "Mat.TextMuted"),
                },
            }),
            SelectedIndex = sets.Count > 0 ? 0 : -1,
        };
        AutomationProperties.SetName(List, "Dubletten");
        var empty = new TextBlock { Text = "Keine Dubletten – jeder Kontakt kommt nur einmal vor.", IsVisible = sets.Count == 0 };
        OkButton.Content = new IconLabel { Icon = "layers", Text = "Zusammenführen …" };
        OkButton.IsEnabled = sets.Count > 0;
        CancelButton.Content = new IconLabel { Icon = "x", Text = "Schließen" };
        SetBody(new StackPanel { Spacing = 8, Children = { new TextBlock { Text = sets.Count == 1 ? "1 Gruppe von Dubletten." : $"{sets.Count} Gruppen von Dubletten." }, empty, List } });
    }

    public IReadOnlyList<IReadOnlyList<Person>> Sets { get; }

    public ListBox List { get; }

    public IReadOnlyList<Person>? Chosen => List.SelectedItem as IReadOnlyList<Person>;

    protected override bool Apply() => Chosen is not null;
}

/// <summary>
/// "Zusammenführen": the contacts side by side, field by field – per field the person picks whose value stays (by default the first that has one);
/// "Behalten" picks the contact that is kept (with its groups). E-mail addresses, numbers, addresses, web addresses, categories and notes are joined.
/// </summary>
public sealed class MergeDialog : OfficeDialog
{
    private readonly IReadOnlyList<Person> _people;
    private readonly List<RadioButton> _keep = [];
    private readonly Dictionary<string, List<RadioButton>> _fields = [];

    public MergeDialog(IReadOnlyList<Person> people)
        : base("Kontakte zusammenführen", Math.Min(900, 220 + people.Count * 220))
    {
        _people = people;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150," + string.Join(",", people.Select(_ => "*"))) };
        var row = 0;
        void AddRow(string label, IReadOnlyList<Control?> cells)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var title = new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 8, 6), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(title, row);
            grid.Children.Add(title);
            for (var i = 0; i < cells.Count; i++)
            {
                var cell = cells[i] ?? new TextBlock { Text = "–", VerticalAlignment = VerticalAlignment.Center }.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
                cell.Margin = new Thickness(0, 6, 8, 6);
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, i + 1);
                grid.Children.Add(cell);
            }

            row++;
        }

        AddRow("", people.Select(p => (Control?)new TextBlock { Text = p.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }).ToList());
        AddRow("Behalten", people.Select((p, i) =>
        {
            var radio = new RadioButton { GroupName = "keep", Content = "Diesen", IsChecked = i == 0 };
            _keep.Add(radio);
            return (Control?)radio;
        }).ToList());
        foreach (var field in PersonDuplicates.Fields)
        {
            var values = people.Select(p => PersonDuplicates.ValueOf(p.Data, field.Id)).ToList();
            if (values.All(v => v.Length == 0))
            {
                continue;
            }

            var first = values.FindIndex(v => v.Length > 0);
            var radios = new List<RadioButton>();
            _fields[field.Id] = radios;
            AddRow(field.Label, values.Select((value, i) =>
            {
                if (value.Length == 0)
                {
                    radios.Add(new RadioButton());
                    return (Control?)null;
                }

                var radio = new RadioButton
                {
                    GroupName = "field-" + field.Id,
                    Content = new TextBlock { Text = field.Id is "birthday" or "anniversary" ? ThixOffice.Pim.Desktop.PersonCard.DateText(value) : value, TextWrapping = TextWrapping.Wrap },
                    IsChecked = i == first,
                };
                radios.Add(radio);
                return radio;
            }).ToList());
        }

        var note = new TextBlock { Text = "E-Mail-Adressen, Telefonnummern, Adressen, Webadressen, Kategorien und Notizen aller Kontakte bleiben (jeder Wert einmal).", TextWrapping = TextWrapping.Wrap };
        note.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        OkButton.Content = new IconLabel { Icon = "layers", Text = "Zusammenführen" };
        SetBody(new StackPanel { Spacing = 10, Children = { new ScrollViewer { Content = grid, MaxHeight = 460 }, note } });
    }

    /// <summary>The ids in the order of the merge: the one to keep first.</summary>
    public IReadOnlyList<string> Order
    {
        get
        {
            var keep = Math.Max(0, _keep.FindIndex(r => r.IsChecked == true));
            return [_people[keep].Id, .. _people.Where((_, i) => i != keep).Select(p => p.Id)];
        }
    }

    /// <summary>Per field the id of the contact whose value stays.</summary>
    public IReadOnlyDictionary<string, string> Choices => _fields
        .Select(pair => (pair.Key, Index: pair.Value.FindIndex(r => r.IsChecked == true)))
        .Where(pair => pair.Index >= 0)
        .ToDictionary(pair => pair.Key, pair => _people[pair.Index].Id);

    /// <summary>Chooses the value of <paramref name="field"/> from the contact at <paramref name="index"/> (tests).</summary>
    public void Choose(string field, int index) => _fields[field][index].IsChecked = true;

    protected override bool Apply() => true;
}

/// <summary>
/// "Spalten zuordnen" (an import of a CSV file): every column of the file with the first values as an example and the field it fills – suggested by
/// its name (German and English Outlook, Google), changed by the person; "Dubletten überspringen" leaves out contacts that are here already.
/// </summary>
public sealed class CsvMappingDialog : OfficeDialog
{
    private static readonly IReadOnlyList<(string Id, string Label)> Choices = [(PersonCsv.Ignore, "– nicht übernehmen –"), .. PersonCsv.Fields.Select(f => (f.Id, f.Header))];

    private readonly List<ComboBox> _boxes = [];

    public CsvMappingDialog(CsvTable table)
        : base("Spalten zuordnen", 640)
    {
        Table = table;
        var mapping = PersonCsv.AutoMap(table.Columns);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*,12,220") };
        for (var i = 0; i < table.Columns.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = new TextBlock { Text = table.Columns[i].Length > 0 ? table.Columns[i] : $"Spalte {i + 1}", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var index = i;
            var sample = new TextBlock
            {
                Text = string.Join(" · ", table.Rows.Select(r => r[index]).Where(v => v.Trim().Length > 0).Take(2)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            }.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
            var box = new ComboBox { ItemsSource = Choices.Select(c => c.Label).ToList(), SelectedIndex = Math.Max(0, Choices.ToList().FindIndex(c => c.Id == mapping[i])), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 3) };
            AutomationProperties.SetName(box, "Feld für " + name.Text);
            _boxes.Add(box);
            Grid.SetRow(name, i);
            Grid.SetRow(sample, i);
            Grid.SetRow(box, i);
            Grid.SetColumn(sample, 2);
            Grid.SetColumn(box, 4);
            grid.Children.Add(name);
            grid.Children.Add(sample);
            grid.Children.Add(box);
        }

        SkipDuplicates = new CheckBox { Content = "Dubletten überspringen", IsChecked = true };
        var intro = new TextBlock { Text = $"{table.Rows.Count} Zeilen. Vorgeschlagen ist, was die Spaltennamen sagen; Spalten ohne Feld werden nicht übernommen.", TextWrapping = TextWrapping.Wrap };
        OkButton.Content = new IconLabel { Icon = "upload", Text = "Importieren" };
        SetBody(new StackPanel { Spacing = 10, Children = { intro, new ScrollViewer { Content = grid, MaxHeight = 420 }, SkipDuplicates } });
    }

    public CsvTable Table { get; }

    public CheckBox SkipDuplicates { get; }

    /// <summary>The field of every column.</summary>
    public IReadOnlyList<string> Mapping => _boxes.Select(b => Choices[Math.Max(0, b.SelectedIndex)].Id).ToList();

    /// <summary>Sets the field of a column (tests).</summary>
    public void Map(int column, string fieldId) => _boxes[column].SelectedIndex = Choices.ToList().FindIndex(c => c.Id == fieldId);

    protected override bool Apply()
    {
        if (Mapping.All(m => m.Length == 0))
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = QuestionDialog.ShowMessageAsync(this, Title ?? "", "Bitte ordnen Sie mindestens eine Spalte einem Feld zu."));
            return false;
        }

        return true;
    }
}

/// <summary>"Exportieren": the format (vCard 3.0 for Outlook, Apple and Android; vCard 4.0; CSV for Excel) and what (the selection or every contact).</summary>
public sealed class ExportDialog : OfficeDialog
{
    public ExportDialog(int selected, int total)
        : base("Kontakte exportieren", 440)
    {
        VCard3 = new RadioButton { GroupName = "format", Content = "vCard 3.0 (Outlook, Apple, Android)", IsChecked = true };
        VCard4 = new RadioButton { GroupName = "format", Content = "vCard 4.0" };
        Csv = new RadioButton { GroupName = "format", Content = "CSV (Excel, Outlook – ohne Bilder)" };
        OnlySelected = new RadioButton { GroupName = "scope", Content = selected == 1 ? "Der ausgewählte Kontakt" : $"Die {selected} ausgewählten Kontakte", IsChecked = selected > 0, IsEnabled = selected > 0 };
        All = new RadioButton { GroupName = "scope", Content = total == 1 ? "Alle Kontakte (1)" : $"Alle Kontakte ({total})", IsChecked = selected == 0 };
        OkButton.Content = new IconLabel { Icon = "download", Text = "Exportieren …" };
        SetBody(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "Format" }.Themed("MatText.FieldLabel"), VCard3, VCard4, Csv,
                new TextBlock { Text = "Kontakte", Margin = new Thickness(0, 10, 0, 0) }.Themed("MatText.FieldLabel"), OnlySelected, All,
            },
        });
    }

    public RadioButton VCard3 { get; }

    public RadioButton VCard4 { get; }

    public RadioButton Csv { get; }

    public RadioButton OnlySelected { get; }

    public RadioButton All { get; }

    public ContactExportFormat Format => Csv.IsChecked == true ? ContactExportFormat.Csv : VCard4.IsChecked == true ? ContactExportFormat.VCard4 : ContactExportFormat.VCard3;

    protected override bool Apply() => true;
}
