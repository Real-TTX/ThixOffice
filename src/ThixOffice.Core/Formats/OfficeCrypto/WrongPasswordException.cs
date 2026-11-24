using ThixOffice.Core.Models;

namespace ThixOffice.Core.Formats.OfficeCrypto;

/// <summary>The password does not open the encrypted document (the verifier did not match). Nothing else is tried.</summary>
public sealed class WrongPasswordException() : BusinessException(Text, OfficeEncryption.PasswordField)
{
    public const string Text = "Das Kennwort ist falsch. Achten Sie auf Groß- und Kleinschreibung.";
}
