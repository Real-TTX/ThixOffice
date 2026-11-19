using ThixShow.Core.Editing;
using ThixShow.Core.Model;

namespace ThixShow.Core.Tests.Editing;

/// <summary>Zuschneiden keeps the visible part of a picture in its place: the frame follows the cut sides.</summary>
public class CropTests
{
    [Fact]
    public void Cropping_the_left_side_moves_the_frame_and_resetting_brings_it_back()
    {
        var session = new EditingSession(TestDecks.Blank());
        session.GoToSlide(session.Presentation.Slides[0].Id);
        var bounds = new EmuRect(Units.PointsToEmu(100), Units.PointsToEmu(100), Units.PointsToEmu(200), Units.PointsToEmu(100));
        var id = session.InsertPicture(ImageData.From(TestImages.Png(40, 20)), bounds);

        session.CropPicture(id, new RelativeRect(20000, 0, 0, 10000));
        var cropped = session.CurrentSlide!.FindShape(id)!.Transform!;
        Assert.Equal(140, Units.EmuToPoints(cropped.X), 1);
        Assert.Equal(160, Units.EmuToPoints(cropped.Width), 1);
        Assert.Equal(100, Units.EmuToPoints(cropped.Y), 1);
        Assert.Equal(90, Units.EmuToPoints(cropped.Height), 1);
        Assert.Equal(new RelativeRect(20000, 0, 0, 10000), ((PictureShape)session.CurrentSlide!.FindShape(id)!).Picture.Crop);

        session.CropPicture(id, new RelativeRect(0, 0, 0, 0));
        var reset = session.CurrentSlide!.FindShape(id)!.Transform!;
        Assert.Equal(100, Units.EmuToPoints(reset.X), 1);
        Assert.Equal(200, Units.EmuToPoints(reset.Width), 1);
        Assert.Equal(100, Units.EmuToPoints(reset.Height), 1);
    }
}
