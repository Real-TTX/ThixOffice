using ThixOffice.Core.Models;

namespace ThixOffice.Core.Formats.OfficeCrypto;

/// <summary>
/// The file is protected with "Mit Kennwort verschlüsseln" and was read without a password: the UI asks for it (PasswordDialog) and
/// reads again. <see cref="FileName"/> is the name the person knows the file by ("Bericht.docx").
/// </summary>
public sealed class PasswordRequiredException(string fileName)
    : BusinessException($"„{fileName}“ ist mit einem Kennwort geschützt.", OfficeEncryption.PasswordField)
{
    public string FileName { get; } = fileName;
}
