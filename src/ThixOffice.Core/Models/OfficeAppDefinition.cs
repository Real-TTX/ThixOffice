namespace ThixOffice.Core.Models;

/// <summary>How far an app is with a document type.</summary>
public enum DocumentTypeSupport
{
    /// <summary>The app opens and saves it: the file type is registered with the system (ProgID, "Öffnen mit").</summary>
    Supported = 0,

    /// <summary>Declared for later (the format comes in a later phase): never registered, never offered in a file dialog.</summary>
    Planned = 1,
}

/// <summary>
/// A document type of an app. <see cref="ProgId"/> is the Windows class of the type (<c>ThixWrite.Document</c>), the
/// <see cref="Description"/> the name Explorer and Finder show. <see cref="IsDefault"/> is the app's own format (new
/// documents are saved in it); it is still never taken over from another program silently (FileAssociations).
/// </summary>
public sealed record DocumentType(
    string Extension,
    string ProgId,
    string Description,
    DocumentTypeSupport Support = DocumentTypeSupport.Supported,
    bool IsDefault = false,
    string? MimeType = null,
    string? MacTypeIdentifier = null)
{
    /// <summary>The extension without the dot, lower case ("docx").</summary>
    public string Name => Extension.TrimStart('.').ToLowerInvariant();

    public bool IsSupported => Support == DocumentTypeSupport.Supported;
}

/// <summary>
/// Everything that makes one app of the suite different from the others, as data: id, name, accent, file names, registry
/// keys, bundle id and document types. Every shared service (settings, setup, file types, updates, policies, autostart)
/// takes its names from here instead of being copied per app; <see cref="Infrastructure.AppInfo.Initialize"/> makes it the
/// definition of the running process.
/// </summary>
public sealed record OfficeAppDefinition
{
    /// <summary>A definition; <paramref name="id"/> is lower case (<c>thixwrite</c>), <paramref name="name"/> the product name (<c>ThixWrite</c>).</summary>
    public OfficeAppDefinition(string id, string name, string accent, string description, IReadOnlyList<DocumentType> documentTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!name.StartsWith(SuiteMarkPrefix, StringComparison.Ordinal) || !string.Equals(id, name.ToLowerInvariant(), StringComparison.Ordinal))
        {
            throw new ArgumentException($"An app of the suite is called Thix<Suffix> and its id is the name in lower case ({name}, {id}).", nameof(id));
        }

        Id = id;
        Name = name;
        Accent = accent;
        Description = description;
        DocumentTypes = documentTypes;
    }

    /// <summary>The first part of every name and word mark ("Thix"); the rest is drawn in the accent.</summary>
    public const string SuiteMarkPrefix = "Thix";

    /// <summary>The publisher of every app (uninstall entry, copyright).</summary>
    public const string Publisher = "Matthias Schmoldt";

    /// <summary>The GitHub repository that builds every app of the suite.</summary>
    public const string Repository = "Real-TTX/ThixOffice";

    /// <summary>The app id: <c>thixwrite</c>.</summary>
    public string Id { get; }

    /// <summary>The product name: <c>ThixWrite</c>.</summary>
    public string Name { get; }

    /// <summary>The accent of the app (<c>#185ABD</c>): the one literal colour an app has (MatTheme derives everything from it).</summary>
    public string Accent { get; }

    /// <summary>One German sentence: what the app is (setup, about page, shortcuts).</summary>
    public string Description { get; }

    public IReadOnlyList<DocumentType> DocumentTypes { get; }

    /// <summary>The name of a new document before it is saved, numbered per process: "Dokument" → Dokument1, Dokument2 …</summary>
    public string UntitledBaseName { get; init; } = "Dokument";

    /// <summary>The tile of "Neu" that makes an empty document ("Leeres Dokument", "Leere Arbeitsmappe").</summary>
    public string BlankDocumentLabel { get; init; } = "Leeres Dokument";

    /// <summary>The part of the name after "Thix", drawn in the accent of the word mark: <c>Write</c>.</summary>
    public string Suffix => Name[SuiteMarkPrefix.Length..];

    /// <summary>The program file on Windows: <c>ThixWrite.exe</c>.</summary>
    public string ExecutableName => Name + ".exe";

    /// <summary>The setup that CI publishes: <c>ThixWriteSetup.exe</c> (the program itself under another name).</summary>
    public string SetupFileName => Name + "Setup.exe";

    /// <summary>The key of the entry in "Installierte Apps" below HKCU (ThixLauncher looks for it).</summary>
    public string UninstallKey => $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{Name}";

    /// <summary>The machine policies (DWORD values): <c>SOFTWARE\Policies\ThixWrite</c> below HKLM.</summary>
    public string PolicyKey => $@"SOFTWARE\Policies\{Name}";

    /// <summary>The bundle id on macOS, also the domain of its configuration profile, the LaunchAgent and the Keychain item.</summary>
    public string MacBundleId => $"info.schmoldt.{Id}";

    /// <summary>The update manifest in the rolling releases: <c>thixwrite-update.json</c>.</summary>
    public string ManifestFileName => $"{Id}-update.json";

    /// <summary>The zipped Mac app of one architecture (the update package): <c>ThixWrite-macOS-arm64.zip</c>.</summary>
    public string MacPackageFileName(string architecture) => $"{Name}-macOS-{architecture}.zip";

    /// <summary>The variable that points a development run to a data folder of its own: <c>THIXWRITE_DATA_DIR</c>.</summary>
    public string DataDirectoryVariable => Name.ToUpperInvariant() + "_DATA_DIR";

    /// <summary>The variable that makes the UI tests keep their pictures: <c>THIXWRITE_SCREENSHOTS</c>.</summary>
    public string ScreenshotsVariable => Name.ToUpperInvariant() + "_SCREENSHOTS";

    /// <summary>The icon file of the documents, written next to the program (the single-file exe carries only the app's icon).</summary>
    public string DocumentIconFileName => Name + "-Document.ico";

    /// <summary>The types the app opens and saves now.</summary>
    public IReadOnlyList<DocumentType> SupportedTypes => DocumentTypes.Where(type => type.IsSupported).ToList();

    /// <summary>The app's own format; a planned one until its reader exists (then <see cref="SaveType"/> is the first supported type).</summary>
    public DocumentType DefaultType => DocumentTypes.FirstOrDefault(type => type.IsDefault) ?? DocumentTypes[0];

    /// <summary>The type a new document is saved in: the default type when it is supported, otherwise the first supported one.</summary>
    public DocumentType SaveType => DefaultType.IsSupported ? DefaultType : SupportedTypes.FirstOrDefault() ?? DefaultType;

    /// <summary>The supported type of a file name, or null.</summary>
    public DocumentType? SupportedTypeOf(string path)
    {
        var extension = Path.GetExtension(path);
        return SupportedTypes.FirstOrDefault(type => string.Equals(type.Extension, extension, StringComparison.OrdinalIgnoreCase));
    }
}
