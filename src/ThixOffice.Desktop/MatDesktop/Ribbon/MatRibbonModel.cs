// MatDesktop 0.3.1 – a copy from MatDesign, do not edit here
// Plain C#, only System: the definition of a ribbon, which any front end can draw (MatRibbon in Avalonia; a web app the same definition).
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MatDesktop;

/// <summary>
/// How large an item of a ribbon is drawn, from the smallest: <see cref="Small"/> is the icon alone (24 high), <see cref="Medium"/> the small icon with the label beside it (24 high, three
/// stack in a column), <see cref="Large"/> the large icon with the label under it (the full height of a group). The order counts: a group that shrinks draws its items one size smaller.
/// </summary>
public enum MatRibbonSize
{
    /// <summary>The icon alone; the label is the tooltip and the name for a screen reader.</summary>
    Small,

    /// <summary>The small icon and the label beside it.</summary>
    Medium,

    /// <summary>The large icon and the label under it, the full height of a group.</summary>
    Large,
}

/// <summary>
/// How a group of a ribbon is drawn while the window narrows (<see cref="MatRibbonLayout"/>): every item as large as it may be, then one size smaller, then the smallest, then the group as one
/// button that shows the group in a flyout.
/// </summary>
public enum MatRibbonGroupState
{
    /// <summary>Every item in the size it was defined with.</summary>
    Large,

    /// <summary>Large items drawn medium (the small icon and the label, three in a column).</summary>
    Medium,

    /// <summary>Medium items drawn small (the icon alone), where they have an icon.</summary>
    Small,

    /// <summary>The group is one button with its icon and label; the group itself opens under it.</summary>
    Collapsed,
}

/// <summary>
/// An item of a ribbon: a command and its state. The state (<see cref="IsEnabled"/>, <see cref="IsVisible"/>, the checked state, a value) is changed on the item and every front end that shows
/// it follows at once (<see cref="INotifyPropertyChanged"/>): a program pushes the state of the selection on every change without building the ribbon again. Setting a value it already has
/// does nothing.
/// </summary>
public abstract class MatRibbonItem : INotifyPropertyChanged
{
    private string _label;
    private string? _icon;
    private string? _toolTip;
    private string? _shortcut;
    private string? _keyTip;
    private bool _isEnabled = true;
    private bool _isVisible = true;

    /// <summary>An item with an id (unique in its ribbon) and a label.</summary>
    protected MatRibbonItem(string id, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        _label = label ?? string.Empty;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// The id, unique in the ribbon (<c>bold</c>, <c>font-size</c>): what a program finds the item by (<see cref="MatRibbonDefinition.Find"/>). An item of the quick access shares the id of
    /// the ribbon's item of the same command (<see cref="MatRibbonDefinition.QuickAccess"/>).
    /// </summary>
    public string Id { get; }

    /// <summary>The label (sentence case, <c>Fett</c>); also the name for a screen reader and the first line of the tooltip.</summary>
    public string Label
    {
        get => _label;
        set => Set(ref _label, value ?? string.Empty);
    }

    /// <summary>The name of an icon of the sprite (<c>bold</c>), or null.</summary>
    public string? Icon
    {
        get => _icon;
        set => Set(ref _icon, value);
    }

    /// <summary>A sentence that says what the command does, under the label in the tooltip, or null.</summary>
    public string? ToolTip
    {
        get => _toolTip;
        set => Set(ref _toolTip, value);
    }

    /// <summary>The shortcut as the user reads it (<c>Strg+B</c>, on macOS <c>⌘B</c>), named in the tooltip (DESK-07); the program handles the key.</summary>
    public string? Shortcut
    {
        get => _shortcut;
        set => Set(ref _shortcut, value);
    }

    /// <summary>The key tip shown after Alt (one or two letters), or null: then the ribbon gives one from the label.</summary>
    public string? KeyTip
    {
        get => _keyTip;
        set => Set(ref _keyTip, value);
    }

    /// <summary>False turns the item off (drawn faint, not pressable).</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => Set(ref _isEnabled, value);
    }

    /// <summary>False hides the item.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    /// <summary>The first line of the tooltip: the label and the shortcut, <c>Fett (Strg+B)</c> (DESK-07).</summary>
    public string ToolTipTitle => string.IsNullOrWhiteSpace(Shortcut) ? Label : $"{Label} ({Shortcut.Trim()})";

    /// <summary>The items inside this one (a stack, a row, a menu), for walking a ribbon.</summary>
    public virtual IEnumerable<MatRibbonItem> Children => [];

    /// <summary>Sets a field and says so when the value changed; true when it changed.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name is nameof(Label) or nameof(Shortcut))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToolTipTitle)));
        }

        return true;
    }
}

/// <summary>A button: a command (<see cref="Command"/>). <see cref="Size"/> is the largest it is drawn, <see cref="MinSize"/> the smallest a narrow window may make it.</summary>
public class MatRibbonButton : MatRibbonItem
{
    private MatRibbonSize _size = MatRibbonSize.Medium;
    private MatRibbonSize _minSize = MatRibbonSize.Small;

    /// <summary>A button with an id, a label, an icon of the sprite and the command it runs.</summary>
    public MatRibbonButton(string id, string label, string? icon = null, Action? command = null)
        : base(id, label)
    {
        Icon = icon;
        Command = command;
    }

    /// <summary>What the button does, or null (the program listens to <see cref="MatRibbonDefinition.Invoked"/> instead).</summary>
    public Action? Command { get; set; }

    /// <summary>The largest size (Medium by default; Large for the main commands of a group: Einfügen, Neue Folie).</summary>
    public MatRibbonSize Size
    {
        get => _size;
        set => Set(ref _size, value);
    }

    /// <summary>The smallest size a narrow window may draw it in (Small by default).</summary>
    public MatRibbonSize MinSize
    {
        get => _minSize;
        set => Set(ref _minSize, value);
    }
}

/// <summary>
/// A button that stays pressed (Fett, Lineal): <see cref="IsChecked"/> is its state, drawn as the tint of the accent behind the whole button (COLOR-08). Toggles with the same
/// <see cref="GroupName"/> are one choice (Linksbündig, Zentriert, Rechtsbündig): checking one unchecks the others, and pressing the checked one leaves it checked.
/// </summary>
public class MatRibbonToggle : MatRibbonButton
{
    private bool _isChecked;

    /// <summary>A toggle with an id, a label, an icon and what it does with its new state.</summary>
    public MatRibbonToggle(string id, string label, string? icon = null, Action<bool>? toggled = null)
        : base(id, label, icon)
    {
        Toggled = toggled;
    }

    /// <summary>True while the toggle is on.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set => Set(ref _isChecked, value);
    }

    /// <summary>The name of the choice this toggle is one option of, or null.</summary>
    public string? GroupName { get; set; }

    /// <summary>What the toggle does when the user changes it (the new state), or null.</summary>
    public Action<bool>? Toggled { get; set; }
}

/// <summary>A button that opens a menu of items (<see cref="Menu"/>: buttons, toggles, separators): Zeilenabstand, Spalten.</summary>
public class MatRibbonMenuButton : MatRibbonButton
{
    /// <summary>A menu button with an id, a label, an icon and the items of its menu.</summary>
    public MatRibbonMenuButton(string id, string label, string? icon = null, params MatRibbonItem[] menu)
        : base(id, label, icon)
    {
        Menu = [.. menu];
    }

    /// <summary>The items of the menu, in their order.</summary>
    public List<MatRibbonItem> Menu { get; }

    /// <inheritdoc />
    public override IEnumerable<MatRibbonItem> Children => Menu;
}

/// <summary>A button of two parts: the larger part runs <see cref="MatRibbonButton.Command"/>, the arrow opens the menu (Einfügen with its choices, Aufzählung with its symbols).</summary>
public class MatRibbonSplitButton : MatRibbonMenuButton
{
    /// <summary>A split button with an id, a label, an icon, its command and the items of its menu.</summary>
    public MatRibbonSplitButton(string id, string label, string? icon = null, Action? command = null, params MatRibbonItem[] menu)
        : base(id, label, icon, menu)
    {
        Command = command;
    }
}

/// <summary>
/// A colour button (Schriftfarbe, Hervorhebung, Füllung): the button applies <see cref="Color"/>, shown as a bar under its icon; the arrow opens the palette. The colours are the document's,
/// data like a swatch (<c>#rrggbb</c>), never colours of the theme: the program gives them (<see cref="Palette"/>).
/// </summary>
public class MatRibbonColorPicker : MatRibbonSplitButton
{
    private string? _color;

    /// <summary>A colour button with an id, a label, an icon and the colours of its palette (<c>#rrggbb</c>).</summary>
    public MatRibbonColorPicker(string id, string label, string? icon, IEnumerable<string> palette)
        : base(id, label, icon)
    {
        Palette = [.. palette ?? throw new ArgumentNullException(nameof(palette))];
    }

    /// <summary>The colour the button applies (<c>#rrggbb</c>), or null for the automatic one (no colour of its own).</summary>
    public string? Color
    {
        get => _color;
        set => Set(ref _color, value);
    }

    /// <summary>The colours of the palette (<c>#rrggbb</c>), row by row.</summary>
    public List<string> Palette { get; }

    /// <summary>How many colours a row of the palette has (10, like the theme colours of an office program).</summary>
    public int Columns { get; set; } = 10;

    /// <summary>The label of the entry without a colour (<c>Automatisch</c>, <c>Keine Farbe</c>), or null for none.</summary>
    public string? AutomaticLabel { get; set; }

    /// <summary>What "More colours …" does (a dialog of the program), or null for no such entry.</summary>
    public Action? MoreColors { get; set; }

    /// <summary>What happens with a colour that was chosen or applied (null: the automatic one).</summary>
    public Action<string?>? ColorChosen { get; set; }
}

/// <summary>A combo box (Schriftart, Schriftgrad): a list of <see cref="Options"/> and, when <see cref="IsEditable"/>, a value of its own typed in.</summary>
public class MatRibbonComboBox : MatRibbonItem
{
    private string? _text;

    /// <summary>A combo box with an id, a label (the tooltip and the name; shown beside it with <see cref="ShowLabel"/>) and its options.</summary>
    public MatRibbonComboBox(string id, string label, IEnumerable<string> options, Action<string>? committed = null)
        : base(id, label)
    {
        Options = [.. options ?? throw new ArgumentNullException(nameof(options))];
        Committed = committed;
    }

    /// <summary>The value shown (an option or, editable, any text), or null for none (a selection of several fonts).</summary>
    public string? Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    /// <summary>The options of the list.</summary>
    public List<string> Options { get; }

    /// <summary>True lets the user type a value (a font size of 10.5).</summary>
    public bool IsEditable { get; set; }

    /// <summary>The width of the field in px.</summary>
    public double Width { get; set; } = 120;

    /// <summary>True shows the label at the left of the field (a field whose meaning is not clear from its value).</summary>
    public bool ShowLabel { get; set; }

    /// <summary>What happens with a value the user chose or typed and confirmed.</summary>
    public Action<string>? Committed { get; set; }
}

/// <summary>A number field with buttons up and down (Abstand vor, Einzug): <see cref="Value"/> between <see cref="Minimum"/> and <see cref="Maximum"/> in steps of <see cref="Step"/>.</summary>
public class MatRibbonNumber : MatRibbonItem
{
    private double _value;

    /// <summary>A number field with an id and a label.</summary>
    public MatRibbonNumber(string id, string label, Action<double>? changed = null)
        : base(id, label)
    {
        Changed = changed;
    }

    /// <summary>The value.</summary>
    public double Value
    {
        get => _value;
        set => Set(ref _value, value);
    }

    /// <summary>The smallest value.</summary>
    public double Minimum { get; set; }

    /// <summary>The largest value.</summary>
    public double Maximum { get; set; } = 100;

    /// <summary>The step of the buttons and the arrow keys.</summary>
    public double Step { get; set; } = 1;

    /// <summary>The digits after the decimal point.</summary>
    public int Decimals { get; set; }

    /// <summary>The unit after the number (<c>pt</c>, <c>cm</c>), or null.</summary>
    public string? Unit { get; set; }

    /// <summary>The width of the field in px.</summary>
    public double Width { get; set; } = 96;

    /// <summary>True shows the label (or the icon) at the left of the field (true by default: a number says little without it).</summary>
    public bool ShowLabel { get; set; } = true;

    /// <summary>What happens with a value the user set.</summary>
    public Action<double>? Changed { get; set; }
}

/// <summary>An entry of a gallery: an id, a label and how its picture looks (a sample text in the look of a style, or an icon).</summary>
public sealed record MatRibbonGalleryEntry(string Id, string Label)
{
    /// <summary>The sample text of the picture (<c>AaBbCc</c>), or null for the label alone.</summary>
    public string? Sample { get; init; }

    /// <summary>The size of the sample in px, or null for the size of the body.</summary>
    public double? SampleSize { get; init; }

    /// <summary>True draws the sample bold.</summary>
    public bool SampleIsBold { get; init; }

    /// <summary>True draws the sample italic.</summary>
    public bool SampleIsItalic { get; init; }

    /// <summary>True draws the sample in the accent text colour (a heading).</summary>
    public bool SampleIsAccent { get; init; }

    /// <summary>The name of an icon of the sprite instead of a sample, or null.</summary>
    public string? Icon { get; init; }
}

/// <summary>
/// A gallery (Formatvorlagen, Diagrammtypen): the first entries in a row in the ribbon and all of them in a flyout; the chosen one (<see cref="SelectedId"/>) is the tint of the accent with
/// the accent line all around (COLOR-08). In a narrow window it is one large button that opens the flyout.
/// </summary>
public class MatRibbonGallery : MatRibbonItem
{
    private string? _selectedId;

    /// <summary>A gallery with an id, a label, an icon (for the button of a narrow window) and its entries.</summary>
    public MatRibbonGallery(string id, string label, string? icon, IEnumerable<MatRibbonGalleryEntry> entries, Action<string>? chosen = null)
        : base(id, label)
    {
        Icon = icon;
        Entries = [.. entries ?? throw new ArgumentNullException(nameof(entries))];
        Chosen = chosen;
    }

    /// <summary>The entries.</summary>
    public List<MatRibbonGalleryEntry> Entries { get; }

    /// <summary>The id of the chosen entry, or null.</summary>
    public string? SelectedId
    {
        get => _selectedId;
        set => Set(ref _selectedId, value);
    }

    /// <summary>How many entries the row in the ribbon shows.</summary>
    public int InlineCount { get; set; } = 4;

    /// <summary>The width of an entry in px.</summary>
    public double EntryWidth { get; set; } = 76;

    /// <summary>Items under the entries in the flyout (Formatvorlage erstellen …), or empty.</summary>
    public List<MatRibbonItem> Menu { get; } = [];

    /// <summary>What happens with an entry that was chosen (its id).</summary>
    public Action<string>? Chosen { get; set; }

    /// <inheritdoc />
    public override IEnumerable<MatRibbonItem> Children => Menu;
}

/// <summary>A thin line between items of a group.</summary>
public sealed class MatRibbonSeparator : MatRibbonItem
{
    private static int _count;

    /// <summary>A separator (its id is made when none is given).</summary>
    public MatRibbonSeparator(string? id = null)
        : base(id ?? "separator-" + Interlocked.Increment(ref _count).ToString(System.Globalization.CultureInfo.InvariantCulture), string.Empty)
    {
    }
}

/// <summary>Items one under the other in a column of their own (up to three medium or small items: the rows of the group Schriftart).</summary>
public sealed class MatRibbonStack : MatRibbonItem
{
    /// <summary>A stack with an id and its items, from the top.</summary>
    public MatRibbonStack(string id, params MatRibbonItem[] items)
        : base(id, string.Empty)
    {
        Items = [.. items];
    }

    /// <summary>The items, from the top.</summary>
    public List<MatRibbonItem> Items { get; }

    /// <inheritdoc />
    public override IEnumerable<MatRibbonItem> Children => Items;
}

/// <summary>Small items side by side in one row (Fett, Kursiv, Unterstrichen): their icons alone, close together.</summary>
public sealed class MatRibbonRow : MatRibbonItem
{
    /// <summary>A row with an id and its items, from the left.</summary>
    public MatRibbonRow(string id, params MatRibbonItem[] items)
        : base(id, string.Empty)
    {
        Items = [.. items];
    }

    /// <summary>The items, from the left.</summary>
    public List<MatRibbonItem> Items { get; }

    /// <inheritdoc />
    public override IEnumerable<MatRibbonItem> Children => Items;
}

/// <summary>A place for a control of the program's own (a preview, a field the ribbon does not know); the front end asks the program for its content by the id.</summary>
public sealed class MatRibbonCustom : MatRibbonItem
{
    /// <summary>A place with an id and a label (the name for a screen reader).</summary>
    public MatRibbonCustom(string id, string label)
        : base(id, label)
    {
    }

    /// <summary>True takes the full height of the group (a column of its own), false one row.</summary>
    public bool IsTall { get; set; }
}

/// <summary>
/// A group of a tab (Zwischenablage, Schriftart, Absatz): its items and its label under them. <see cref="Icon"/> is the icon of the button the group becomes in a narrow window;
/// <see cref="ShrinkOrder"/> says when it shrinks (lower first; groups of the same order shrink from the right). <see cref="Launcher"/> is the small button at its label that opens the dialog of the group.
/// </summary>
public sealed class MatRibbonGroup(string id, string label)
{
    /// <summary>The id, unique in the ribbon.</summary>
    public string Id { get; } = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A group needs an id.", nameof(id)) : id;

    /// <summary>The label under the group.</summary>
    public string Label { get; set; } = label ?? string.Empty;

    /// <summary>The icon of the button the group becomes in a narrow window (or the icon of its first item).</summary>
    public string? Icon { get; set; }

    /// <summary>The key tip of that button, or null.</summary>
    public string? KeyTip { get; set; }

    /// <summary>When the group shrinks in a narrowing window: lower first, the same order from the right.</summary>
    public int ShrinkOrder { get; set; }

    /// <summary>What the small button at the label opens (the dialog of the group: Schriftart …), or null for none.</summary>
    public Action? Launcher { get; set; }

    /// <summary>The items, in their order.</summary>
    public List<MatRibbonItem> Items { get; } = [];

    /// <summary>Adds items and returns the group.</summary>
    public MatRibbonGroup Add(params MatRibbonItem[] items)
    {
        Items.AddRange(items);
        return this;
    }
}

/// <summary>A tab of a ribbon (Start, Einfügen, Layout); a contextual tab has a <see cref="Context"/> and is shown only while its context is (a table is selected: Tabelle).</summary>
public sealed class MatRibbonTab(string id, string label)
{
    /// <summary>The id, unique in the ribbon.</summary>
    public string Id { get; } = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A tab needs an id.", nameof(id)) : id;

    /// <summary>The label.</summary>
    public string Label { get; set; } = label ?? string.Empty;

    /// <summary>The key tip after Alt, or null: then the first free letter of the label.</summary>
    public string? KeyTip { get; set; }

    /// <summary>The context this tab belongs to (<c>table</c>), or null for a tab that is always there.</summary>
    public string? Context { get; set; }

    /// <summary>The groups, in their order.</summary>
    public List<MatRibbonGroup> Groups { get; } = [];

    /// <summary>Adds groups and returns the tab.</summary>
    public MatRibbonTab Add(params MatRibbonGroup[] groups)
    {
        Groups.AddRange(groups);
        return this;
    }
}

/// <summary>
/// The definition of a ribbon, free of any user interface: the tabs, their groups, their items, the items of the quick access, and the contexts that are shown. A front end draws it
/// (<c>MatRibbon</c>) and acts through <see cref="Invoke"/>, <see cref="Choose"/>, <see cref="Commit"/> and <see cref="ChangeValue"/>, which run the item's callback and raise
/// <see cref="Invoked"/>: a program may route every command by its id there instead. Change the state on the items (<see cref="SetEnabled"/>, <see cref="SetChecked"/> or the item itself);
/// after a change of the structure call <see cref="Reindex"/>.
/// <para>
/// An id is unique inside the ribbon (<see cref="Problems"/> names one that is there twice). The quick access shares ids with the ribbon on purpose: its Speichern is the ribbon's Speichern,
/// as the same item or as an item of its own with the same id (<see cref="QuickAccess"/>). The state setters reach every item with the id, in the ribbon and in the quick access;
/// <see cref="Find"/> and <see cref="Get{T}"/> return one of them (the ribbon's first), <see cref="FindAll"/> all of them.
/// </para>
/// </summary>
public sealed class MatRibbonDefinition
{
    private readonly HashSet<string> _contexts = new(StringComparer.Ordinal);
    private Dictionary<string, List<MatRibbonItem>>? _index;

    /// <summary>
    /// The label of the first tab, which opens the backstage (<c>Datei</c>), or null: then the front end shows its own word in the language of the program (<c>MatRibbon</c>: <c>Datei</c> or
    /// <c>File</c>). The definition itself knows no language.
    /// </summary>
    public string? FileLabel { get; set; }

    /// <summary>The key tip of that tab, or null: then the front end gives its own (<c>MatRibbon</c>: <c>D</c> in German, <c>F</c> in English).</summary>
    public string? FileKeyTip { get; set; }

    /// <summary>The tabs, in their order (contextual tabs after the others).</summary>
    public List<MatRibbonTab> Tabs { get; } = [];

    /// <summary>
    /// The items of the quick access (the buttons of the title bar: Speichern, Rückgängig), in their order: the same items as in the ribbon or items of their own that share an id with one of
    /// the ribbon. <c>MatQuickAccess</c> adds its items here; after changing the list by hand call <see cref="Reindex"/>.
    /// </summary>
    public List<MatRibbonItem> QuickAccess { get; } = [];

    /// <summary>The contexts shown now.</summary>
    public IReadOnlyCollection<string> ActiveContexts => _contexts;

    /// <summary>A context was shown or hidden.</summary>
    public event EventHandler? ContextsChanged;

    /// <summary>An item was used (after its own callback ran): the program may route commands by <see cref="MatRibbonItem.Id"/> here.</summary>
    public event EventHandler<MatRibbonItem>? Invoked;

    /// <summary>Adds tabs and returns the definition.</summary>
    public MatRibbonDefinition Add(params MatRibbonTab[] tabs)
    {
        Tabs.AddRange(tabs);
        _index = null;
        return this;
    }

    /// <summary>The tabs shown now: those without a context and those of an active context.</summary>
    public IEnumerable<MatRibbonTab> VisibleTabs => Tabs.Where(t => t.Context is null || _contexts.Contains(t.Context));

    /// <summary>Shows the tabs of a context (a table was selected).</summary>
    public void ShowContext(string context)
    {
        if (_contexts.Add(context))
        {
            ContextsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Hides the tabs of a context.</summary>
    public void HideContext(string context)
    {
        if (_contexts.Remove(context))
        {
            ContextsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Every item of the ribbon, those inside stacks, rows and menus included (not those of the quick access that are not in the ribbon).</summary>
    public IEnumerable<MatRibbonItem> AllItems() => Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Items).SelectMany(Walk);

    /// <summary>
    /// The item with this id, or null. When several items have it (the ribbon's and the quick access's of the same command), the first of the ribbon in the order of its tabs, else the first
    /// of the quick access.
    /// </summary>
    public MatRibbonItem? Find(string id) => Index().TryGetValue(id, out var items) ? items[0] : null;

    /// <summary>
    /// The item with this id as a <typeparamref name="T"/>; throws when there is none. When several items have the id, the first one that is a <typeparamref name="T"/> (in the order of
    /// <see cref="Find"/>).
    /// </summary>
    public T Get<T>(string id)
        where T : MatRibbonItem => FindAll(id).OfType<T>().FirstOrDefault() ?? throw new KeyNotFoundException($"The ribbon has no {typeof(T).Name} {id}.");

    /// <summary>Every item with this id: those of the ribbon in the order of its tabs, then those of the quick access (each item once); empty when there is none.</summary>
    public IReadOnlyList<MatRibbonItem> FindAll(string id) => Index().TryGetValue(id, out var items) ? items : [];

    /// <summary>Builds the index of the ids again after items were added or removed (in the ribbon or in <see cref="QuickAccess"/>).</summary>
    public void Reindex() => _index = null;

    /// <summary>Turns every item with this id on or off (the ribbon's and the quick access's); throws when there is none.</summary>
    public void SetEnabled(string id, bool enabled) => SetAll<MatRibbonItem>(id, i => i.IsEnabled = enabled);

    /// <summary>Sets the state of every toggle with this id; throws when there is none.</summary>
    public void SetChecked(string id, bool isChecked) => SetAll<MatRibbonToggle>(id, t => t.IsChecked = isChecked);

    /// <summary>Sets the value of every combo box with this id; throws when there is none.</summary>
    public void SetText(string id, string? text) => SetAll<MatRibbonComboBox>(id, c => c.Text = text);

    /// <summary>Sets the value of every number field with this id; throws when there is none.</summary>
    public void SetValue(string id, double value) => SetAll<MatRibbonNumber>(id, n => n.Value = value);

    /// <summary>
    /// What a press of the item does: a button runs its command, a toggle changes its state (one of a choice is checked and the others not; the checked one of a choice stays checked) and
    /// tells <see cref="MatRibbonToggle.Toggled"/>, a colour button applies its colour; then <see cref="Invoked"/>. A turned-off item does nothing. A new state (checked, a colour, a value,
    /// an entry) goes to every item with the same id, so that the ribbon and the quick access stay alike.
    /// </summary>
    public void Invoke(MatRibbonItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsEnabled)
        {
            return;
        }

        switch (item)
        {
            case MatRibbonToggle toggle:
                Check(toggle, toggle.GroupName is not null || !toggle.IsChecked);
                toggle.Toggled?.Invoke(toggle.IsChecked);
                break;
            case MatRibbonColorPicker picker:
                picker.ColorChosen?.Invoke(picker.Color);
                break;
            case MatRibbonButton button:
                button.Command?.Invoke();
                break;
        }

        Invoked?.Invoke(this, item);
    }

    /// <summary>A colour was chosen in the palette (null: the automatic one): it becomes the colour of the button and is applied.</summary>
    public void ChooseColor(MatRibbonColorPicker picker, string? color)
    {
        ArgumentNullException.ThrowIfNull(picker);
        if (!picker.IsEnabled)
        {
            return;
        }

        picker.Color = color;
        Twins(picker, p => p.Color = color);
        picker.ColorChosen?.Invoke(color);
        Invoked?.Invoke(this, picker);
    }

    /// <summary>An entry of a gallery was chosen.</summary>
    public void Choose(MatRibbonGallery gallery, string entryId)
    {
        ArgumentNullException.ThrowIfNull(gallery);
        if (!gallery.IsEnabled)
        {
            return;
        }

        gallery.SelectedId = entryId;
        Twins(gallery, g => g.SelectedId = entryId);
        gallery.Chosen?.Invoke(entryId);
        Invoked?.Invoke(this, gallery);
    }

    /// <summary>A value of a combo box was chosen or typed and confirmed.</summary>
    public void Commit(MatRibbonComboBox combo, string text)
    {
        ArgumentNullException.ThrowIfNull(combo);
        if (!combo.IsEnabled)
        {
            return;
        }

        combo.Text = text;
        Twins(combo, c => c.Text = text);
        combo.Committed?.Invoke(text);
        Invoked?.Invoke(this, combo);
    }

    /// <summary>A number was set (held to its minimum and maximum).</summary>
    public void ChangeValue(MatRibbonNumber number, double value)
    {
        ArgumentNullException.ThrowIfNull(number);
        if (!number.IsEnabled)
        {
            return;
        }

        number.Value = Math.Clamp(Math.Round(value, number.Decimals), number.Minimum, number.Maximum);
        Twins(number, n => n.Value = number.Value);
        number.Changed?.Invoke(number.Value);
        Invoked?.Invoke(this, number);
    }

    /// <summary>
    /// What is wrong with the definition, as sentences (empty when nothing is): an id twice in the ribbon, an item without a label (but separators, stacks and rows), a tab without groups, a key
    /// tip twice in the same place, a colour of a palette that is not <c>#rrggbb</c>. The quick access sharing an id with the ribbon is meant (<see cref="QuickAccess"/>), not a problem.
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in Tabs.Select(t => t.Id).Concat(Tabs.SelectMany(t => t.Groups).Select(g => g.Id)).Concat(AllItems().Select(i => i.Id)))
        {
            if (!ids.Add(id))
            {
                problems.Add($"The id {id} is there twice.");
            }
        }

        foreach (var tab in Tabs)
        {
            if (tab.Groups.Count == 0)
            {
                problems.Add($"The tab {tab.Id} has no groups.");
            }

            var tips = tab.Groups.SelectMany(g => g.Items).SelectMany(Walk).Where(i => i.KeyTip is not null).GroupBy(i => i.KeyTip!.ToUpperInvariant());
            problems.AddRange(tips.Where(t => t.Count() > 1).Select(t => $"The key tip {t.Key} is there twice on the tab {tab.Id}."));
        }

        foreach (var item in AllItems())
        {
            if (string.IsNullOrWhiteSpace(item.Label) && item is not (MatRibbonSeparator or MatRibbonStack or MatRibbonRow))
            {
                problems.Add($"The item {item.Id} has no label (a screen reader and the tooltip need it).");
            }

            if (item is MatRibbonColorPicker picker)
            {
                problems.AddRange(picker.Palette.Where(c => !IsHexColor(c)).Select(c => $"The colour {c} of {item.Id} is not #rrggbb."));
            }
        }

        var tabTips = Tabs.Where(t => t.KeyTip is not null).GroupBy(t => t.KeyTip!.ToUpperInvariant());
        problems.AddRange(tabTips.Where(t => t.Count() > 1).Select(t => $"The key tip {t.Key} is there twice on the tabs."));
        return problems;
    }

    /// <summary>True for a colour written <c>#rrggbb</c>, the one form of a colour of a document.</summary>
    public static bool IsHexColor(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(char.IsAsciiHexDigit);

    private void Check(MatRibbonToggle toggle, bool isChecked)
    {
        toggle.IsChecked = isChecked;
        Twins(toggle, t => t.IsChecked = isChecked);
        if (!isChecked || toggle.GroupName is null)
        {
            return;
        }

        foreach (var other in Everything().OfType<MatRibbonToggle>().Where(t => t.Id != toggle.Id && t.GroupName == toggle.GroupName))
        {
            other.IsChecked = false;
        }
    }

    /// <summary>The items of the ribbon, then those of the quick access, each once.</summary>
    private IEnumerable<MatRibbonItem> Everything() => AllItems().Concat(QuickAccess.SelectMany(Walk)).Distinct();

    private Dictionary<string, List<MatRibbonItem>> Index() =>
        _index ??= Everything().GroupBy(i => i.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

    private void SetAll<T>(string id, Action<T> set)
        where T : MatRibbonItem
    {
        var items = FindAll(id).OfType<T>().ToList();
        if (items.Count == 0)
        {
            throw new KeyNotFoundException($"The ribbon has no {typeof(T).Name} {id}.");
        }

        items.ForEach(set);
    }

    /// <summary>Gives the other items with the id of <paramref name="item"/> (of its kind) its new state.</summary>
    private void Twins<T>(T item, Action<T> set)
        where T : MatRibbonItem
    {
        foreach (var twin in FindAll(item.Id).OfType<T>().Where(t => t != item))
        {
            set(twin);
        }
    }

    private static IEnumerable<MatRibbonItem> Walk(MatRibbonItem item) => item.Children.SelectMany(Walk).Prepend(item);
}

/// <summary>
/// How the groups of a tab shrink to fit a width (pure, so that any front end shrinks them the same way): in rounds, every group one step (Large → Medium → Small → Collapsed) in the order of
/// <see cref="MatRibbonGroup.ShrinkOrder"/> (lower first, the same order from the right), until the widths fit.
/// </summary>
public static class MatRibbonLayout
{
    /// <summary>
    /// The state of each group for <paramref name="available"/> px. <paramref name="widths"/> has per group its width in each of the four states (Large, Medium, Small, Collapsed);
    /// <paramref name="shrinkOrder"/> per group its <see cref="MatRibbonGroup.ShrinkOrder"/>. When even every group collapsed does not fit, all are collapsed.
    /// </summary>
    public static MatRibbonGroupState[] Fit(IReadOnlyList<double[]> widths, IReadOnlyList<int> shrinkOrder, double available)
    {
        ArgumentNullException.ThrowIfNull(widths);
        ArgumentNullException.ThrowIfNull(shrinkOrder);
        var states = new MatRibbonGroupState[widths.Count];
        double Total() => states.Select((s, i) => widths[i][(int)s]).Sum();

        // lower order first; the same order from the right (the last group first)
        var order = Enumerable.Range(0, widths.Count).OrderBy(i => shrinkOrder[i]).ThenByDescending(i => i).ToList();
        for (var step = MatRibbonGroupState.Medium; step <= MatRibbonGroupState.Collapsed; step++)
        {
            foreach (var i in order)
            {
                if (Total() <= available)
                {
                    return states;
                }

                // a step that makes the group no narrower is taken with the next one (a group of large items only goes from Large to Collapsed)
                states[i] = step;
            }
        }

        return states;
    }
}

/// <summary>The key tips of a ribbon (after Alt): the explicit ones, and for the rest the first letter of the label that is still free, then two letters.</summary>
public static class MatRibbonKeyTips
{
    /// <summary>A key tip per id, for items (or tabs) given in their order as (id, label, explicit tip or null); the explicit ones are taken first.</summary>
    public static IReadOnlyDictionary<string, string> Assign(IEnumerable<(string Id, string Label, string? KeyTip)> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var list = targets.ToList();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, _, tip) in list.Where(t => !string.IsNullOrWhiteSpace(t.KeyTip)))
        {
            var key = tip!.Trim().ToUpperInvariant();
            if (taken.Add(key))
            {
                result[id] = key;
            }
        }

        // a letter of the label first; what is left gets two letters that do not start with a tip of one letter (it would never be reached)
        foreach (var (id, label, _) in list.Where(t => !result.ContainsKey(t.Id)))
        {
            var key = Letters(label).Select(c => c.ToString()).FirstOrDefault(c => Free(taken, c));
            if (key is not null && taken.Add(key))
            {
                result[id] = key;
            }
        }

        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        foreach (var (id, label, _) in list.Where(t => !result.ContainsKey(t.Id)))
        {
            var letters = Letters(label);
            var key = letters.Concat(Alphabet)
                .SelectMany(first => letters.Concat(Alphabet).Concat("123456789").Select(second => $"{first}{second}"))
                .FirstOrDefault(c => Free(taken, c));
            if (key is not null && taken.Add(key))
            {
                result[id] = key;
            }
        }

        return result;
    }

    private static string Letters(string label) => new([.. label.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).Distinct()]);

    /// <summary>A tip is free when it is not taken and no taken tip starts with it or is the start of it.</summary>
    private static bool Free(HashSet<string> taken, string tip) =>
        !taken.Contains(tip) && !taken.Any(t => t.StartsWith(tip, StringComparison.Ordinal) || tip.StartsWith(t, StringComparison.Ordinal));
}
