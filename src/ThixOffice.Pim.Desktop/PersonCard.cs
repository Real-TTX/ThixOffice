using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatCloud.Contract.Contacts;
using MatDesktop;
using ThixOffice.Desktop;
using ThixOffice.Pim.Model;

namespace ThixOffice.Pim.Desktop;

/// <summary>
/// The card of a person (Outlook's "Kontaktkarte"), read only: the avatar, the name, position and company, then a section per kind – E-Mail (a click
/// writes a mail), Telefon, Adresse, Web, Geburtstag and Jahrestag, Kategorien, Notizen – each value with its label. Shared by ThixContacts (beside the
/// list) and ThixMail (a sender's card). Empty sections are left out.
/// </summary>
public sealed class PersonCard : UserControl
{
    private static readonly string[] Months = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"];

    private readonly PersonAvatar _avatar = new(72);
    private readonly TextBlock _name = new() { FontSize = MatTokens.Type.FsXl, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _work = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _sections = new() { Spacing = 18, Margin = new Thickness(0, 20, 0, 0) };
    private readonly StackPanel _header = new() { Orientation = Orientation.Horizontal, Spacing = 16 };

    public PersonCard()
    {
        _work.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4, Children = { _name, _work } };
        _header.Children.Add(_avatar);
        _header.Children.Add(titles);
        Content = new ScrollViewer
        {
            Content = new StackPanel { Margin = new Thickness(24, 20), Children = { _header, _sections } },
        };
    }

    /// <summary>Where pictures come from (id → bytes).</summary>
    public Func<string, byte[]?>? PhotoSource
    {
        get => _avatar.PhotoSource;
        set => _avatar.PhotoSource = value;
    }

    /// <summary>The colour key (a Mat token) of a category; null: the muted text.</summary>
    public Func<string, string>? CategoryColor { get; set; }

    /// <summary>The person shown, or null.</summary>
    public Person? Person { get; private set; }

    /// <summary>The name shown (tests).</summary>
    public string NameText => _name.Text ?? "";

    /// <summary>The titles of the sections shown (tests).</summary>
    public IReadOnlyList<string> SectionTitles => _sections.Children.OfType<StackPanel>().Select(s => (s.Children[0] as TextBlock)?.Text ?? "").ToList();

    public void Show(Person? person)
    {
        Person = person;
        _sections.Children.Clear();
        _avatar.Show(person);
        IsVisible = person is not null;
        if (person is null)
        {
            return;
        }

        var c = person.Data;
        _name.Text = person.Name;
        AutomationProperties.SetName(this, "Kontaktkarte " + person.Name);
        var work = string.Join(" · ", new[] { c.JobTitle, string.Join(", ", new[] { c.Company, c.Department }.Where(p => p.Length > 0)) }.Where(p => p.Length > 0));
        _work.Text = work;
        _work.IsVisible = work.Length > 0;

        Section("E-Mail", "mail", c.Emails.Select<ContactValue, (string, string, Action?)>(e => (ContactLabels.NameOf(e.Label), e.Value, (Action?)(() => Ui.OpenExternal("mailto:" + e.Value)))));
        Section("Telefon", "smartphone", c.Phones.Select(p => (ContactLabels.NameOf(p.Label), p.Value, (Action?)null)));
        Section("Adresse", "home", c.Addresses.Select(a => (ContactLabels.NameOf(a.Label), string.Join("\n", Lines(a)), (Action?)null)));
        Section("Web", "globe", c.Urls.Select<ContactValue, (string, string, Action?)>(u => (ContactLabels.NameOf(u.Label), u.Value, (Action?)(() => Ui.OpenExternal(u.Value)))));
        var dates = new List<(string, string, Action?)>();
        if (c.Birthday.Length > 0)
        {
            dates.Add(("Geburtstag", DateText(c.Birthday), null));
        }

        if (c.Anniversary.Length > 0)
        {
            dates.Add(("Jahrestag", DateText(c.Anniversary), null));
        }

        Section("Daten", "calendar", dates);
        if (c.Nickname.Length > 0)
        {
            Section("Spitzname", "user", [("", c.Nickname, null)]);
        }

        if (c.Categories.Count > 0)
        {
            var tags = new WrapPanel();
            foreach (var category in c.Categories)
            {
                var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
                dot.Use(Border.BackgroundProperty, CategoryColor?.Invoke(category) ?? "Mat.TextMuted");
                var tag = new Border
                {
                    Padding = new Thickness(8, 3),
                    Margin = new Thickness(0, 0, 6, 6),
                    CornerRadius = new CornerRadius(10),
                    Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { dot, new TextBlock { Text = category } } },
                };
                tag.Use(Border.BackgroundProperty, "Mat.Surface2");
                tags.Children.Add(tag);
            }

            _sections.Children.Add(new StackPanel { Spacing = 6, Children = { Title("Kategorien", "tag"), tags } });
        }

        if (c.Notes.Length > 0)
        {
            Section("Notizen", "file-text", [("", c.Notes, null)]);
        }
    }

    /// <summary>"1. Mai 1980" or "1. Mai" for a date of a contact.</summary>
    public static string DateText(string date)
    {
        if (!ContactsContract.TryParseDate(date, out var year, out var month, out var day))
        {
            return date;
        }

        return year is null ? $"{day}. {Months[month - 1]}" : $"{day}. {Months[month - 1]} {year}";
    }

    /// <summary>The lines of an address: street lines, postal code and city, region, country.</summary>
    public static IEnumerable<string> Lines(ContactAddress address)
    {
        foreach (var line in address.Street.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return line;
        }

        var city = string.Join(' ', new[] { address.PostalCode, address.City }.Where(p => p.Length > 0));
        if (city.Length > 0)
        {
            yield return city;
        }

        if (address.Region.Length > 0)
        {
            yield return address.Region;
        }

        if (address.Country.Length > 0)
        {
            yield return address.Country;
        }
    }

    private void Section(string title, string icon, IEnumerable<(string Label, string Value, Action? Open)> values)
    {
        var list = values.ToList();
        if (list.Count == 0)
        {
            return;
        }

        var section = new StackPanel { Spacing = 6, Children = { Title(title, icon) } };
        foreach (var (label, value, open) in list)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*") };
            var labelText = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Top };
            labelText.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
            Control valueControl;
            if (open is not null)
            {
                var link = new Button { Content = value, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left }.Themed("MatButton.Link");
                link.Click += (_, _) => open();
                ToolTip.SetTip(link, title == "E-Mail" ? "E-Mail schreiben" : "Im Browser öffnen");
                valueControl = link;
            }
            else
            {
                valueControl = new SelectableTextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
            }

            Grid.SetColumn(valueControl, label.Length == 0 ? 0 : 1);
            if (label.Length == 0)
            {
                Grid.SetColumnSpan(valueControl, 2);
            }
            else
            {
                row.Children.Add(labelText);
            }

            row.Children.Add(valueControl);
            section.Children.Add(row);
        }

        _sections.Children.Add(section);
    }

    private static TextBlock Title(string text, string icon) => new TextBlock { Text = text }.Themed("MatText.SectionTitle");
}
