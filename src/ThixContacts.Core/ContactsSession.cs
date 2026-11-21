using MatCloud.Contract.Contacts;
using ThixOffice.Core.Models;
using ThixOffice.Pim.Model;
using ThixOffice.Pim.Rules;
using ThixOffice.Pim.Store;

namespace ThixContacts.Core;

/// <summary>Which contacts the list shows (Outlook's folder pane): all, the favourites, a group or a category.</summary>
public enum ContactFilterKind
{
    All,
    Favorites,
    Group,
    Category,
}

/// <summary>The filter of the list: its kind and – for a group or a category – which one.</summary>
public sealed record ContactFilter(ContactFilterKind Kind, string? Value = null)
{
    public static ContactFilter All { get; } = new(ContactFilterKind.All);

    public static ContactFilter Favorites { get; } = new(ContactFilterKind.Favorites);
}

/// <summary>The query of the list: search, filter, order.</summary>
public sealed record ContactQuery(string? Search = null, ContactFilter? Filter = null, PersonSort Sort = PersonSort.GivenName);

/// <summary>What an import did.</summary>
public sealed record ContactImportResult(int Imported, int Skipped);

/// <summary>
/// The contacts of one store, for the window of ThixContacts: the persons and groups in memory (built once, then kept in step with every change and
/// with what the sync brings), the query of the list, and every change as one undoable step (<see cref="Undo"/>/<see cref="Redo"/>: the items before and
/// after). The rules (validation, duplicates, merging) are ThixOffice.Pim's – the same as MatCloud's. A refusal is a
/// <see cref="BusinessException"/> naming the field (<c>GivenName</c>, <c>Emails[1]</c>).
/// </summary>
public sealed class ContactsSession : IDisposable
{
    private readonly Dictionary<string, Person> _people = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PersonGroup> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, PersonSort), string> _sortKeys = [];
    private readonly Stack<Change> _undo = new();
    private readonly Stack<Change> _redo = new();
    private readonly object _gate = new();

    public ContactsSession(PimStore store)
    {
        Store = store;
        Reload();
        store.RemoteChanged += OnRemoteChanged;
    }

    /// <summary>Raised after every change, of this computer or from the cloud (from the cloud on a worker thread).</summary>
    public event EventHandler? Changed;

    /// <summary>Raised after a change of this computer (the sync takes it up at once).</summary>
    public event EventHandler? LocalChanged;

    public PimStore Store { get; }

    /// <summary>Every person, in no order.</summary>
    public IReadOnlyCollection<Person> People
    {
        get
        {
            lock (_gate)
            {
                return _people.Values.ToList();
            }
        }
    }

    /// <summary>The groups, by name; their members only the persons that exist.</summary>
    public IReadOnlyList<PersonGroup> Groups
    {
        get
        {
            lock (_gate)
            {
                return _groups.Values
                    .Select(g => g with { Data = g.Data with { Members = g.Data.Members.Where(_people.ContainsKey).Distinct().ToList() } })
                    .OrderBy(g => PersonRules.Fold(g.Name), StringComparer.Ordinal)
                    .ToList();
            }
        }
    }

    /// <summary>The categories of every person, each once, by name.</summary>
    public IReadOnlyList<string> Categories
    {
        get
        {
            lock (_gate)
            {
                return _people.Values.SelectMany(p => p.Data.Categories).GroupBy(c => c.ToLowerInvariant()).Select(g => g.First())
                    .OrderBy(PersonRules.Fold, StringComparer.Ordinal).ToList();
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _people.Count;
            }
        }
    }

    public Person? Find(string? id)
    {
        lock (_gate)
        {
            return id is not null && _people.TryGetValue(id, out var person) ? person : null;
        }
    }

    public PersonGroup? FindGroup(string? id) => Groups.FirstOrDefault(g => g.Id == id);

    /// <summary>The persons the list shows, in its order.</summary>
    public IReadOnlyList<Person> Query(ContactQuery query)
    {
        lock (_gate)
        {
            IEnumerable<Person> people = _people.Values;
            var filter = query.Filter ?? ContactFilter.All;
            switch (filter.Kind)
            {
                case ContactFilterKind.Favorites:
                    people = people.Where(p => p.Data.IsFavorite);
                    break;
                case ContactFilterKind.Group:
                    var members = _groups.TryGetValue(filter.Value ?? "", out var group) ? group.Data.Members.ToHashSet() : [];
                    people = people.Where(p => members.Contains(p.Id));
                    break;
                case ContactFilterKind.Category:
                    people = people.Where(p => p.Data.Categories.Contains(filter.Value ?? "", StringComparer.OrdinalIgnoreCase));
                    break;
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                people = people.Where(p => PersonRules.Matches(p.Data, query.Search));
            }

            return people.OrderBy(p => SortKey(p, query.Sort), StringComparer.Ordinal).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>The letter of the index a person stands under in an order.</summary>
    public static string LetterOf(Person person, PersonSort sort) => PersonRules.IndexLetter(person.Data, sort);

    // ---- changes ----

    /// <summary>A new contact (<paramref name="id"/> null) or a change; answers its id. Checked by the rules; the picture must be in the store.</summary>
    public string Save(string? id, ContactData data)
    {
        var contact = PersonRules.Normalize(data);
        if (PersonRules.Validate(contact).FirstOrDefault() is { } problem)
        {
            throw new BusinessException(problem.Message, problem.Field);
        }

        if (contact.PhotoId.Length > 0 && !Store.HasPhoto(contact.PhotoId))
        {
            throw new BusinessException("Das Bild fehlt. Bitte wählen Sie es noch einmal.", nameof(ContactData.PhotoId));
        }

        var existing = Find(id);
        if (id is not null && existing is null)
        {
            throw new BusinessException("Diesen Kontakt gibt es nicht mehr (er wurde auf einem anderen Gerät gelöscht).");
        }

        if (existing is not null)
        {
            contact = contact with { Extra = existing.Data.Extra }; // what a newer app wrote stays
        }

        var person = new Person(id ?? Guid.NewGuid().ToString(), contact, DateTimeOffset.UtcNow);
        Apply(new Change(existing is null ? "Neuer Kontakt" : "Kontakt bearbeiten", [(existing, person)], []));
        return person.Id;
    }

    /// <summary>Deletes persons (one undoable step: "Rückgängig" brings them back); answers how many.</summary>
    public int Delete(IEnumerable<string> ids)
    {
        var people = ids.Distinct().Select(Find).OfType<Person>().ToList();
        if (people.Count == 0)
        {
            return 0;
        }

        Apply(new Change(people.Count == 1 ? "Kontakt löschen" : "Kontakte löschen", people.Select(p => ((Person?)p, (Person?)null)).ToList(), []));
        return people.Count;
    }

    public void SetFavorite(IEnumerable<string> ids, bool favorite)
    {
        var people = ids.Select(Find).OfType<Person>().Where(p => p.Data.IsFavorite != favorite).ToList();
        Apply(new Change(favorite ? "Zu den Favoriten" : "Aus den Favoriten",
            people.Select(p => ((Person?)p, (Person?)(p with { Data = p.Data with { IsFavorite = favorite } }))).ToList(), []));
    }

    /// <summary>"Kategorisieren": gives the persons the category (<paramref name="add"/>) or takes it away.</summary>
    public void Categorize(IEnumerable<string> ids, string category, bool add)
    {
        var name = category.Trim();
        if (PersonRules.CategoryProblem(name) is { } problem)
        {
            throw new BusinessException(problem, "Category");
        }

        var changes = new List<(Person?, Person?)>();
        foreach (var person in ids.Select(Find).OfType<Person>())
        {
            var has = person.Data.Categories.Contains(name, StringComparer.OrdinalIgnoreCase);
            if (has != add)
            {
                var categories = add ? [.. person.Data.Categories, name] : person.Data.Categories.Where(c => !c.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
                changes.Add((person, person with { Data = person.Data with { Categories = categories } }));
            }
        }

        Apply(new Change("Kategorisieren", changes, []));
    }

    /// <summary>The picture of a contact into the store (JPEG, PNG, GIF, WebP; ≤ 5 MB); answers its id for <see cref="ContactData.PhotoId"/>.</summary>
    public string AddPhoto(byte[] bytes)
    {
        if (bytes.Length > ContactsContract.MaxPhotoBytes)
        {
            throw new BusinessException($"Ein Bild hat höchstens {ContactsContract.MaxPhotoBytes / 1024 / 1024} MB.", nameof(ContactData.PhotoId));
        }

        if (VCard.ImageTypeOf(bytes) is null)
        {
            throw new BusinessException("Das ist kein Bild (JPEG, PNG, GIF oder WebP).", nameof(ContactData.PhotoId));
        }

        var id = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        Store.PutPhoto(id, bytes, isUploaded: false);
        return id;
    }

    public byte[]? Photo(string? id) => string.IsNullOrEmpty(id) ? null : Store.Photo(id);

    // ---- groups ----

    public string CreateGroup(string name)
    {
        var clean = CheckGroupName(name, null);
        var group = new PersonGroup(Guid.NewGuid().ToString(), new ContactGroupData { Name = clean }, DateTimeOffset.UtcNow);
        Apply(new Change("Neue Kontaktgruppe", [], [(null, group)]));
        return group.Id;
    }

    public void RenameGroup(string id, string name)
    {
        var group = FindGroup(id) ?? throw new BusinessException("Diese Gruppe gibt es nicht mehr.");
        var clean = CheckGroupName(name, id);
        Apply(new Change("Gruppe umbenennen", [], [(group, group with { Data = group.Data with { Name = clean } })]));
    }

    public void DeleteGroup(string id)
    {
        var group = FindGroup(id) ?? throw new BusinessException("Diese Gruppe gibt es nicht mehr.");
        Apply(new Change("Gruppe löschen", [], [(group, null)]));
    }

    /// <summary>Adds persons to a group (<paramref name="add"/>) or takes them out; answers how many changed.</summary>
    public int SetMembers(string groupId, IEnumerable<string> ids, bool add)
    {
        var group = FindGroup(groupId) ?? throw new BusinessException("Diese Gruppe gibt es nicht mehr.");
        var members = group.Data.Members.ToList();
        var changed = 0;
        foreach (var id in ids.Distinct())
        {
            if (add && Find(id) is not null && !members.Contains(id))
            {
                members.Add(id);
                changed++;
            }
            else if (!add && members.Remove(id))
            {
                changed++;
            }
        }

        if (members.Count > ContactsContract.MaxGroupMembers)
        {
            throw new BusinessException($"Eine Gruppe hat höchstens {ContactsContract.MaxGroupMembers} Mitglieder.");
        }

        if (changed > 0)
        {
            Apply(new Change(add ? "Zur Gruppe hinzufügen" : "Aus der Gruppe entfernen", [], [(group, group with { Data = group.Data with { Members = members } })]));
        }

        return changed;
    }

    /// <summary>The e-mail addresses of a group's members (the hook of "E-Mail an Gruppe", for ThixMail later: a mailto: until then).</summary>
    public IReadOnlyList<string> GroupAddresses(string groupId)
        => (FindGroup(groupId)?.Data.Members ?? []).Select(Find).OfType<Person>().Select(p => p.Email).Where(e => e.Length > 0).Distinct().ToList();

    private string CheckGroupName(string name, string? except)
    {
        var clean = (name ?? "").Trim();
        if (PersonRules.CategoryProblem(clean) is { } problem)
        {
            throw new BusinessException(problem, "Name");
        }

        if (Groups.Any(g => g.Id != except && g.Name.Equals(clean, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BusinessException("Eine Gruppe mit diesem Namen gibt es schon.", "Name");
        }

        return clean;
    }

    // ---- duplicates ----

    /// <summary>The sets of duplicates, each as its persons (by name).</summary>
    public IReadOnlyList<IReadOnlyList<Person>> Duplicates()
    {
        var people = Query(new ContactQuery());
        var byId = people.ToDictionary(p => p.Id);
        return PersonDuplicates.Find(people.Select(p => (p.Id, p.Data)).ToList())
            .Select(set => (IReadOnlyList<Person>)set.Select(id => byId[id]).ToList()).ToList();
    }

    /// <summary>
    /// Merges persons into the first of <paramref name="ids"/>: per field the value of the person <paramref name="choices"/> names (by default the first
    /// that has one), lists joined; the others are deleted and their groups name the first. One undoable step; answers the id kept.
    /// </summary>
    public string Merge(IReadOnlyList<string> ids, IReadOnlyDictionary<string, string>? choices = null)
    {
        var people = ids.Distinct().Select(Find).OfType<Person>().ToList();
        if (people.Count < 2)
        {
            throw new BusinessException("Bitte mindestens zwei Kontakte zum Zusammenführen wählen.");
        }

        var byIndex = choices?.Where(c => people.Any(p => p.Id == c.Value)).ToDictionary(c => c.Key, c => people.FindIndex(p => p.Id == c.Value));
        var merged = PersonDuplicates.Merge(people.Select(p => p.Data).ToList(), byIndex);
        var kept = people[0];
        var others = people.Skip(1).Select(p => p.Id).ToHashSet();
        var personChanges = new List<(Person?, Person?)> { (kept, kept with { Data = merged with { Extra = kept.Data.Extra }, UpdatedAt = DateTimeOffset.UtcNow }) };
        personChanges.AddRange(people.Skip(1).Select(p => ((Person?)p, (Person?)null)));
        var groupChanges = Groups.Where(g => g.Data.Members.Any(others.Contains))
            .Select(g => ((PersonGroup?)FindRawGroup(g.Id), (PersonGroup?)(FindRawGroup(g.Id)! with
            {
                Data = g.Data with { Members = FindRawGroup(g.Id)!.Data.Members.Select(m => others.Contains(m) ? kept.Id : m).Distinct().ToList() },
            }))).ToList();
        Apply(new Change("Zusammenführen", personChanges, groupChanges));
        return kept.Id;
    }

    // ---- import ----

    /// <summary>
    /// Contacts read from a file (vCard, CSV) with their pictures; <paramref name="skipDuplicates"/> leaves out one that would be a duplicate of a person
    /// here (or of one imported before it). A value the rules refuse goes, the contact stays. One undoable step.
    /// </summary>
    public ContactImportResult Import(IReadOnlyList<ImportedPerson> contacts, bool skipDuplicates)
    {
        var known = skipDuplicates ? People.SelectMany(p => PersonDuplicates.Keys(p.Data)).ToHashSet(StringComparer.Ordinal) : [];
        var added = new List<(Person?, Person?)>();
        var skipped = 0;
        foreach (var imported in contacts)
        {
            var contact = Repair(PersonRules.Normalize(imported.Data));
            if (PersonRules.Validate(contact).Count > 0)
            {
                skipped++;
                continue;
            }

            var keys = PersonDuplicates.Keys(contact).ToList();
            if (skipDuplicates && keys.Any(known.Contains))
            {
                skipped++;
                continue;
            }

            foreach (var key in keys)
            {
                known.Add(key);
            }

            contact = imported.Photo is { Length: > 0 and <= ContactsContract.MaxPhotoBytes } photo && VCard.ImageTypeOf(photo) is not null
                ? contact with { PhotoId = AddPhoto(photo) }
                : contact with { PhotoId = string.Empty };
            added.Add((null, new Person(Guid.NewGuid().ToString(), contact, DateTimeOffset.UtcNow)));
        }

        if (added.Count > 0)
        {
            Apply(new Change(added.Count == 1 ? "Kontakt importieren" : "Kontakte importieren", added, []));
        }

        return new ContactImportResult(added.Count, skipped);
    }

    /// <summary>Leaves out the values the rules refuse (an import should not lose a contact over one broken e-mail address).</summary>
    internal static ContactData Repair(ContactData contact)
    {
        static string Cut(string text) => text.Length > ContactsContract.MaxNameLength ? text[..ContactsContract.MaxNameLength] : text;
        return contact with
        {
            Emails = contact.Emails.Where(e => PersonRules.IsEmail(e.Value) && e.Value.Length <= ContactsContract.MaxValueLength).Take(ContactsContract.MaxValues).ToList(),
            Phones = contact.Phones.Where(p => PersonRules.IsPhone(p.Value) && p.Value.Length <= ContactsContract.MaxValueLength).Take(ContactsContract.MaxValues).ToList(),
            Urls = contact.Urls.Where(u => PersonRules.IsUrl(u.Value) && u.Value.Length <= ContactsContract.MaxValueLength).Take(ContactsContract.MaxValues).ToList(),
            Addresses = contact.Addresses.Take(ContactsContract.MaxValues).ToList(),
            Categories = contact.Categories.Where(c => PersonRules.CategoryProblem(c) is null).Take(ContactsContract.MaxValues).ToList(),
            Birthday = ContactsContract.IsValidDate(contact.Birthday) ? contact.Birthday : string.Empty,
            Anniversary = ContactsContract.IsValidDate(contact.Anniversary) ? contact.Anniversary : string.Empty,
            Notes = contact.Notes.Length > ContactsContract.MaxNotesLength ? contact.Notes[..ContactsContract.MaxNotesLength] : contact.Notes,
            Prefix = Cut(contact.Prefix),
            GivenName = Cut(contact.GivenName),
            MiddleName = Cut(contact.MiddleName),
            FamilyName = Cut(contact.FamilyName),
            Suffix = Cut(contact.Suffix),
            Nickname = Cut(contact.Nickname),
            DisplayName = Cut(contact.DisplayName),
            Company = Cut(contact.Company),
            Department = Cut(contact.Department),
            JobTitle = Cut(contact.JobTitle),
        };
    }

    /// <summary>The persons to write into a file (the ids given, or all), by family name, with their pictures.</summary>
    public IReadOnlyList<ExportedPerson> Export(IReadOnlyCollection<string>? ids = null)
        => People.Where(p => ids is null || ids.Count == 0 || ids.Contains(p.Id))
            .OrderBy(p => PersonRules.SortKey(p.Data, PersonSort.FamilyName), StringComparer.Ordinal)
            .Select(p => new ExportedPerson(p.Id, p.Data, Photo(p.Data.PhotoId)))
            .ToList();

    // ---- undo ----

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>What "Rückgängig" would undo ("Kontakte löschen"), or null.</summary>
    public string? UndoLabel => _undo.Count > 0 ? _undo.Peek().Label : null;

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        var change = _undo.Pop();
        Write(change.Reversed());
        _redo.Push(change);
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        var change = _redo.Pop();
        Write(change);
        _undo.Push(change);
    }

    public void Dispose() => Store.RemoteChanged -= OnRemoteChanged;

    // ---- helpers ----

    /// <summary>A change of this computer: written, remembered for undo (the redo stack is gone).</summary>
    private void Apply(Change change)
    {
        if (change.People.Count == 0 && change.Groups.Count == 0)
        {
            return;
        }

        Write(change);
        _undo.Push(change);
        _redo.Clear();
    }

    private void Write(Change change)
    {
        var now = DateTimeOffset.UtcNow;
        var people = change.People.Where(c => c.After is not null).Select(c => c.After! with { UpdatedAt = now }).ToList();
        var deletedPeople = change.People.Where(c => c.After is null && c.Before is not null).Select(c => c.Before!.Id).ToList();
        var groups = change.Groups.Where(c => c.After is not null).Select(c => c.After! with { UpdatedAt = now }).ToList();
        var deletedGroups = change.Groups.Where(c => c.After is null && c.Before is not null).Select(c => c.Before!.Id).ToList();
        PersonItems.Write(Store, people, groups, deletedPeople, deletedGroups);
        lock (_gate)
        {
            foreach (var person in people)
            {
                _people[person.Id] = person;
                ForgetSortKeys(person.Id);
            }

            foreach (var id in deletedPeople)
            {
                _people.Remove(id);
                ForgetSortKeys(id);
            }

            foreach (var group in groups)
            {
                _groups[group.Id] = group;
            }

            foreach (var id in deletedGroups)
            {
                _groups.Remove(id);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        LocalChanged?.Invoke(this, EventArgs.Empty);
    }

    private PersonGroup? FindRawGroup(string id)
    {
        lock (_gate)
        {
            return _groups.GetValueOrDefault(id);
        }
    }

    private void Reload()
    {
        var people = PersonItems.People(Store);
        var groups = PersonItems.Groups(Store);
        lock (_gate)
        {
            _people.Clear();
            _groups.Clear();
            _sortKeys.Clear();
            foreach (var person in people)
            {
                _people[person.Id] = person;
            }

            foreach (var group in groups)
            {
                _groups[group.Id] = group;
            }
        }
    }

    private void OnRemoteChanged(object? sender, IReadOnlyCollection<string> collections)
    {
        Reload();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private string SortKey(Person person, PersonSort sort)
    {
        if (!_sortKeys.TryGetValue((person.Id, sort), out var key))
        {
            key = PersonRules.SortKey(person.Data, sort);
            _sortKeys[(person.Id, sort)] = key;
        }

        return key;
    }

    private void ForgetSortKeys(string id)
    {
        foreach (var sort in Enum.GetValues<PersonSort>())
        {
            _sortKeys.Remove((id, sort));
        }
    }

    /// <summary>One undoable step: the persons and groups before and after (null: did not exist / deleted).</summary>
    private sealed record Change(string Label, IReadOnlyList<(Person? Before, Person? After)> People, IReadOnlyList<(PersonGroup? Before, PersonGroup? After)> Groups)
    {
        public Change Reversed() => new(Label, People.Select(c => (c.After, c.Before)).ToList(), Groups.Select(c => (c.After, c.Before)).ToList());
    }
}
