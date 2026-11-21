using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MatDesktop;
using ThixOffice.Pim.Model;

namespace ThixOffice.Pim.Desktop;

/// <summary>
/// The round picture of a person: its picture (from the store, by <see cref="MatCloud.Contract.Contacts.ContactData.PhotoId"/>) or its initials on the
/// soft accent. Several on one page work (no static state); pictures are decoded once per id and size by the caller's <see cref="PhotoSource"/>.
/// </summary>
public sealed class PersonAvatar : Border
{
    private readonly TextBlock _initials = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill };

    public PersonAvatar(double size = 32)
    {
        Width = Height = size;
        CornerRadius = new CornerRadius(size / 2);
        ClipToBounds = true;
        _initials.FontSize = Math.Max(10, size * 0.38);
        this.Use(BackgroundProperty, "Mat.AccentSoft");
        _initials.Use(TextBlock.ForegroundProperty, "Mat.AccentText");
        Child = new Panel { Children = { _initials, _image } };
    }

    /// <summary>Where pictures come from: id → bytes (null: none). The avatar shows the initials while there is none.</summary>
    public Func<string, byte[]?>? PhotoSource { get; set; }

    /// <summary>The initials shown (tests).</summary>
    public string Initials => _initials.Text ?? "";

    /// <summary>True while a picture shows.</summary>
    public bool ShowsPhoto => _image.Source is not null;

    /// <summary>Shows <paramref name="person"/> (null: nobody, empty).</summary>
    public void Show(Person? person)
    {
        _initials.Text = person?.Initials ?? "";
        var old = _image.Source as IDisposable;
        _image.Source = null;
        old?.Dispose();
        if (person is { Data.PhotoId: { Length: > 0 } id } && PhotoSource?.Invoke(id) is { Length: > 0 } bytes)
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                _image.Source = Bitmap.DecodeToWidth(stream, (int)Math.Max(32, Width * 2));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException) // Avalonia/Skia throw anything at bytes they cannot decode
            {
                _image.Source = null; // a picture Skia cannot read: the initials stay
                ThixOffice.Core.Infrastructure.Log.Warning($"A picture could not be shown: {exception.GetType().Name}");
            }
        }

        _initials.IsVisible = _image.Source is null;
    }
}
