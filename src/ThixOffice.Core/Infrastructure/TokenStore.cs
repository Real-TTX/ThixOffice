using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace ThixOffice.Core.Infrastructure;

/// <summary>
/// Keeps secrets (later: the device token of ThixCloud) where only this user can read them: Windows DPAPI (the protected
/// value is kept by the caller), macOS Keychain (generic password, service = the app's bundle id, one item per account).
/// Tokens never go into a log. Cloud sign-in comes in a later phase; the store is here so that it is the same for every app.
/// </summary>
internal static class TokenStore
{
    private static Dictionary<string, string>? _testTokens;

    private static string KeychainService => AppInfo.Definition.MacBundleId;

    private static byte[] Entropy => Encoding.UTF8.GetBytes(AppInfo.ProductName + ".Token");

    /// <summary>Stores the token; returns the value to keep (Windows) or a marker (Keychain).</summary>
    public static string Save(string account, string token)
    {
        if (_testTokens is not null)
        {
            _testTokens[account] = token;
            return "test";
        }

        if (OperatingSystem.IsWindows())
        {
            return Convert.ToBase64String(Protect(token));
        }

        if (OperatingSystem.IsMacOS())
        {
            RunSecurity("add-generic-password", "-U", "-a", account, "-s", KeychainService, "-w", token);
            return "keychain";
        }

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
    }

    public static string? Read(string account, string protectedValue)
    {
        if (_testTokens is not null)
        {
            return _testTokens.GetValueOrDefault(account);
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                return protectedValue.Length == 0 ? null : Unprotect(Convert.FromBase64String(protectedValue));
            }

            if (OperatingSystem.IsMacOS())
            {
                var token = RunSecurity("find-generic-password", "-a", account, "-s", KeychainService, "-w").Trim();
                return token.Length == 0 ? null : token;
            }

            return protectedValue.Length == 0 ? null : Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or InvalidOperationException or IOException)
        {
            Log.Warning($"A token could not be read: {exception.GetType().Name}");
            return null;
        }
    }

    public static void Delete(string account)
    {
        if (_testTokens is not null)
        {
            _testTokens.Remove(account);
            return;
        }

        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            RunSecurity("delete-generic-password", "-a", account, "-s", KeychainService);
        }
        catch (InvalidOperationException)
        {
            // not there
        }
    }

    internal static void UseMemoryForTests(bool enabled) => _testTokens = enabled ? new Dictionary<string, string>() : null;

    [SupportedOSPlatform("windows")]
    private static byte[] Protect(string token) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static string Unprotect(byte[] data) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser));

    private static string RunSecurity(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/security")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("security could not be started");
        var output = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"security {arguments[0]} failed ({process.ExitCode})");
        }

        return output;
    }
}
