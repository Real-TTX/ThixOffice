using System.Text.Json;
using ThixMail.Core.Accounts;
using ThixMail.Core.Mime;
using ThixMail.Core.Model;
using ThixMail.Core.Store;
using ThixMail.Core.Sync;
using ThixOffice.Core.Models;

namespace ThixMail.Core;

/// <summary>
/// What the person does with mail, UI-free and undoable: read/unread, flag, categories, delete (into Gelöschte Elemente; for good only there), move,
/// archive, folders, mark all read, empty a folder, send (into the Postausgang) and drafts (into Entwürfe). Every action changes the store at once (the
/// list follows), queues the operation for the server (replayed in order by the account's sync, also after being offline) and wakes the sync. Undo
/// takes a waiting operation back before the server saw it, or queues the opposite one.
/// </summary>
public sealed class MailSession
{
    private readonly MailStore _store;
    private readonly AccountStore _accounts;
    private readonly Action<string> _wake;
    private readonly Stack<UndoStep> _undo = new();
    private readonly Stack<UndoStep> _redo = new();

    public MailSession(MailStore store, AccountStore accounts, Action<string> wake)
    {
        _store = store;
        _accounts = accounts;
        _wake = wake;
    }

    /// <summary>Raised after an action, undo or redo (the commands' state follows).</summary>
    public event EventHandler? Changed;

    public MailStore Store => _store;

    public AccountStore Accounts => _accounts;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoLabel => _undo.Count > 0 ? _undo.Peek().Label : null;

    /// <summary>The sample data of snapshots and the self test starts without history.</summary>
    internal void ClearUndoForSamples()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public void Undo() => Step(_undo, _redo, step => step.Undo());

    public void Redo() => Step(_redo, _undo, step => step.Redo());

    private void Step(Stack<UndoStep> from, Stack<UndoStep> to, Action<UndoStep> action)
    {
        if (from.Count == 0)
        {
            return;
        }

        var step = from.Pop();
        action(step);
        to.Push(step);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Record(string label, Action undo, Action redo)
    {
        _undo.Push(new UndoStep(label, undo, redo));
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ===== flags =====

    /// <summary>Gelesen / Ungelesen (Strg+Q / Strg+U).</summary>
    public void SetRead(IReadOnlyCollection<long> ids, bool read) =>
        ChangeFlags(ids, read ? MessageFlags.Seen : MessageFlags.None, read ? MessageFlags.None : MessageFlags.Seen, read ? "Als gelesen markieren" : "Als ungelesen markieren");

    /// <summary>Zur Nachverfolgung (Einfg): flagged or not.</summary>
    public void SetFlagged(IReadOnlyCollection<long> ids, bool flagged) =>
        ChangeFlags(ids, flagged ? MessageFlags.Flagged : MessageFlags.None, flagged ? MessageFlags.None : MessageFlags.Flagged, flagged ? "Kennzeichnen" : "Kennzeichnung löschen");

    /// <summary>Marks every message of a folder read ("Alle als gelesen markieren").</summary>
    public void MarkAllRead(long folderId)
    {
        var ids = _store.List(new MessageQuery([folderId], ListFilter.Unread)).Select(e => e.Id).ToList();
        ChangeFlags(ids, MessageFlags.Seen, MessageFlags.None, "Alle als gelesen markieren");
    }

    /// <summary>Kategorisieren: adds the category to every message that lacks it, else removes it from all.</summary>
    public void ToggleCategory(IReadOnlyCollection<long> ids, string category)
    {
        var name = category.Trim();
        if (name.Length == 0 || name.Any(char.IsWhiteSpace) || name.StartsWith('$') || name.StartsWith('\\'))
        {
            throw new BusinessException("Ein Kategoriename darf keine Leerzeichen enthalten und nicht mit $ oder \\ beginnen.", "Category");
        }

        var headers = _store.Headers(ids);
        var add = headers.Any(h => !h.Categories.Contains(name, StringComparer.OrdinalIgnoreCase));
        ApplyCategory(ids, name, add);
        Record("Kategorisieren", () => ApplyCategory(ids, name, !add), () => ApplyCategory(ids, name, add));
    }

    private void ApplyCategory(IReadOnlyCollection<long> ids, string name, bool add)
    {
        _store.SetFlags(ids, MessageFlags.None, MessageFlags.None, add ? [name] : null, add ? null : [name]);
        foreach (var group in GroupByFolder(ids))
        {
            Queue(group.Folder.AccountId, OperationKind.Flags, new OperationData
            {
                Folder = group.Folder.Path,
                Uids = [.. group.Uids],
                MessageIds = [.. group.Ids],
                AddKeywords = add ? [name] : [],
                RemoveKeywords = add ? [] : [name],
            });
        }
    }

    private void ChangeFlags(IReadOnlyCollection<long> ids, MessageFlags add, MessageFlags remove, string label)
    {
        if (ids.Count == 0)
        {
            return;
        }

        // only the messages that really change (undo restores exactly those)
        var headers = _store.Headers(ids);
        var changing = headers.Where(h => (h.Flags & add) != add || (h.Flags & remove) != 0).Select(h => h.Id).ToList();
        if (changing.Count == 0)
        {
            return;
        }

        ApplyFlags(changing, add, remove);
        Record(label, () => ApplyFlags(changing, remove, add), () => ApplyFlags(changing, add, remove));
    }

    private void ApplyFlags(IReadOnlyCollection<long> ids, MessageFlags add, MessageFlags remove)
    {
        _store.SetFlags(ids, add, remove);
        foreach (var group in GroupByFolder(ids))
        {
            Queue(group.Folder.AccountId, OperationKind.Flags, new OperationData
            {
                Folder = group.Folder.Path,
                Uids = [.. group.Uids],
                MessageIds = [.. group.Ids],
                Add = (int)add,
                Remove = (int)remove,
            });
        }
    }

    // ===== delete, move, archive =====

    /// <summary>
    /// Entf: into Gelöschte Elemente of each account (undoable). Messages already there are deleted for good (the UI asks first) – also when an
    /// account has no Gelöschte Elemente. Answers how many were deleted for good.
    /// </summary>
    public int Delete(IReadOnlyCollection<long> ids)
    {
        var permanent = new List<long>();
        var moves = new List<(long Id, long Target)>();
        foreach (var header in _store.Headers(ids))
        {
            var folder = _store.Folder(header.FolderId);
            var trash = folder is null ? null : _store.FolderByRole(folder.AccountId, FolderRole.Trash);
            if (folder is null || trash is null || trash.Id == folder.Id)
            {
                permanent.Add(header.Id);
            }
            else
            {
                moves.Add((header.Id, trash.Id));
            }
        }

        if (moves.Count > 0)
        {
            MoveRecorded(moves, moves.Count == 1 ? "Löschen" : $"{moves.Count} Elemente löschen");
        }

        if (permanent.Count > 0)
        {
            Expunge(permanent);
        }

        return permanent.Count;
    }

    /// <summary>True when deleting would remove for good (the messages are in Gelöschte Elemente or the account has none): the UI asks.</summary>
    public bool DeletesForGood(IReadOnlyCollection<long> ids) =>
        _store.Headers(ids).Any(h => _store.Folder(h.FolderId) is not { } folder || _store.FolderByRole(folder.AccountId, FolderRole.Trash) is not { } trash || trash.Id == folder.Id);

    /// <summary>Verschieben (Strg+Umschalt+V, drag onto a folder): within the account of each message (another account's folder is refused).</summary>
    public void Move(IReadOnlyCollection<long> ids, long targetFolderId)
    {
        var target = _store.Folder(targetFolderId) ?? throw new BusinessException("Diesen Ordner gibt es nicht mehr.");
        var headers = _store.Headers(ids);
        if (headers.Any(h => h.AccountId != target.AccountId))
        {
            throw new BusinessException("Nachrichten lassen sich nur innerhalb ihres Kontos verschieben.");
        }

        var moves = headers.Where(h => h.FolderId != targetFolderId).Select(h => (h.Id, targetFolderId)).ToList();
        if (moves.Count > 0)
        {
            MoveRecorded(moves, $"In „{target.DisplayName}“ verschieben");
        }
    }

    /// <summary>
    /// Verschieben into a folder of ANOTHER account: every message's whole text first (cached or fetched through <paramref name="body"/>; nothing changes
    /// when one cannot be loaded), then a copy into the target (shown at once, appended by the target's sync) and the originals into Gelöschte Elemente
    /// of their accounts (for good where there is none). Not undoable – the originals wait in Gelöschte Elemente. Answers how many were moved.
    /// </summary>
    public async Task<int> MoveToAccountAsync(IReadOnlyCollection<long> ids, long targetFolderId, Func<long, CancellationToken, Task<byte[]?>> body, CancellationToken cancellationToken)
    {
        var target = _store.Folder(targetFolderId) ?? throw new BusinessException("Diesen Ordner gibt es nicht mehr.");
        var headers = _store.Headers(ids).Where(h => h.AccountId != target.AccountId).ToList();
        var copies = new List<(MessageHeader Header, byte[] Raw)>();
        foreach (var header in headers)
        {
            var raw = await body(header.Id, cancellationToken)
                      ?? throw new BusinessException($"„{header.Subject}“ ist nicht auf diesem Computer gespeichert und der Mailserver ist nicht erreichbar. Verschieben Sie die Nachricht, wenn ThixMail online ist.");
            copies.Add((header, raw));
        }

        foreach (var (header, raw) in copies)
        {
            var flags = header.Flags & (MessageFlags.Seen | MessageFlags.Flagged | MessageFlags.Answered | MessageFlags.Forwarded);
            var path = _store.WriteFile("move", raw);
            _store.Enqueue(target.AccountId, OperationKind.Append, new OperationData { Folder = target.Path, RawPath = path, Add = (int)flags, MessageId = header.MessageId });
            var id = _store.AddLocalMessage(target.Id, target.AccountId, MailMime.Summarize(0, raw, flags, [.. header.Categories], 0));
            _store.SetBody(id, raw, MailReader.Read(raw).Text);
        }

        var toTrash = new List<(long Id, long Target)>();
        var forGood = new List<long>();
        foreach (var (header, _) in copies)
        {
            if (_store.FolderByRole(header.AccountId, FolderRole.Trash) is { } trash && trash.Id != header.FolderId)
            {
                toTrash.Add((header.Id, trash.Id));
            }
            else
            {
                forGood.Add(header.Id);
            }
        }

        ApplyMoves(toTrash);
        if (forGood.Count > 0)
        {
            Expunge(forGood);
        }

        _wake(target.AccountId);
        Changed?.Invoke(this, EventArgs.Empty);
        return copies.Count;
    }

    /// <summary>Archivieren: into the account's Archiv (refused when the account has none).</summary>
    public void Archive(IReadOnlyCollection<long> ids)
    {
        var moves = new List<(long, long)>();
        foreach (var header in _store.Headers(ids))
        {
            var archive = _store.FolderByRole(header.AccountId, FolderRole.Archive)
                          ?? throw new BusinessException("Dieses Konto hat keinen Ordner „Archiv“. Legen Sie einen an (Ordner › Neuer Ordner) oder verschieben Sie die Nachricht.");
            if (archive.Id != header.FolderId)
            {
                moves.Add((header.Id, archive.Id));
            }
        }

        if (moves.Count > 0)
        {
            MoveRecorded(moves, "Archivieren");
        }
    }

    private void MoveRecorded(IReadOnlyList<(long Id, long Target)> moves, string label)
    {
        var (undo, redo) = MoveStep(moves);
        Record(label, undo, redo);
    }

    /// <summary>Moves now; answers how to take it back and how to do it again (one undo step, alone or within the rules' step).</summary>
    private (Action Undo, Action Redo) MoveStep(IReadOnlyList<(long Id, long Target)> moves)
    {
        var before = moves.Select(m => (m.Id, Folder: _store.Header(m.Id)!.FolderId, Uid: _store.RawUid(m.Id))).ToList();
        var operations = ApplyMoves(moves);
        return (
            () =>
            {
                // the server has not seen the move yet: take it back; else move back
                var waiting = _store.PendingOperations().Select(op => op.Id).ToHashSet();
                if (operations.All(waiting.Contains))
                {
                    foreach (var operation in operations)
                    {
                        _store.CompleteOperation(operation);
                    }

                    foreach (var group in before.GroupBy(b => b.Folder))
                    {
                        _store.MoveMessages([.. group.Select(b => b.Id)], group.Key);
                        _store.SetUids([.. group.Select(b => b.Id)], [.. group.Select(b => (uint)Math.Max(0, b.Uid))]);
                    }
                }
                else
                {
                    operations = ApplyMoves([.. before.Select(b => (b.Id, b.Folder))]);
                }
            },
            () => operations = ApplyMoves(moves));
    }

    // ===== rules =====

    /// <summary>
    /// Carries out what the rules decided (<see cref="MailRules.Plan"/>): read, flagged, categories, moves, deletions into Gelöschte Elemente (an
    /// account without one keeps the message – a rule never deletes for good). <paramref name="undoable"/>: ONE step "Regeln anwenden" ("Regeln
    /// jetzt anwenden"); on arrival nothing is recorded (it may run on the sync's thread). Answers how many messages changed.
    /// </summary>
    public int ApplyRules(IReadOnlyList<RuleOutcome> outcomes, bool undoable)
    {
        var steps = new List<(Action Undo, Action Redo)>();
        var read = outcomes.Where(o => o.MarkRead).Select(o => o.MessageId).ToList();
        if (read.Count > 0)
        {
            ApplyFlags(read, MessageFlags.Seen, MessageFlags.None);
            steps.Add((() => ApplyFlags(read, MessageFlags.None, MessageFlags.Seen), () => ApplyFlags(read, MessageFlags.Seen, MessageFlags.None)));
        }

        var flagged = outcomes.Where(o => o.Flag).Select(o => o.MessageId).ToList();
        if (flagged.Count > 0)
        {
            ApplyFlags(flagged, MessageFlags.Flagged, MessageFlags.None);
            steps.Add((() => ApplyFlags(flagged, MessageFlags.None, MessageFlags.Flagged), () => ApplyFlags(flagged, MessageFlags.Flagged, MessageFlags.None)));
        }

        foreach (var group in outcomes.SelectMany(o => o.Categories.Select(c => (o.MessageId, Category: c))).GroupBy(p => p.Category, StringComparer.OrdinalIgnoreCase))
        {
            var ids = group.Select(p => p.MessageId).ToList();
            ApplyCategory(ids, group.Key, add: true);
            steps.Add((() => ApplyCategory(ids, group.Key, add: false), () => ApplyCategory(ids, group.Key, add: true)));
        }

        var moves = new List<(long Id, long Target)>();
        foreach (var outcome in outcomes)
        {
            if (outcome.MoveTo is { } target)
            {
                moves.Add((outcome.MessageId, target));
            }
            else if (outcome.Delete && _store.Header(outcome.MessageId) is { } header && _store.FolderByRole(header.AccountId, FolderRole.Trash) is { } trash && trash.Id != header.FolderId)
            {
                moves.Add((outcome.MessageId, trash.Id));
            }
        }

        if (moves.Count > 0)
        {
            steps.Add(MoveStep(moves));
        }

        if (undoable && steps.Count > 0)
        {
            Record("Regeln anwenden", () =>
            {
                for (var index = steps.Count - 1; index >= 0; index--)
                {
                    steps[index].Undo();
                }
            }, () =>
            {
                foreach (var step in steps)
                {
                    step.Redo();
                }
            });
        }

        return outcomes.Count(o => !o.IsEmpty);
    }

    /// <summary>"Regeln jetzt anwenden" on a folder: every enabled rule on every message there, one undoable step; how many messages changed.</summary>
    public int ApplyRulesNow(long folderId)
    {
        var messages = _store.Headers([.. _store.List(new MessageQuery([folderId])).Select(e => e.Id)]);
        return ApplyRules(MailRules.Plan(_accounts.Rules, messages, _store), undoable: true);
    }

    /// <summary>Moves in the store and queues one operation per source folder and target; answers the operations' ids.</summary>
    private List<long> ApplyMoves(IReadOnlyList<(long Id, long Target)> moves)
    {
        var operations = new List<long>();
        foreach (var byTarget in moves.GroupBy(m => m.Target))
        {
            var target = _store.Folder(byTarget.Key);
            if (target is null)
            {
                continue;
            }

            foreach (var group in GroupByFolder([.. byTarget.Select(m => m.Id)]))
            {
                operations.Add(_store.Enqueue(group.Folder.AccountId, OperationKind.Move, new OperationData
                {
                    Folder = group.Folder.Path,
                    Uids = [.. group.Uids],
                    MessageIds = [.. group.Ids],
                    Target = target.Path,
                }));
            }

            _store.MoveMessages([.. byTarget.Select(m => m.Id)], target.Id);
            _wake(target.AccountId);
        }

        return operations;
    }

    private void Expunge(IReadOnlyCollection<long> ids)
    {
        foreach (var group in GroupByFolder(ids))
        {
            Queue(group.Folder.AccountId, OperationKind.Expunge, new OperationData { Folder = group.Folder.Path, Uids = [.. group.Uids], MessageIds = [.. group.Ids] });
        }

        _store.RemoveMessages(ids);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ordner leeren: every message of the folder for good (the UI asks first).</summary>
    public void EmptyFolder(long folderId)
    {
        var ids = _store.List(new MessageQuery([folderId])).Select(e => e.Id).ToList();
        Expunge(ids);
    }

    // ===== folders =====

    /// <summary>Neuer Ordner below <paramref name="parentFolderId"/> (null = the account's top level).</summary>
    public long CreateFolder(string accountId, long? parentFolderId, string name)
    {
        var clean = CheckFolderName(name);
        var parent = parentFolderId is { } id ? _store.Folder(id) : null;
        var delimiter = parent?.Delimiter ?? _store.Folders(accountId).FirstOrDefault()?.Delimiter ?? '/';
        var path = parent is null ? clean : parent.Path + delimiter + clean;
        if (_store.FolderByPath(accountId, path) is not null)
        {
            throw new BusinessException($"Den Ordner „{clean}“ gibt es hier schon.", "Name");
        }

        var created = _store.AddFolder(accountId, path, clean, parent?.Path, delimiter);
        Queue(accountId, OperationKind.CreateFolder, new OperationData { Folder = parent?.Path ?? "", Name = clean });
        Changed?.Invoke(this, EventArgs.Empty);
        return created;
    }

    public void RenameFolder(long folderId, string name)
    {
        var folder = _store.Folder(folderId) ?? throw new BusinessException("Diesen Ordner gibt es nicht mehr.");
        if (folder.Role != FolderRole.None)
        {
            throw new BusinessException($"„{folder.DisplayName}“ ist ein Standardordner und lässt sich nicht umbenennen.");
        }

        var clean = CheckFolderName(name);
        var newPath = folder.ParentPath is null ? clean : folder.ParentPath + folder.Delimiter + clean;
        if (_store.FolderByPath(folder.AccountId, newPath) is not null)
        {
            throw new BusinessException($"Den Ordner „{clean}“ gibt es hier schon.", "Name");
        }

        Queue(folder.AccountId, OperationKind.RenameFolder, new OperationData { Folder = folder.Path, Name = clean });
        _store.RenameFolder(folderId, newPath, clean);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Deletes a folder with its messages and subfolders (the UI asks first; standard folders are refused).</summary>
    public void DeleteFolder(long folderId)
    {
        var folder = _store.Folder(folderId) ?? throw new BusinessException("Diesen Ordner gibt es nicht mehr.");
        if (folder.Role != FolderRole.None)
        {
            throw new BusinessException($"„{folder.DisplayName}“ ist ein Standardordner und lässt sich nicht löschen.");
        }

        Queue(folder.AccountId, OperationKind.DeleteFolder, new OperationData { Folder = folder.Path });
        _store.RemoveFolder(folderId);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string CheckFolderName(string name)
    {
        var clean = name.Trim();
        if (clean.Length == 0)
        {
            throw new BusinessException("Bitte geben Sie einen Namen für den Ordner an.", "Name");
        }

        if (clean.IndexOfAny(['/', '\\', '*', '%', '"']) >= 0 || clean.Length > 100)
        {
            throw new BusinessException("Ein Ordnername darf / \\ * % \" nicht enthalten und höchstens 100 Zeichen lang sein.", "Name");
        }

        return clean;
    }

    // ===== sending and drafts =====

    /// <summary>The largest message ThixMail sends (most servers take 25 MB; the composer warns from 10 MB).</summary>
    public const long MaximumMessageSize = 25L * 1024 * 1024;

    public const long LargeAttachmentWarning = 10L * 1024 * 1024;

    /// <summary>Checks a draft before sending: an account, at least one recipient, every address plausible, not too large.</summary>
    public MailAccount ValidateForSending(Draft draft)
    {
        var account = _accounts.Account(draft.AccountId) ?? throw new BusinessException("Bitte wählen Sie das Konto, von dem gesendet wird.", "From");
        if (!draft.Recipients.Any())
        {
            throw new BusinessException("Bitte geben Sie mindestens einen Empfänger an.", "To");
        }

        if (draft.Recipients.FirstOrDefault(r => !AccountService.IsAddress(r.Address)) is { } wrong)
        {
            throw new BusinessException($"„{wrong.Full}“ ist keine gültige E-Mail-Adresse.", draft.To.Contains(wrong) ? "To" : draft.Cc.Contains(wrong) ? "Cc" : "Bcc");
        }

        if (draft.AttachmentSize > MaximumMessageSize)
        {
            throw new BusinessException("Die Anlagen sind zusammen größer als 25 MB – das nehmen die meisten Mailserver nicht an.", "Attachments");
        }

        return account;
    }

    /// <summary>
    /// Senden (Strg+Eingabe): into the Postausgang (sent by the sync now, or when the server can be reached again), the recipients remembered for
    /// suggestions, the saved draft removed. Refused with a German message at the field that is wrong.
    /// </summary>
    public long Send(Draft draft, DateTimeOffset? sendAt = null)
    {
        var account = ValidateForSending(draft);
        if (sendAt is { } at && at <= DateTimeOffset.Now.AddMinutes(1))
        {
            throw new BusinessException("Der Zeitpunkt zum Senden muss in der Zukunft liegen.", "SendAt");
        }

        var sending = draft with { From = account.Sender };
        var raw = MailComposer.ToBytes(sending);
        var recipients = JsonSerializer.Serialize(sending.Recipients.ToList(), MailJson.Default.ListMailAddress);
        var id = _store.AddToOutbox(account.Id, raw, sending.Subject, recipients, sending.MessageId, sending.OriginalMessage, (int)sending.Origin, sendAt);
        _store.RememberRecipients(sending.Recipients);
        if (draft.SavedDraft is { } saved)
        {
            RemoveDraft(saved);
        }

        _wake(account.Id);
        Changed?.Invoke(this, EventArgs.Empty);
        return id;
    }

    /// <summary>Saves the draft into Entwürfe (replacing its previous copy); answers the draft with its new copy's id. Works offline.</summary>
    public Draft SaveDraft(Draft draft)
    {
        var account = _accounts.Account(draft.AccountId) ?? throw new BusinessException("Bitte wählen Sie ein Konto.", "From");
        var drafts = _store.FolderByRole(account.Id, FolderRole.Drafts)
                     ?? throw new BusinessException("Dieses Konto hat keinen Ordner „Entwürfe“ – der Entwurf kann nicht auf dem Server gespeichert werden.");
        if (draft.SavedDraft is { } previous)
        {
            RemoveDraft(previous);
        }

        var raw = MailComposer.ToBytes(draft with { From = account.Sender }, keepBcc: true);
        var path = _store.WriteFile("draft", raw);
        _store.Enqueue(account.Id, OperationKind.Append, new OperationData { Folder = drafts.Path, RawPath = path, Add = (int)(MessageFlags.Draft | MessageFlags.Seen), MessageId = draft.MessageId });
        var id = _store.AddLocalMessage(drafts.Id, account.Id, MailMime.Summarize(0, raw, MessageFlags.Draft | MessageFlags.Seen, [], 0));
        _store.SetBody(id, raw, draft.Text);
        _wake(account.Id);
        return draft with { SavedDraft = id };
    }

    /// <summary>A saved draft goes (sent, or replaced by a newer save): from the server when it is there, its waiting append dropped otherwise.</summary>
    public void RemoveDraft(long id)
    {
        var header = _store.Header(id);
        if (header is null || _store.Folder(header.FolderId) is not { } folder)
        {
            return;
        }

        var uid = _store.RawUid(id);
        if (uid > 0)
        {
            Queue(folder.AccountId, OperationKind.Expunge, new OperationData { Folder = folder.Path, Uids = [(uint)uid], MessageIds = [id] });
        }
        else
        {
            foreach (var waiting in _store.PendingOperations(folder.AccountId).Where(op => op.Kind == OperationKind.Append && op.Data.MessageId == header.MessageId))
            {
                _store.CompleteOperation(waiting.Id);
                if (waiting.Data.RawPath is { } path)
                {
                    _store.DeleteFile(path);
                }
            }
        }

        _store.RemoveMessages([id]);
    }

    /// <summary>A message the server refused stays in the Postausgang; "Erneut senden" puts it back in the queue.</summary>
    public void RetryOutbox(long outboxId, string accountId)
    {
        _store.SetOutboxState(outboxId, OutboxState.Waiting);
        _wake(accountId);
    }

    // ===== helpers =====

    private void Queue(string accountId, OperationKind kind, OperationData data)
    {
        _store.Enqueue(accountId, kind, data);
        _wake(accountId);
    }

    private sealed record FolderGroup(MailFolder Folder, List<long> Ids, List<uint> Uids);

    /// <summary>The messages by folder, with their server UIDs (0 for placeholders the server does not know yet: the server part is skipped for them).</summary>
    private List<FolderGroup> GroupByFolder(IReadOnlyCollection<long> ids)
    {
        var groups = new List<FolderGroup>();
        foreach (var byFolder in _store.Headers(ids).GroupBy(h => h.FolderId))
        {
            if (_store.Folder(byFolder.Key) is not { } folder)
            {
                continue;
            }

            var list = byFolder.ToList();
            groups.Add(new FolderGroup(folder, [.. list.Select(h => h.Id)], [.. list.Select(h => (uint)Math.Max(0, _store.RawUid(h.Id)))]));
        }

        return groups;
    }

    private sealed record UndoStep(string Label, Action Undo, Action Redo);
}
