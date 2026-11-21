using ThixContacts.Core;
using ThixContacts.Dialogs;
using ThixContacts.Pages;
using ThixOffice.Core.Business;
using ThixOffice.Core.Commands;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;
using ThixOffice.Pim.Rules;

namespace ThixContacts.Editor;

/// <summary>
/// ThixContacts' commands – what the ribbon, the search "Was möchten Sie tun?", the keys and the context menu run. Keys like Outlook's people: Strg+N new
/// contact, Strg+Umschalt+L new group, F2 or Enter edit, Entf delete (with "Rückgängig" in the message bar), Strg+E / Strg+F search, Strg+A all, F9
/// sync now, Strg+P print. Every change goes through the session (one undoable step each); the dialogs only collect what to do.
/// </summary>
public static class ContactsCommands
{
    public static void Add(OfficeWindow window)
    {
        ContactsEditor Editor() => (ContactsEditor)window.Editor;
        bool HasOne() => window.Editor is ContactsEditor { Current: not null };
        bool HasSome() => window.Editor is ContactsEditor editor && editor.Selected.Count > 0;
        var commands = window.Commands;

        commands.Add(new OfficeCommand(ContactsCommandIds.New, "Neuer Kontakt", () => NewAsync(window, Editor()))
        {
            Icon = "plus",
            Shortcut = "Ctrl+N",
            Description = "Legt einen neuen Kontakt an.",
            Keywords = ["neu", "kontakt", "person", "anlegen", "erstellen", "visitenkarte"],
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.NewGroup, "Neue Kontaktgruppe", () => NewGroupAsync(window, Editor()))
        {
            Icon = "users",
            Shortcut = "Ctrl+Shift+L",
            Description = "Legt eine Gruppe von Kontakten an, etwa „Team“ oder „Familie“.",
            Keywords = ["gruppe", "verteiler", "liste", "team"],
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.Edit, "Bearbeiten", () => EditAsync(window, Editor()))
        {
            Icon = "edit",
            Shortcut = "F2",
            Description = "Öffnet den gewählten Kontakt zum Bearbeiten.",
            Keywords = ["ändern", "öffnen", "kontakt"],
            CanExecute = HasOne,
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.Delete, "Löschen", () => DeleteAsync(window, Editor()))
        {
            Icon = "trash",
            Shortcut = "Delete",
            Description = "Löscht die ausgewählten Kontakte – „Rückgängig“ holt sie zurück.",
            Keywords = ["entfernen", "kontakt"],
            CanExecute = HasSome,
            YieldsToTextField = true,
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.Favorite, "Favorit", () =>
        {
            var editor = Editor();
            var on = !(editor.Current?.Data.IsFavorite ?? false);
            editor.Session.SetFavorite(editor.Selected.Select(p => p.Id), on);
        })
        {
            Icon = "star",
            Description = "Nimmt die ausgewählten Kontakte in die Favoriten auf – oder heraus.",
            Keywords = ["favoriten", "stern", "wichtig", "anheften"],
            CanExecute = HasSome,
            IsChecked = () => window.Editor is ContactsEditor { Current.Data.IsFavorite: true },
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.Categorize, "Kategorie zuweisen …", () => CategorizeAsync(window, Editor(), remove: false))
        {
            Icon = "tag",
            Description = "Gibt den ausgewählten Kontakten eine Kategorie (vorhanden oder neu).",
            Keywords = ["kategorisieren", "kategorie", "farbe", "markieren"],
            CanExecute = HasSome,
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.Uncategorize, "Kategorie entfernen …", () => CategorizeAsync(window, Editor(), remove: true))
        {
            Icon = "x",
            Description = "Nimmt den ausgewählten Kontakten eine Kategorie.",
            Keywords = ["kategorie", "entfernen"],
            CanExecute = HasSome,
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.ClearCategories, "Alle Kategorien löschen", () =>
        {
            var editor = Editor();
            foreach (var category in editor.Selected.SelectMany(p => p.Data.Categories).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
            {
                editor.Session.Categorize(editor.Selected.Select(p => p.Id), category, add: false);
            }
        })
        {
            Icon = "clear-format",
            Description = "Nimmt den ausgewählten Kontakten alle Kategorien.",
            Keywords = ["kategorien", "entfernen", "leeren"],
            CanExecute = () => window.Editor is ContactsEditor editor && editor.Selected.Any(p => p.Data.Categories.Count > 0),
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.Email, "E-Mail senden", () =>
        {
            var addresses = Editor().Selected.Select(p => p.Email).Where(e => e.Length > 0).Distinct().ToList();
            if (addresses.Count > 0)
            {
                Ui.OpenExternal("mailto:" + string.Join(",", addresses));
            }
        })
        {
            Icon = "mail",
            Description = "Schreibt den ausgewählten Kontakten eine E-Mail (mit dem E-Mail-Programm des Systems).",
            Keywords = ["mail", "schreiben", "nachricht", "senden"],
            CanExecute = () => window.Editor is ContactsEditor editor && editor.Selected.Any(p => p.Email.Length > 0),
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.Search, "Personen suchen", () => Editor().FocusSearch())
        {
            Icon = "search",
            Shortcut = "Ctrl+E",
            Description = "Sucht in Namen, E-Mail-Adressen, Telefonnummern, Firmen und Kategorien.",
            Keywords = ["suchen", "finden", "filter"],
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.Find, "Suchen", () => Editor().FocusSearch())
        {
            Icon = "search",
            Shortcut = "Ctrl+F",
            IsSearchable = false,
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.SelectAll, "Alle auswählen", () => Editor().SelectAll())
        {
            Icon = "check",
            Shortcut = "Ctrl+A",
            Keywords = ["markieren", "alles"],
            YieldsToTextField = true,
        });
        foreach (var (id, label, sort) in new[]
                 {
                     (ContactsCommandIds.SortGiven, "Vorname", PersonSort.GivenName),
                     (ContactsCommandIds.SortFamily, "Nachname", PersonSort.FamilyName),
                     (ContactsCommandIds.SortCompany, "Firma", PersonSort.Company),
                 })
        {
            commands.Add(new OfficeCommand(id, label, () => Editor().SetSort(sort))
            {
                Icon = "sort-asc",
                Description = $"Sortiert die Liste nach {label}.",
                Keywords = ["sortieren", "reihenfolge", label.ToLowerInvariant()],
                IsChecked = () => window.Editor is ContactsEditor editor && editor.Sort == sort,
            });
        }

        commands.Add(new OfficeCommand(ContactsCommandIds.ShowAll, "Alle Kontakte", () => Editor().SetFilter(ContactFilter.All))
        {
            Icon = "users",
            Keywords = ["alle", "ordner"],
            IsChecked = () => window.Editor is ContactsEditor { Filter.Kind: ContactFilterKind.All },
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.ShowFavorites, "Favoriten", () => Editor().SetFilter(ContactFilter.Favorites))
        {
            Icon = "star",
            Keywords = ["favoriten", "stern", "ordner"],
            IsChecked = () => window.Editor is ContactsEditor { Filter.Kind: ContactFilterKind.Favorites },
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.ShowCard, "Lesebereich", () => Editor().ToggleCard())
        {
            Icon = "sidebar",
            Description = "Zeigt die Karte des gewählten Kontakts neben der Liste – oder nur die Liste.",
            Keywords = ["karte", "vorschau", "lesebereich", "details"],
            IsChecked = () => window.Editor is ContactsEditor { ShowsCard: true },
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.ShowIndex, "Alphabet", () => Editor().ToggleIndex())
        {
            Icon = "list",
            Description = "Zeigt die Buchstaben neben der Liste: ein Klick springt zum ersten Kontakt.",
            Keywords = ["buchstaben", "index", "springen"],
            IsChecked = () => window.Editor is ContactsEditor { ShowsIndex: true },
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.ShowFolders, "Ordnerbereich", () => Editor().ToggleFolders())
        {
            Icon = "folder",
            Description = "Zeigt links die Ordner: alle Kontakte, Favoriten, Gruppen, Kategorien.",
            Keywords = ["ordner", "gruppen", "navigation"],
            IsChecked = () => window.Editor is ContactsEditor { ShowsFolders: true },
        });

        // groups
        bool InGroup() => window.Editor is ContactsEditor { CurrentGroup: not null };
        commands.Add(new OfficeCommand(ContactsCommandIds.RenameGroup, "Gruppe umbenennen …", () => RenameGroupAsync(window, Editor()))
        {
            Icon = "edit",
            Description = "Gibt der gewählten Kontaktgruppe einen neuen Namen.",
            Keywords = ["gruppe", "umbenennen", "name"],
            CanExecute = InGroup,
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.DeleteGroup, "Gruppe löschen", () => DeleteGroupAsync(window, Editor()))
        {
            Icon = "trash",
            Description = "Löscht die gewählte Kontaktgruppe – ihre Kontakte bleiben.",
            Keywords = ["gruppe", "löschen", "entfernen"],
            CanExecute = InGroup,
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.AddMembers, "Mitglieder hinzufügen …", () => AddMembersAsync(window, Editor()))
        {
            Icon = "plus",
            Description = "Nimmt Kontakte in eine Gruppe auf – die ausgewählten oder welche aus einer Liste.",
            Keywords = ["gruppe", "mitglied", "hinzufügen", "aufnehmen"],
            CanExecute = () => window.Editor is ContactsEditor editor && editor.Session.Groups.Count > 0,
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.RemoveMembers, "Aus der Gruppe entfernen", () =>
        {
            var editor = Editor();
            editor.Session.SetMembers(editor.CurrentGroup!.Id, editor.Selected.Select(p => p.Id), add: false);
        })
        {
            Icon = "minus",
            Description = "Nimmt die ausgewählten Kontakte aus der gewählten Gruppe (sie bleiben in Ihren Kontakten).",
            Keywords = ["gruppe", "mitglied", "entfernen"],
            CanExecute = () => InGroup() && HasSome(),
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.EmailGroup, "E-Mail an Gruppe", () =>
        {
            var editor = Editor();
            var addresses = editor.Session.GroupAddresses(editor.CurrentGroup!.Id);
            if (addresses.Count == 0)
            {
                throw new BusinessException("Kein Mitglied dieser Gruppe hat eine E-Mail-Adresse.");
            }

            Ui.OpenExternal("mailto:" + string.Join(",", addresses));
        })
        {
            Icon = "mail",
            Description = "Schreibt allen Mitgliedern der gewählten Gruppe eine E-Mail.",
            Keywords = ["gruppe", "mail", "verteiler", "rundmail"],
            CanExecute = InGroup,
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.Duplicates, "Dubletten suchen …", () => DuplicatesAsync(window, Editor()))
        {
            Icon = "copy",
            Description = "Findet Kontakte mit demselben Namen, derselben E-Mail-Adresse oder Telefonnummer und führt sie zusammen.",
            Keywords = ["doppelt", "dubletten", "zusammenführen", "aufräumen", "duplikate"],
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.Merge, "Zusammenführen …", () => MergeAsync(window, Editor(), Editor().Selected))
        {
            Icon = "layers",
            Description = "Führt die ausgewählten Kontakte zu einem zusammen, Feld für Feld.",
            Keywords = ["zusammenführen", "vereinen", "dubletten"],
            CanExecute = () => window.Editor is ContactsEditor editor && editor.Selected.Count > 1,
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.Import, "Importieren …", () => ImportAsync(window, Editor()))
        {
            Icon = "upload",
            Description = "Importiert Kontakte aus einer vCard-Datei (Outlook, Apple, Google) oder einer CSV-Datei.",
            Keywords = ["importieren", "vcard", "vcf", "csv", "outlook", "übernehmen", "einlesen"],
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.Export, "Exportieren …", () => ExportAsync(window, Editor()))
        {
            Icon = "download",
            Description = "Speichert Kontakte als vCard- oder CSV-Datei.",
            Keywords = ["exportieren", "vcard", "vcf", "csv", "sichern", "weitergeben"],
            CanExecute = () => window.Editor is ContactsEditor editor && editor.Session.Count > 0,
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.Sync, "Jetzt synchronisieren", () => SyncAsync(window, Editor()))
        {
            Icon = "refresh",
            Shortcut = "F9",
            Description = "Gleicht die Kontakte jetzt mit der ThixCloud ab.",
            Keywords = ["synchronisieren", "sync", "cloud", "abgleichen", "aktualisieren"],
            CanExecute = () => window.Editor is ContactsEditor { Document.IsSyncing: true },
        });
        commands.Add(new OfficeCommand(ContactsCommandIds.Print, "Drucken", () => window.ShowBackstage(ContactsPrintPage.PageId))
        {
            Icon = "print",
            Shortcut = "Ctrl+P",
            Description = "Druckt die Kontakte als Liste oder als Karten – oder speichert sie als PDF.",
            Keywords = ["drucken", "pdf", "liste", "karten", "papier"],
        });
    }

    // ---- what the commands do ----

    public static async Task NewAsync(OfficeWindow window, ContactsEditor editor)
    {
        var dialog = new ContactDialog(editor.Session, null, editor.CurrentGroup?.Id);
        if (await dialog.ShowAsync(window) && dialog.SavedId is { } id)
        {
            editor.Refresh();
            editor.Select(id);
        }
    }

    public static async Task EditAsync(OfficeWindow window, ContactsEditor editor)
    {
        if (editor.Current is not { } person)
        {
            return;
        }

        var dialog = new ContactDialog(editor.Session, person);
        if (await dialog.ShowAsync(window))
        {
            editor.Refresh();
            editor.Select(person.Id);
        }
    }

    public static Task DeleteAsync(OfficeWindow window, ContactsEditor editor)
    {
        var people = editor.Selected;
        var count = editor.Session.Delete(people.Select(p => p.Id));
        if (count > 0)
        {
            var text = count == 1 ? $"„{people[0].Name}“ wurde gelöscht." : $"{count} Kontakte wurden gelöscht.";
            window.ShowMessage(text, InfoSeverity.Info, isClosable: true, "Rückgängig", () =>
            {
                editor.Session.Undo();
                window.ShowMessage(null);
            });
        }

        return Task.CompletedTask;
    }

    public static async Task NewGroupAsync(OfficeWindow window, ContactsEditor editor)
    {
        string? error = null;
        var name = "";
        while (true)
        {
            var answer = await TextInputDialog.AskAsync(window, "Neue Kontaktgruppe", "Name", name, "Anlegen", error);
            if (answer is null)
            {
                return;
            }

            try
            {
                var id = editor.Session.CreateGroup(answer);
                if (editor.Selected.Count > 0)
                {
                    editor.Session.SetMembers(id, editor.Selected.Select(p => p.Id), add: true);
                }

                editor.SetFilter(new ContactFilter(ContactFilterKind.Group, id));
                return;
            }
            catch (BusinessException exception)
            {
                name = answer;
                error = exception.Message;
            }
        }
    }

    private static async Task RenameGroupAsync(OfficeWindow window, ContactsEditor editor)
    {
        var group = editor.CurrentGroup!;
        string? error = null;
        var name = group.Name;
        while (true)
        {
            var answer = await TextInputDialog.AskAsync(window, "Gruppe umbenennen", "Name", name, "Umbenennen", error);
            if (answer is null)
            {
                return;
            }

            try
            {
                editor.Session.RenameGroup(group.Id, answer);
                return;
            }
            catch (BusinessException exception)
            {
                name = answer;
                error = exception.Message;
            }
        }
    }

    private static async Task DeleteGroupAsync(OfficeWindow window, ContactsEditor editor)
    {
        var group = editor.CurrentGroup!;
        if (await QuestionDialog.ConfirmAsync(window, "Gruppe löschen", $"Die Gruppe „{group.Name}“ löschen? Ihre {group.Data.Members.Count} Kontakte bleiben.", "Löschen", isDestructive: true))
        {
            editor.Session.DeleteGroup(group.Id);
            editor.SetFilter(ContactFilter.All);
        }
    }

    private static async Task AddMembersAsync(OfficeWindow window, ContactsEditor editor)
    {
        var group = editor.CurrentGroup;
        if (group is null || editor.Selected.Count > 0 && editor.Filter.Kind != ContactFilterKind.Group)
        {
            // the selection of the list into a group: which one?
            var groups = editor.Session.Groups;
            if (editor.Selected.Count > 0)
            {
                var choice = await QuestionDialog.AskAsync(window, "Zur Gruppe hinzufügen", $"{editor.Selected.Count} Kontakte in welche Gruppe?",
                    [.. groups.Take(6).Select((g, i) => new DialogChoice(g.Name, "user-circle", i == 0 ? "MatButton.Primary" : null, IsDefault: i == 0)), new DialogChoice("Abbrechen", "x", IsCancel: true, IsSeparated: true)]);
                if (choice < 0 || choice >= Math.Min(6, groups.Count))
                {
                    return;
                }

                editor.Session.SetMembers(groups[choice].Id, editor.Selected.Select(p => p.Id), add: true);
                return;
            }

            group = groups[0];
        }

        var dialog = new MembersDialog(editor.Session, group);
        if (await dialog.ShowAsync(window))
        {
            editor.Session.SetMembers(group.Id, dialog.Chosen, add: true);
        }
    }

    private static async Task CategorizeAsync(OfficeWindow window, ContactsEditor editor, bool remove)
    {
        var categories = remove ? editor.Selected.SelectMany(p => p.Data.Categories).Distinct(StringComparer.OrdinalIgnoreCase).ToList() : editor.Session.Categories;
        var dialog = new CategoryDialog(categories, remove);
        if (await dialog.ShowAsync(window))
        {
            editor.Session.Categorize(editor.Selected.Select(p => p.Id), dialog.Category, add: !remove);
        }
    }

    public static async Task DuplicatesAsync(OfficeWindow window, ContactsEditor editor)
    {
        var dialog = new DuplicatesDialog(editor.Session.Duplicates());
        if (await dialog.ShowAsync(window) && dialog.Chosen is { } set)
        {
            await MergeAsync(window, editor, set);
        }
    }

    public static async Task MergeAsync(OfficeWindow window, ContactsEditor editor, IReadOnlyList<ThixOffice.Pim.Model.Person> people)
    {
        var dialog = new MergeDialog(people);
        if (await dialog.ShowAsync(window))
        {
            var kept = editor.Session.Merge(dialog.Order, dialog.Choices);
            editor.Refresh();
            editor.Select(kept);
            window.ShowMessage($"{people.Count} Kontakte wurden zu einem zusammengeführt.", InfoSeverity.Success, isClosable: true, "Rückgängig", () =>
            {
                editor.Session.Undo();
                window.ShowMessage(null);
            });
        }
    }

    /// <summary>"Importieren": a vCard or CSV file chosen by the person.</summary>
    public static async Task ImportAsync(OfficeWindow window, ContactsEditor editor)
    {
        var path = await OfficeApplication.FileDialogs.PickFileAsync(window, "Kontakte importieren", SettingsService.SaveFolder, "vCard oder CSV", [".vcf", ".vcard", ".csv", ".txt"]);
        if (path is not null)
        {
            await ImportFileAsync(window, editor, path);
        }
    }

    /// <summary>Imports one file (also a .vcf the system handed over): vCard at once, CSV after its columns were matched. The result in the message bar.</summary>
    public static async Task ImportFileAsync(OfficeWindow window, ContactsEditor editor, string path)
    {
        var file = ContactFiles.Read(path);
        ContactImportResult result;
        if (file.Table is { } table)
        {
            var dialog = new CsvMappingDialog(table);
            if (!await dialog.ShowAsync(window))
            {
                return;
            }

            result = editor.Session.Import(PersonCsv.Import(table, dialog.Mapping), dialog.SkipDuplicates.IsChecked == true);
        }
        else
        {
            result = editor.Session.Import(file.Contacts!, skipDuplicates: true);
        }

        editor.Refresh();
        var text = result.Imported == 1 ? "1 Kontakt importiert" : $"{result.Imported} Kontakte importiert";
        window.ShowMessage(result.Skipped > 0 ? $"{text}, {result.Skipped} übersprungen (Dubletten oder ohne Namen)." : text + ".", InfoSeverity.Success, isClosable: true,
            result.Imported > 0 ? "Rückgängig" : null, result.Imported > 0 ? () =>
            {
                editor.Session.Undo();
                window.ShowMessage(null);
            } : null);
    }

    public static async Task ExportAsync(OfficeWindow window, ContactsEditor editor)
    {
        var selected = editor.Selected;
        var dialog = new ExportDialog(selected.Count, editor.Session.Count);
        if (!await dialog.ShowAsync(window))
        {
            return;
        }

        var people = editor.Session.Export(dialog.OnlySelected.IsChecked == true ? selected.Select(p => p.Id).ToList() : null);
        var extension = ContactFiles.ExtensionOf(dialog.Format);
        var name = (people.Count == 1 ? people[0].Data.NameToShow() : "Kontakte") + extension;
        var target = await OfficeApplication.FileDialogs.PickExportTargetAsync(window, name, SettingsService.SaveFolder,
            dialog.Format == ContactExportFormat.Csv ? "CSV-Datei" : "vCard-Datei", extension);
        if (target is null)
        {
            return;
        }

        var written = ContactFiles.Write(target, people, dialog.Format);
        window.ShowMessage(people.Count == 1 ? $"1 Kontakt exportiert: {written}" : $"{people.Count} Kontakte exportiert: {written}", InfoSeverity.Success, isClosable: true,
            "Ordner zeigen", () => Ui.RevealFile(written));
    }

    private static async Task SyncAsync(OfficeWindow window, ContactsEditor editor)
    {
        var report = await editor.Document.SyncNowAsync();
        editor.UpdateStatus(window.StatusBar);
        if (report.Conflicts > 0)
        {
            window.ShowMessage($"Synchronisiert. {report.Conflicts} Kontakte wurden inzwischen auf einem anderen Gerät geändert – die neuere Fassung gilt.", InfoSeverity.Info, isClosable: true);
        }
    }
}
