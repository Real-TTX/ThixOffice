using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Business;

/// <summary>
/// The entry of an app in "Installierte Apps" (HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\&lt;App&gt;). ThixLauncher
/// finds an installed app by exactly these values: DisplayVersion (the version it compares), InstallLocation (the program
/// folder), DisplayIcon ("&lt;exe&gt;,0", the program if the folder is not named), UninstallString and QuietUninstallString.
/// </summary>
public static class UninstallEntry
{
    public const string UninstallArgument = "--uninstall";
    public const string QuietArgument = "--quiet";

    public static void Write(IUserRegistry registry, OfficeAppDefinition definition, string executable, string programFolder, string version, long programSize)
    {
        var key = definition.UninstallKey;
        registry.SetValue(key, "DisplayName", definition.Name);
        registry.SetValue(key, "DisplayVersion", version);
        registry.SetValue(key, "Publisher", OfficeAppDefinition.Publisher);
        registry.SetValue(key, "DisplayIcon", executable + ",0");
        registry.SetValue(key, "InstallLocation", programFolder);
        registry.SetValue(key, "UninstallString", $"\"{executable}\" {UninstallArgument}");
        registry.SetValue(key, "QuietUninstallString", $"\"{executable}\" {UninstallArgument} {QuietArgument}");
        registry.SetValue(key, "URLInfoAbout", $"https://github.com/{OfficeAppDefinition.Repository}");
        registry.SetValue(key, "NoModify", 1, RegistryValueType.DWord);
        registry.SetValue(key, "NoRepair", 1, RegistryValueType.DWord);
        registry.SetValue(key, "EstimatedSize", (int)Math.Min(int.MaxValue, programSize / 1024), RegistryValueType.DWord);
        registry.SetValue(key, "InstallDate", DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
    }

    public static void Remove(IUserRegistry registry, OfficeAppDefinition definition) => registry.DeleteTree(definition.UninstallKey);

    public static bool Exists(IUserRegistry registry, OfficeAppDefinition definition) => registry.KeyExists(definition.UninstallKey);

    public static string? InstalledVersion(IUserRegistry registry, OfficeAppDefinition definition) =>
        registry.GetValue(definition.UninstallKey, "DisplayVersion") as string;
}
