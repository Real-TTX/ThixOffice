using ThixOffice.Desktop.Dialogs;

namespace ThixWrite.Dialogs;

/// <summary>The frame of ThixWrite's dialogs: the shared <see cref="OfficeDialog"/> (heading, content, OK · Abbrechen).</summary>
public abstract class WriteDialog(string title, double width) : OfficeDialog(title, width);
