using System.Text;
using ThixOffice.Core.Business;
using ThixOffice.Core.Documents;
using ThixOffice.Core.Formats.OfficeCrypto;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;
using ThixOffice.Core.Platform;
using static ThixOffice.Core.Tests.CloudTests;

namespace ThixOffice.Core.Tests;

/// <summary>
/// Documents protected with "Mit Kennwort verschlüsseln" through the business layer: opening asks for the password (and refuses a wrong
/// one), saving, Speichern unter, recovery files and ThixDrive keep the encryption, a type without passwords drops it, foreign kinds are
/// refused in words instead of being read as garbage.
/// </summary>
public sealed class PasswordDocumentTests
{
    private const string Password = "Geheim-2026";

    /// <summary>ThixTest with a type that can carry a password (.tse, like .docx) next to one that cannot (.tst, like .odt).</summary>
    private static readonly OfficeAppDefinition ProtectingApp = new(
        "thixtest",
        "ThixTest",
        "#185ABD",
        "Ein Testprogramm.",
        [
            new DocumentType(".tse", "ThixTest.Protected", "Geschützter Testtext", IsDefault: true) { CanEncrypt = true },
            new DocumentType(".tst", "ThixTest.Text", "Testtext", MimeType: "text/plain"),
        ])
    {
        UntitledBaseName = "Test",
        CloudAppId = MatCloud.Contract.CloudApps.Write,
    };

    /// <summary>ThixTest writing OpenDocument packages (.tod, like .odt): its saves are encrypted the way LibreOffice does it.</summary>
    private static readonly OfficeAppDefinition OdfApp = new(
        "thixtest",
        "ThixTest",
        "#185ABD",
        "Ein Testprogramm.",
        [
            new DocumentType(".tod", "ThixTest.Odf", "Test-OpenDocument", IsDefault: true) { CanEncrypt = true },
            new DocumentType(".tst", "ThixTest.Text", "Testtext", MimeType: "text/plain"),
        ])
    {
        UntitledBaseName = "Test",
    };

    [Fact]
    public void The_Office_Open_XML_and_OpenDocument_types_can_carry_a_password()
    {
        Assert.True(new DocumentType(".docx", "x", "Word").CanEncrypt);
        Assert.True(new DocumentType(".XLSM", "x", "Excel").CanEncrypt);
        Assert.True(new DocumentType(".pptx", "x", "PowerPoint").CanEncrypt);
        Assert.True(new DocumentType(".odt", "x", "ODF").CanEncrypt);
        Assert.True(new DocumentType(".ods", "x", "ODF").CanEncrypt);
        Assert.True(new DocumentType(".odp", "x", "ODF").CanEncrypt);
        Assert.False(new DocumentType(".txt", "x", "Text").CanEncrypt);
        Assert.False(new DocumentType(".csv", "x", "CSV").CanEncrypt);
        Assert.False(DocumentType.Pdf.CanEncrypt);
    }

    [Fact]
    public void A_protected_file_needs_its_password_and_a_wrong_one_is_refused_at_the_field()
    {
        using var data = Protecting();
        var path = Encrypted(data, "Bericht.tse", "Inhalt");

        var required = Assert.Throws<PasswordRequiredException>(() => DocumentService.Open(path, name => new TestDocument(name)));
        Assert.Equal("Bericht.tse", required.FileName);
        var wrong = Assert.Throws<WrongPasswordException>(() => DocumentService.Open(path, name => new TestDocument(name), "falsch"));
        Assert.Equal(OfficeEncryption.PasswordField, wrong.Field);
        Assert.Empty(RecentDocuments.List()); // nothing opened yet

        var document = DocumentService.Open(path, name => new TestDocument(name), Password);

        Assert.Equal("Inhalt", document.Content);
        Assert.True(document.HasPassword);
        Assert.False(document.IsDirty);
        Assert.Equal(path, document.FilePath);
        Assert.Single(RecentDocuments.List());
        Assert.DoesNotContain(Directory.EnumerateFiles(AppPaths.DataDirectory, "*", SearchOption.AllDirectories), file => File.ReadAllText(file).Contains(Password, StringComparison.Ordinal));
    }

    [Fact]
    public void Saving_a_protected_document_writes_it_encrypted_again()
    {
        using var data = Protecting();
        var path = Encrypted(data, "Bericht.tse", "alt");
        var document = DocumentService.Open(path, name => new TestDocument(name), Password);
        document.Type("neu");

        DocumentService.Save(document);

        Assert.Equal("neu", Decrypted(path));
        Assert.True(document.HasPassword);
        Assert.Equal("neu", DocumentService.Open(path, name => new TestDocument(name), Password).Content);
    }

    [Fact]
    public void A_new_password_protects_the_next_save_and_removing_it_saves_plain()
    {
        using var data = Protecting();
        var document = new TestDocument("Test1");
        document.Type("Hallo");
        var path = DocumentService.SaveAs(document, Path.Combine(data.DocumentsFolder, "Neu"));
        Assert.Equal("Hallo", File.ReadAllText(path));

        DocumentService.SetPassword(document, Password, Password);
        Assert.True(document.HasPassword);
        Assert.True(document.IsDirty); // a change that needs a save, like Word's
        DocumentService.Save(document);
        Assert.Equal(EncryptedFileKind.OfficeAgile, Kind(path));
        Assert.Equal("Hallo", Decrypted(path));

        DocumentService.SetPassword(document, "", "");
        Assert.False(document.HasPassword);
        Assert.True(document.IsDirty);
        DocumentService.Save(document);
        Assert.Equal("Hallo", File.ReadAllText(path));
    }

    [Fact]
    public void A_new_password_is_checked_in_the_business_layer()
    {
        using var data = Protecting();
        var document = new TestDocument("Test1");

        Assert.Equal(OfficeEncryption.PasswordField, Assert.Throws<BusinessException>(() => DocumentService.SetPassword(document, "", "")).Field);
        var mismatch = Assert.Throws<BusinessException>(() => DocumentService.SetPassword(document, "eins", "zwei"));
        Assert.Equal(OfficeEncryption.ConfirmationField, mismatch.Field);
        Assert.Equal("Die Kennwörter stimmen nicht überein.", mismatch.Message);
        var tooLong = new string('x', OfficeEncryption.MaxPasswordLength + 1);
        Assert.Equal(OfficeEncryption.PasswordField, Assert.Throws<BusinessException>(() => DocumentService.SetPassword(document, tooLong, tooLong)).Field);
        Assert.Equal(OfficeEncryption.PasswordField, Assert.Throws<BusinessException>(() => document.SetPassword(tooLong)).Field);
        Assert.False(document.HasPassword);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void Saving_as_a_type_without_passwords_writes_plain_and_leaves_the_document_unprotected()
    {
        using var data = Protecting();
        var document = DocumentService.Open(Encrypted(data, "Bericht.tse", "Inhalt"), name => new TestDocument(name), Password);
        var plain = Path.Combine(data.DocumentsFolder, "Bericht.tst");

        Assert.True(DocumentService.LosesPassword(document, plain));
        Assert.False(DocumentService.LosesPassword(document, Path.Combine(data.DocumentsFolder, "Kopie.tse")));
        Assert.Equal("„Bericht.tst“ kann nicht mit einem Kennwort geschützt werden. Ohne Kennwort speichern?", DocumentService.PasswordLossQuestion(plain));
        DocumentService.SaveAs(document, plain);

        Assert.Equal("Inhalt", File.ReadAllText(plain));
        Assert.False(document.HasPassword);
        Assert.False(DocumentService.LosesPassword(document, plain));
    }

    [Fact]
    public void Speichern_unter_keeps_the_password_in_a_type_that_carries_it()
    {
        using var data = Protecting();
        var document = DocumentService.Open(Encrypted(data, "Bericht.tse", "Inhalt"), name => new TestDocument(name), Password);

        var copy = DocumentService.SaveAs(document, Path.Combine(data.DocumentsFolder, "Kopie.tse"));

        Assert.Equal("Inhalt", Decrypted(copy));
        Assert.True(document.HasPassword);
    }

    [Fact]
    public void The_recovery_file_of_a_protected_document_is_encrypted_and_needs_the_password()
    {
        using var data = Protecting();
        var document = DocumentService.Open(Encrypted(data, "Bericht.tse", "alt"), name => new TestDocument(name), Password);
        document.Type("ungespeichert");
        DocumentService.WriteRecovery(document);
        var info = Assert.Single(DocumentService.RecoveredDocuments());
        var recoveryFile = Directory.EnumerateFiles(AppPaths.RecoveryDirectory, "*.data").Single();
        Assert.Equal(EncryptedFileKind.OfficeAgile, Kind(recoveryFile));

        var required = Assert.Throws<PasswordRequiredException>(() => DocumentService.Recover(info, name => new TestDocument(name)));
        Assert.Equal("Bericht.tse", required.FileName);
        Assert.Throws<WrongPasswordException>(() => DocumentService.Recover(info, name => new TestDocument(name), "falsch"));
        Assert.Single(DocumentService.RecoveredDocuments()); // kept for another try

        var recovered = DocumentService.Recover(info, name => new TestDocument(name), Password);

        Assert.Equal("ungespeichert", recovered.Content);
        Assert.True(recovered.HasPassword);
        Assert.True(recovered.IsRecovered);
        Assert.Empty(DocumentService.RecoveredDocuments());
    }

    [Fact]
    public void Foreign_kinds_are_refused_in_words_and_never_reach_the_model()
    {
        using var data = Protecting();
        var binary = Path.Combine(data.DocumentsFolder, "Alt.tse");
        File.WriteAllBytes(binary, CompoundWithoutEncryption());

        var refusal = Assert.Throws<BusinessException>(() => DocumentService.Open(binary, name => new TestDocument(name)));
        Assert.Contains("älteren Binärformat", refusal.Message, StringComparison.Ordinal);

        var broken = Path.Combine(data.DocumentsFolder, "Kaputt.tse");
        var bytes = File.ReadAllBytes(Encrypted(data, "Heil.tse", "x"));
        File.WriteAllBytes(broken, bytes[..600]); // the signature, the rest cut off
        var damaged = Assert.Throws<BusinessException>(() => DocumentService.Open(broken, name => new TestDocument(name), Password));
        Assert.Contains("beschädigt", damaged.Message, StringComparison.Ordinal);
        Assert.IsNotType<WrongPasswordException>(damaged);
    }

    [Fact]
    public async Task A_protected_ThixDrive_document_opens_with_its_password_and_uploads_encrypted()
    {
        using var data = Protecting();
        var cloud = await SignedInAsync(new SampleCloud());
        var file = cloud.AddFile(null, "Brief.tse", EncryptedBytes("aus der Cloud"));

        await Assert.ThrowsAsync<PasswordRequiredException>(() => DriveService.OpenAsync(file.Id, name => new TestDocument(name)));
        await Assert.ThrowsAsync<WrongPasswordException>(() => DriveService.OpenAsync(file.Id, name => new TestDocument(name), password: "falsch"));
        var document = await DriveService.OpenAsync(file.Id, name => new TestDocument(name), password: Password);
        Assert.Equal("aus der Cloud", document.Content);
        document.Type("neu");

        Assert.True((await DriveService.SaveAsync(document)).IsUploaded);

        using (var uploaded = new MemoryStream(cloud.ContentOf(file.Id)))
        {
            Assert.Equal(EncryptedFileKind.OfficeAgile, OfficeEncryption.Detect(uploaded));
            Assert.Equal("neu", Encoding.UTF8.GetString(OfficeEncryption.Decrypt(uploaded, Password)));
        }

        var entry = RecentDocuments.List().Single(item => item.Document.IsInDrive).Document;
        await Assert.ThrowsAsync<PasswordRequiredException>(() => DriveService.OpenRecentAsync(entry, name => new TestDocument(name)));
        Assert.Equal("neu", (await DriveService.OpenRecentAsync(entry, name => new TestDocument(name), password: Password)).Content);
    }

    [Fact]
    public async Task Speichern_unter_into_ThixDrive_keeps_or_drops_the_password_by_the_type()
    {
        using var data = Protecting();
        var cloud = await SignedInAsync(new SampleCloud());
        var document = DocumentService.Open(Encrypted(data, "Bericht.tse", "Inhalt"), name => new TestDocument(name), Password);

        var kept = await DriveService.SaveAsAsync(document, null, "Bericht.tse");
        Assert.True(kept.IsUploaded);
        using (var uploaded = new MemoryStream(cloud.ContentOf(document.Drive!.ItemId)))
        {
            Assert.Equal(EncryptedFileKind.OfficeAgile, OfficeEncryption.Detect(uploaded));
        }

        Assert.True(document.HasPassword);
        Assert.True((await DriveService.SaveAsAsync(document, null, "Bericht.tst")).IsUploaded);
        Assert.Equal("Inhalt", Encoding.UTF8.GetString(cloud.ContentOf(document.Drive!.ItemId)));
        Assert.False(document.HasPassword);
    }

    [Fact]
    public void An_OpenDocument_package_is_saved_opened_and_recovered_with_its_password()
    {
        using var data = new TestData();
        AppInfo.Initialize(OdfApp, typeof(TestData).Assembly);
        var document = new OdfTestDocument("Test1");
        document.Type("Vertraulich");
        DocumentService.SetPassword(document, Password, Password);

        var path = DocumentService.SaveAs(document, Path.Combine(data.DocumentsFolder, "Bericht.tod"));

        Assert.Equal(EncryptedFileKind.OdfEncrypted, Kind(path));
        Assert.DoesNotContain("Vertraulich", Encoding.UTF8.GetString(File.ReadAllBytes(path)), StringComparison.Ordinal);
        Assert.Equal("Bericht.tod", Assert.Throws<PasswordRequiredException>(() => DocumentService.Open(path, name => new OdfTestDocument(name))).FileName);
        Assert.Throws<WrongPasswordException>(() => DocumentService.Open(path, name => new OdfTestDocument(name), "falsch"));
        var opened = DocumentService.Open(path, name => new OdfTestDocument(name), Password);
        Assert.Equal("Vertraulich", opened.Content);
        Assert.True(opened.HasPassword);

        opened.Type("Geändert");
        DocumentService.Save(opened);
        Assert.Equal(EncryptedFileKind.OdfEncrypted, Kind(path));
        Assert.Equal("Geändert", DocumentService.Open(path, name => new OdfTestDocument(name), Password).Content);

        opened.Type("ungespeichert");
        DocumentService.WriteRecovery(opened);
        var info = Assert.Single(DocumentService.RecoveredDocuments());
        Assert.Equal(EncryptedFileKind.OdfEncrypted, Kind(Directory.EnumerateFiles(AppPaths.RecoveryDirectory, "*.data").Single()));
        Assert.Throws<PasswordRequiredException>(() => DocumentService.Recover(info, name => new OdfTestDocument(name)));
        var recovered = DocumentService.Recover(info, name => new OdfTestDocument(name), Password);
        Assert.Equal("ungespeichert", recovered.Content);
        Assert.True(recovered.HasPassword);

        Assert.False(DocumentService.LosesPassword(recovered, Path.Combine(data.DocumentsFolder, "Kopie.tod")));
        Assert.True(DocumentService.LosesPassword(recovered, Path.Combine(data.DocumentsFolder, "Kopie.tst")));
    }

    private static TestData Protecting()
    {
        var data = new TestData();
        AppInfo.Initialize(ProtectingApp, typeof(TestData).Assembly);
        return data;
    }

    private static string Encrypted(TestData data, string name, string content)
    {
        var path = Path.Combine(data.DocumentsFolder, name);
        File.WriteAllBytes(path, EncryptedBytes(content));
        return path;
    }

    private static byte[] EncryptedBytes(string content)
    {
        using var output = new MemoryStream();
        OfficeEncryption.Encrypt(Encoding.UTF8.GetBytes(content), Password, output);
        return output.ToArray();
    }

    private static string Decrypted(string path)
    {
        using var file = File.OpenRead(path);
        return Encoding.UTF8.GetString(OfficeEncryption.Decrypt(file, Password));
    }

    private static EncryptedFileKind Kind(string path)
    {
        using var file = File.OpenRead(path);
        return OfficeEncryption.Detect(file);
    }

    /// <summary>A compound file with one ordinary stream (an old binary document, renamed).</summary>
    private static byte[] CompoundWithoutEncryption()
    {
        var writer = new CompoundFileWriter();
        writer.AddStream("WordDocument", new byte[700]);
        using var output = new MemoryStream();
        writer.Save(output);
        return output.ToArray();
    }
}

/// <summary>A document whose content is one string, written as a small OpenDocument text package (content.xml, styles.xml, meta.xml).</summary>
internal sealed class OdfTestDocument(string untitledName) : OfficeDocument(untitledName)
{
    private static readonly System.Xml.Linq.XNamespace Text = ThixOffice.Core.Formats.Odf.OdfXml.TextNs;

    public string Content { get; private set; } = "";

    public override bool CanUndo => false;

    public override bool CanRedo => false;

    public void Type(string text)
    {
        Content = text;
        OnContentChanged();
    }

    public override void Undo()
    {
    }

    public override void Redo()
    {
    }

    protected override void Read(Stream stream) =>
        Content = ThixOffice.Core.Formats.Odf.OdfPackage.Read(stream, "kaputt", package => package.Xml("content.xml")!.Descendants(Text + "p").Single().Value);

    protected override void Write(Stream stream)
    {
        var writer = new ThixOffice.Core.Formats.Odf.OdfPackageWriter("application/vnd.oasis.opendocument.text");
        writer.AddXml("content.xml", new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement(Text + "p", Content)));
        writer.AddXml("styles.xml", new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement(Text + "styles")));
        writer.AddXml("meta.xml", ThixOffice.Core.Formats.Odf.OdfPackageWriter.Meta("ThixTest"));
        writer.WriteTo(stream);
    }
}
