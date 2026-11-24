namespace ThixOffice.Core.Models;

/// <summary>
/// A rule of the business layer was not met (validation, permission, state). The message is German and meant for the
/// user; <see cref="Field"/> names the input it belongs to, so the UI can show it right there. Subclasses name cases the UI
/// treats on their own (a wrong document password).
/// </summary>
public class BusinessException(string message, string? field = null) : Exception(message)
{
    public string? Field { get; } = field;
}
