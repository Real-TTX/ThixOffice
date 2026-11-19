using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;

namespace ThixOffice.Desktop.Shell;

/// <summary>
/// The file dialogs of the system ("Durchsuchen"). They only return paths – reading and writing is the business layer's
/// (DocumentService). Tests put in their own answers (<see cref="OfficeApplication.FileDialogs"/>).
/// </summary>
public interface IFileDialogs
{
    /// <summary>The files the user chose to open (empty: cancelled).</summary>
    Task<IReadOnlyList<string>> PickFilesToOpenAsync(Window owner, string startFolder);

    /// <summary>Where to save (null: cancelled).</summary>
    Task<string?> PickSaveTargetAsync(Window owner, string suggestedFileName, string startFolder);

    /// <summary>A folder (null: cancelled).</summary>
    Task<string?> PickFolderAsync(Window owner, string startFolder);

    /// <summary>
    /// Where to export to, in a format that is not a document type of the app ("PDF-Dokument", ".pdf"); null: cancelled.
    /// The default asks like <see cref="PickSaveTargetAsync"/> (test doubles).
    /// </summary>
    Task<string?> PickExportTargetAsync(Window owner, string suggestedFileName, string startFolder, string typeDescription, string extension) =>
        PickSaveTargetAsync(owner, suggestedFileName, startFolder);

    /// <summary>A picture to insert (PNG, JPEG, GIF, BMP); null: cancelled. The default asks nothing (test doubles).</summary>
    Task<string?> PickPictureAsync(Window owner, string startFolder) => Task.FromResult<string?>(null);
}

/// <summary>The dialogs of Windows and macOS through Avalonia's storage provider; the filters are the app's supported types.</summary>
public sealed class SystemFileDialogs : IFileDialogs
{
    public async Task<IReadOnlyList<string>> PickFilesToOpenAsync(Window owner, string startFolder)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Öffnen",
            AllowMultiple = true,
            SuggestedStartLocation = await Folder(owner, startFolder),
            FileTypeFilter = [.. Filters(), FilePickerFileTypes.All],
        });
        return files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<string?> PickSaveTargetAsync(Window owner, string suggestedFileName, string startFolder)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Speichern unter",
            SuggestedFileName = suggestedFileName,
            SuggestedStartLocation = await Folder(owner, startFolder),
            DefaultExtension = AppInfo.Definition.SaveType.Name,
            FileTypeChoices = Filters(),
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickExportTargetAsync(Window owner, string suggestedFileName, string startFolder, string typeDescription, string extension)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportieren",
            SuggestedFileName = suggestedFileName,
            SuggestedStartLocation = await Folder(owner, startFolder),
            DefaultExtension = extension.TrimStart('.'),
            FileTypeChoices = [new FilePickerFileType(typeDescription) { Patterns = [$"*{extension}"] }],
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickPictureAsync(Window owner, string startFolder)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Bild einfügen",
            AllowMultiple = false,
            SuggestedStartLocation = await Folder(owner, startFolder),
            FileTypeFilter =
            [
                new FilePickerFileType("Bilder")
                {
                    Patterns = [.. ImageFiles.Extensions.Select(extension => $"*{extension}")],
                    MimeTypes = ["image/png", "image/jpeg", "image/gif", "image/bmp"],
                    AppleUniformTypeIdentifiers = ["public.png", "public.jpeg", "com.compuserve.gif", "com.microsoft.bmp"],
                },
                FilePickerFileTypes.All,
            ],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync(Window owner, string startFolder)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Ordner wählen",
            SuggestedStartLocation = await Folder(owner, startFolder),
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    /// <summary>One filter per supported type ("Textdokument (*.txt)").</summary>
    private static List<FilePickerFileType> Filters() =>
        [.. AppInfo.Definition.SupportedTypes.Select(type => new FilePickerFileType(type.Description)
        {
            Patterns = [$"*{type.Extension}"],
            MimeTypes = type.MimeType is null ? null : [type.MimeType],
            AppleUniformTypeIdentifiers = type.MacTypeIdentifier is null ? null : [type.MacTypeIdentifier],
        })];

    private static async Task<IStorageFolder?> Folder(Window owner, string path) =>
        Directory.Exists(path) ? await owner.StorageProvider.TryGetFolderFromPathAsync(path) : null;
}
