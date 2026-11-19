namespace ThixOffice.Core.Models;

/// <summary>
/// The connection of this app on this computer to ThixCloud (MatCloud): the address, the account and the device as the cloud
/// named them, and the device token protected for this user (Windows DPAPI; on macOS it lives in the Keychain and this holds a
/// marker). Only <see cref="Business.CloudService"/> writes it; a password is never stored.
/// </summary>
public sealed record CloudSettings
{
    /// <summary>The address of the cloud (https://cloud.example.com); empty = never connected. Kept after signing out.</summary>
    public string ServerUrl { get; init; } = "";

    public string UserName { get; init; } = "";

    public string DisplayName { get; init; } = "";

    /// <summary>The name of this computer in the cloud's list of devices.</summary>
    public string DeviceName { get; init; } = "";

    public long DeviceId { get; init; }

    public string ProtectedToken { get; init; } = "";

    public bool IsConnected => ServerUrl.Length > 0 && DeviceId > 0;

    /// <summary>"Anna Beispiel (anna)", or the user name alone.</summary>
    public string AccountText => DisplayName.Length > 0 && DisplayName != UserName ? $"{DisplayName} ({UserName})" : UserName;
}
