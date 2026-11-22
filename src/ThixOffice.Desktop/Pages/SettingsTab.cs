using Avalonia.Controls;

namespace ThixOffice.Desktop.Pages;

/// <summary>A tab of Einstellungen that an app adds (<see cref="OfficeApplication.CreateSettingsTabs"/>): its id (for <see cref="SettingsPage.ShowTab"/>), header and content.</summary>
public sealed record SettingsTab(string Id, string Header, Func<Control> Create);
