using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using MatCloud.Contract.Contacts;
using MatDesktop;
using ThixContacts.Core;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Pim.Model;
using ThixOffice.Pim.Rules;

namespace ThixOffice.Pim.Desktop.People;

/// <summary>
/// "Kontakt" (new or to change), like Outlook's contact form in tabs: Allgemein (picture, name, work, e-mail addresses, phone numbers, favourite),
/// Details (addresses, web addresses, birthday, anniversary, nickname, categories, groups) and Notizen. Several values of a kind, each with its label
/// ("Geschäftlich", "Mobil" …), rows added with "+" and removed with "×". OK saves through the session (one undoable step); a refusal of the rules is
/// shown at its field and the dialog stays open on the tab of the field.
/// </summary>
public sealed class ContactDialog : OfficeDialog
{
    private readonly ContactsSession _session;
    private readonly Person? _existing;
    private readonly TabControl _tabs = new();
    private readonly PersonAvatar _avatar = new(64);
    private readonly ValueRows _emails;
    private readonly ValueRows _phones;
    private readonly ValueRows _urls;
    private readonly StackPanel _addresses = new() { Spacing = 10 };
    private readonly WrapPanel _groups = new();
    private readonly List<(string Id, CheckBox Box)> _groupBoxes = [];
    private string _photoId;

    public ContactDialog(ContactsSession session, Person? existing, string? groupId = null)
        : base(existing is null ? "Neuer Kontakt" : "Kontakt bearbeiten", 680)
    {
        _session = session;
        _existing = existing;
        var c = existing?.Data ?? new ContactData();
        _photoId = c.PhotoId;
        _avatar.PhotoSource = id => session.Photo(id);
        OkButton.Content = new IconLabel { Icon = "save", Text = existing is null ? "Anlegen" : "Speichern" };

        Prefix = Box(c.Prefix, "Dr.");
        GivenName = Box(c.GivenName);
        MiddleName = Box(c.MiddleName);
        FamilyName = Box(c.FamilyName);
        Suffix = Box(c.Suffix, "jun.");
        DisplayName = Box(c.DisplayName, "aus Vor- und Nachname");
        Company = Box(c.Company);
        Department = Box(c.Department);
        JobTitle = Box(c.JobTitle);
        Nickname = Box(c.Nickname);
        Birthday = Box(DateInput(c.Birthday), "31.12.1980 oder 31.12.");
        Anniversary = Box(DateInput(c.Anniversary), "31.12.2010");
        Categories = Box(string.Join(", ", c.Categories), "Kunde, Familie");
        Notes = new TextBox { Text = c.Notes, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Height = 280 };
        Favorite = new CheckBox { Content = "Favorit", IsChecked = c.IsFavorite };
        _emails = new ValueRows("E-Mail-Adresse", nameof(ContactData.Emails), ContactLabels.Email, c.Emails, "name@example.com");
        _phones = new ValueRows("Telefonnummer", nameof(ContactData.Phones), ContactLabels.Phone, c.Phones, "+49 89 1234567");
        _urls = new ValueRows("Webadresse", nameof(ContactData.Urls), ContactLabels.Url, c.Urls, "https://example.com");
        foreach (var address in c.Addresses)
        {
            AddAddress(address);
        }

        foreach (var group in session.Groups)
        {
            var box = new CheckBox { Content = group.Name, IsChecked = existing is not null ? group.Data.Members.Contains(existing.Id) : group.Id == groupId, Margin = new Thickness(0, 0, 14, 4) };
            _groupBoxes.Add((group.Id, box));
            _groups.Children.Add(box);
        }

        _tabs.ItemsSource = new[]
        {
            new TabItem { Header = "Allgemein", Content = Scroll(GeneralTab()) },
            new TabItem { Header = "Details", Content = Scroll(DetailsTab()) },
            new TabItem { Header = "Notizen", Content = new StackPanel { Margin = new Thickness(0, 12, 0, 0), Children = { Field("Notizen", Notes, nameof(ContactData.Notes)) } } },
        };
        SetBody(_tabs);
        _avatar.Show(new Person("", c, DateTimeOffset.UtcNow));
        Opened += (_, _) => GivenName.Focus();
    }

    public TextBox Prefix { get; }

    public TextBox GivenName { get; }

    public TextBox MiddleName { get; }

    public TextBox FamilyName { get; }

    public TextBox Suffix { get; }

    public TextBox DisplayName { get; }

    public TextBox Company { get; }

    public TextBox Department { get; }

    public TextBox JobTitle { get; }

    public TextBox Nickname { get; }

    public TextBox Birthday { get; }

    public TextBox Anniversary { get; }

    public TextBox Categories { get; }

    public TextBox Notes { get; }

    public CheckBox Favorite { get; }

    public ValueRows Emails => _emails;

    public ValueRows Phones => _phones;

    public ValueRows Urls => _urls;

    /// <summary>The tab shown (0 Allgemein, 1 Details, 2 Notizen).</summary>
    public int Tab
    {
        get => _tabs.SelectedIndex;
        set => _tabs.SelectedIndex = value;
    }

    /// <summary>The id of the contact after OK.</summary>
    public string? SavedId { get; private set; }

    /// <summary>The message shown at a field (tests), or null.</summary>
    public string? ErrorText { get; private set; }

    /// <summary>A picture from a file (the business layer reads and checks it).</summary>
    public void SetPicture(byte[] bytes)
    {
        _photoId = _session.AddPhoto(bytes);
        _avatar.Show(new Person("", new ContactData { GivenName = GivenName.Text ?? "", FamilyName = FamilyName.Text ?? "", PhotoId = _photoId }, DateTimeOffset.UtcNow));
    }

    public void AddAddress(ContactAddress? address = null)
    {
        var row = new AddressRow(address ?? new ContactAddress(), _addresses.Children.Count);
        row.Removed += (_, _) => _addresses.Children.Remove(row);
        _addresses.Children.Add(row);
    }

    protected override bool Apply()
    {
        ErrorText = null;
        FormErrors.ShowError(this, null, "");
        var birthday = ParseDate(Birthday.Text);
        var anniversary = ParseDate(Anniversary.Text);
        if (birthday is null || anniversary is null)
        {
            return Fail(birthday is null ? nameof(ContactData.Birthday) : nameof(ContactData.Anniversary), "Bitte ein Datum wie 31.12.1980 eingeben (ohne Jahr: 31.12.).");
        }

        var data = (_existing?.Data ?? new ContactData()) with
        {
            Prefix = Prefix.Text ?? "",
            GivenName = GivenName.Text ?? "",
            MiddleName = MiddleName.Text ?? "",
            FamilyName = FamilyName.Text ?? "",
            Suffix = Suffix.Text ?? "",
            DisplayName = DisplayName.Text ?? "",
            Company = Company.Text ?? "",
            Department = Department.Text ?? "",
            JobTitle = JobTitle.Text ?? "",
            Nickname = Nickname.Text ?? "",
            Emails = _emails.Values,
            Phones = _phones.Values,
            Urls = _urls.Values,
            Addresses = _addresses.Children.OfType<AddressRow>().Select(r => r.Value).ToList(),
            Birthday = birthday,
            Anniversary = anniversary,
            Notes = Notes.Text ?? "",
            Categories = (Categories.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            IsFavorite = Favorite.IsChecked == true,
            PhotoId = _photoId,
        };

        try
        {
            SavedId = _session.Save(_existing?.Id, data);
            foreach (var (groupId, box) in _groupBoxes)
            {
                var member = _session.FindGroup(groupId)?.Data.Members.Contains(SavedId) == true;
                if (member != (box.IsChecked == true))
                {
                    _session.SetMembers(groupId, [SavedId], box.IsChecked == true);
                }
            }

            return true;
        }
        catch (BusinessException exception)
        {
            return Fail(MapField(exception.Field), exception.Message);
        }
    }

    /// <summary>Shows a message at its field and turns to the tab it is on.</summary>
    private bool Fail(string? field, string message)
    {
        ErrorText = message;
        Tab = field switch
        {
            null => Tab,
            _ when field.StartsWith(nameof(ContactData.Addresses), StringComparison.Ordinal) || field.StartsWith(nameof(ContactData.Urls), StringComparison.Ordinal)
                   || field is nameof(ContactData.Birthday) or nameof(ContactData.Anniversary) or nameof(ContactData.Categories) or nameof(ContactData.Nickname) => 1,
            nameof(ContactData.Notes) => 2,
            _ => 0,
        };
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!FormErrors.ShowError(this, field, message))
            {
                FormErrors.ShowError(this, nameof(ContactData.GivenName), message);
            }
        });
        FormErrors.ShowError(this, field, message);
        return false;
    }

    /// <summary>The rules count only the rows with a value ("Emails[1]"); the form's rows have their own index.</summary>
    private string? MapField(string? field)
    {
        if (field is null || !field.Contains('['))
        {
            return field;
        }

        var kind = field[..field.IndexOf('[')];
        var index = int.Parse(field[(field.IndexOf('[') + 1)..^1], System.Globalization.CultureInfo.InvariantCulture);
        var rows = kind switch
        {
            nameof(ContactData.Emails) => _emails,
            nameof(ContactData.Phones) => _phones,
            nameof(ContactData.Urls) => _urls,
            _ => null,
        };
        return rows is null ? field : $"{kind}[{rows.RowOfValue(index)}]";
    }

    private Control GeneralTab()
    {
        var pick = new Button { Content = new IconLabel { Icon = "image", Text = "Bild wählen …" } };
        pick.Click += async (_, _) =>
        {
            var path = await ThixOffice.Desktop.OfficeApplication.FileDialogs.PickPictureAsync(this, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            if (path is null)
            {
                return;
            }

            try
            {
                SetPicture(ContactFiles.ReadPicture(path));
            }
            catch (BusinessException exception)
            {
                Fail(nameof(ContactData.PhotoId), exception.Message);
            }
        };
        var remove = new Button { Content = new IconLabel { Icon = "x", Text = "Bild entfernen" } };
        remove.Click += (_, _) =>
        {
            _photoId = "";
            _avatar.Show(new Person("", new ContactData { GivenName = GivenName.Text ?? "", FamilyName = FamilyName.Text ?? "" }, DateTimeOffset.UtcNow));
        };
        var photoField = Field("Bild", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { pick, remove } }, nameof(ContactData.PhotoId));
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { _avatar, new StackPanel { Spacing = 6, Children = { photoField, Favorite } } } };

        var names = Grid2(
            Field("Anrede", Prefix, nameof(ContactData.Prefix)), Field("Vorname", GivenName, nameof(ContactData.GivenName)),
            Field("Weitere Vornamen", MiddleName, nameof(ContactData.MiddleName)), Field("Nachname", FamilyName, nameof(ContactData.FamilyName)),
            Field("Namenszusatz", Suffix, nameof(ContactData.Suffix)), Field("Anzeigename", DisplayName, nameof(ContactData.DisplayName)),
            Field("Firma", Company, nameof(ContactData.Company)), Field("Abteilung", Department, nameof(ContactData.Department)),
            Field("Position", JobTitle, nameof(ContactData.JobTitle)));
        return new StackPanel { Spacing = 14, Margin = new Thickness(0, 12, 0, 0), Children = { head, names, Section("E-Mail", _emails), Section("Telefon", _phones) } };
    }

    private Control DetailsTab()
    {
        var addAddress = new Button { Content = new IconLabel { Icon = "plus", Text = "Adresse hinzufügen" } }.Themed("MatButton.Ghost");
        addAddress.Click += (_, _) => AddAddress();
        var addresses = new StackPanel { Spacing = 8, Children = { SectionTitle("Adressen"), _addresses, addAddress } };
        var dates = Grid2(
            Field("Geburtstag", Birthday, nameof(ContactData.Birthday)), Field("Jahrestag", Anniversary, nameof(ContactData.Anniversary)),
            Field("Spitzname", Nickname, nameof(ContactData.Nickname)), Field("Kategorien", Categories, nameof(ContactData.Categories)));
        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(0, 12, 0, 0), Children = { addresses, Section("Web", _urls), dates } };
        if (_groupBoxes.Count > 0)
        {
            panel.Children.Add(new StackPanel { Spacing = 6, Children = { SectionTitle("Kontaktgruppen"), _groups } });
        }

        return panel;
    }

    private static Control Section(string title, ValueRows rows) => new StackPanel { Spacing = 8, Children = { SectionTitle(title), rows } };

    private static TextBlock SectionTitle(string text) => new TextBlock { Text = text }.Themed("MatText.SectionTitle");

    private static ScrollViewer Scroll(Control content) => new() { Content = content, MaxHeight = 520, Padding = new Thickness(0, 0, 12, 0) };

    private static Grid Grid2(params Control[] fields)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") };
        for (var i = 0; i < fields.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (var i = 0; i < fields.Length; i++)
        {
            Grid.SetRow(fields[i], i / 2);
            Grid.SetColumn(fields[i], i % 2 == 0 ? 0 : 2);
            fields[i].Margin = new Thickness(0, 0, 0, 8);
            grid.Children.Add(fields[i]);
        }

        return grid;
    }

    private static MatFormField Field(string label, Control control, string field)
    {
        var form = new MatFormField { Label = label, Content = control };
        FormErrors.SetField(form, field);
        AutomationProperties.SetName(control, label);
        return form;
    }

    private static TextBox Box(string text, string? watermark = null) => new() { Text = text, Watermark = watermark };

    /// <summary>"01.05.1980" / "01.05." for a stored date.</summary>
    internal static string DateInput(string date)
    {
        if (!ContactsContract.TryParseDate(date, out var year, out var month, out var day))
        {
            return date;
        }

        return year is null ? $"{day:00}.{month:00}." : $"{day:00}.{month:00}.{year:0000}";
    }

    /// <summary>A typed date in the stored form; empty for nothing, null when it is no date.</summary>
    internal static string? ParseDate(string? text)
    {
        var value = (text ?? "").Trim();
        if (value.Length == 0)
        {
            return "";
        }

        var parsed = PersonCsv.Date(value);
        return parsed.Length > 0 ? parsed : null;
    }

    /// <summary>The rows of one kind of value: a label (a choice of the known ones, or the user's own) and the value, "+" adds one, "×" removes one.</summary>
    public sealed class ValueRows : StackPanel
    {
        private readonly string _noun;
        private readonly string _field;
        private readonly IReadOnlyList<string> _labels;
        private readonly string _placeholder;
        private readonly StackPanel _rows = new() { Spacing = 6 };

        public ValueRows(string noun, string field, IReadOnlyList<string> labels, IEnumerable<ContactValue> values, string placeholder)
        {
            _noun = noun;
            _field = field;
            _labels = labels;
            _placeholder = placeholder;
            Spacing = 6;
            var add = new Button { Content = new IconLabel { Icon = "plus", Text = noun + " hinzufügen" } }.Themed("MatButton.Ghost");
            add.Click += (_, _) => Add(new ContactValue(_labels[0], ""));
            Children.Add(_rows);
            Children.Add(add);
            foreach (var value in values)
            {
                Add(value);
            }

            if (_rows.Children.Count == 0)
            {
                Add(new ContactValue(_labels[0], ""));
            }
        }

        /// <summary>The values typed (empty rows left out; the rules drop them too).</summary>
        public IReadOnlyList<ContactValue> Values => Rows.Select(r => new ContactValue(r.Label, r.Value)).ToList();

        public IReadOnlyList<ValueRow> Rows => _rows.Children.OfType<ValueRow>().ToList();

        /// <summary>A new row (returns it: tests type into it).</summary>
        public ValueRow Add(ContactValue value)
        {
            var row = new ValueRow(_labels, value, _placeholder, _noun);
            row.Removed += (_, _) =>
            {
                _rows.Children.Remove(row);
                Renumber();
            };
            _rows.Children.Add(row);
            Renumber();
            return row;
        }

        /// <summary>The row of the <paramref name="index"/>-th value that is not empty.</summary>
        public int RowOfValue(int index)
        {
            var filled = -1;
            var rows = Rows;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].Value.Trim().Length > 0 && ++filled == index)
                {
                    return i;
                }
            }

            return 0;
        }

        private void Renumber()
        {
            var rows = Rows;
            for (var i = 0; i < rows.Count; i++)
            {
                FormErrors.SetField(rows[i].Field, $"{_field}[{i}]");
            }
        }
    }

    /// <summary>One value with its label.</summary>
    public sealed class ValueRow : Grid
    {
        private readonly ComboBox _label;

        public ValueRow(IReadOnlyList<string> labels, ContactValue value, string placeholder, string noun)
        {
            ColumnDefinitions = new ColumnDefinitions("170,8,*,4,Auto");
            var choices = labels.Contains(value.Label) ? labels : [.. labels, value.Label];
            _label = new ComboBox { ItemsSource = choices.Select(ContactLabels.NameOf).ToList(), SelectedIndex = choices.ToList().IndexOf(value.Label), HorizontalAlignment = HorizontalAlignment.Stretch, Tag = choices };
            AutomationProperties.SetName(_label, "Art der " + noun);
            Box = new TextBox { Text = value.Value, Watermark = placeholder };
            AutomationProperties.SetName(Box, noun);
            Field = new MatFormField { Content = Box };
            var remove = new Button { Content = new MatIcon { Icon = "x" }, VerticalAlignment = VerticalAlignment.Top }.Themed("MatButton.Icon");
            AutomationProperties.SetName(remove, noun + " entfernen");
            ToolTip.SetTip(remove, "Entfernen");
            remove.Click += (_, _) => Removed?.Invoke(this, EventArgs.Empty);
            Grid.SetColumn(Field, 2);
            Grid.SetColumn(remove, 4);
            Children.Add(_label);
            Children.Add(Field);
            Children.Add(remove);
        }

        public event EventHandler? Removed;

        public TextBox Box { get; }

        public MatFormField Field { get; }

        public string Label => _label.Tag is IReadOnlyList<string> choices && _label.SelectedIndex >= 0 ? choices[_label.SelectedIndex] : ContactLabels.Other;

        public string Value => Box.Text ?? "";

        /// <summary>Chooses a label by its id ("mobile").</summary>
        public void ChooseLabel(string label)
        {
            if (_label.Tag is IReadOnlyList<string> choices && choices.ToList().IndexOf(label) is >= 0 and var index)
            {
                _label.SelectedIndex = index;
            }
        }
    }

    /// <summary>One postal address: its label, street (lines), postal code and city, region and country.</summary>
    private sealed class AddressRow : Border
    {
        private readonly ComboBox _label;
        private readonly TextBox _street;
        private readonly TextBox _postal;
        private readonly TextBox _city;
        private readonly TextBox _region;
        private readonly TextBox _country;

        public AddressRow(ContactAddress address, int index)
        {
            var labels = ContactLabels.Address.Contains(address.Label) ? ContactLabels.Address : [.. ContactLabels.Address, address.Label];
            _label = new ComboBox { ItemsSource = labels.Select(ContactLabels.NameOf).ToList(), SelectedIndex = labels.ToList().IndexOf(address.Label), Width = 170, Tag = labels };
            _street = new TextBox { Text = address.Street, AcceptsReturn = true, Watermark = "Straße und Hausnummer", Height = 56 };
            _postal = new TextBox { Text = address.PostalCode, Watermark = "PLZ" };
            _city = new TextBox { Text = address.City, Watermark = "Ort" };
            _region = new TextBox { Text = address.Region, Watermark = "Bundesland / Region" };
            _country = new TextBox { Text = address.Country, Watermark = "Land" };
            AutomationProperties.SetName(_street, "Straße");
            AutomationProperties.SetName(_city, "Ort");
            var remove = new Button { Content = new IconLabel { Icon = "x", Text = "Adresse entfernen" } }.Themed("MatButton.Ghost");
            remove.Click += (_, _) => Removed?.Invoke(this, EventArgs.Empty);
            var city = new Grid { ColumnDefinitions = new ColumnDefinitions("110,8,*") };
            Grid.SetColumn(_city, 2);
            city.Children.Add(_postal);
            city.Children.Add(_city);
            var region = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*") };
            Grid.SetColumn(_country, 2);
            region.Children.Add(_region);
            region.Children.Add(_country);
            var form = new MatFormField { Content = new StackPanel { Spacing = 6, Children = { new DockPanel { Children = { _label, remove } }, _street, city, region } } };
            FormErrors.SetField(form, $"{nameof(ContactData.Addresses)}[{index}]");
            DockPanel.SetDock(_label, Dock.Left);
            remove.HorizontalAlignment = HorizontalAlignment.Right;
            Padding = new Thickness(10);
            CornerRadius = new CornerRadius(6);
            BorderThickness = new Thickness(1);
            this.Use(BorderBrushProperty, "Mat.Border");
            Child = form;
        }

        public event EventHandler? Removed;

        public ContactAddress Value => new()
        {
            Label = _label.Tag is IReadOnlyList<string> labels && _label.SelectedIndex >= 0 ? labels[_label.SelectedIndex] : ContactLabels.Other,
            Street = _street.Text ?? "",
            PostalCode = _postal.Text ?? "",
            City = _city.Text ?? "",
            Region = _region.Text ?? "",
            Country = _country.Text ?? "",
        };
    }
}
