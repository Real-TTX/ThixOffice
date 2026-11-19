using DocumentFormat.OpenXml.Packaging;
using ThixOffice.Text.Drawing;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;
using ThixWrite.Core.Tests.Formats;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Images;

/// <summary>Pictures: their headers, inserting and sizing, the layout, docx (inline, floating, media parts).</summary>
public class ImageTests
{
    /// <summary>A PNG of <paramref name="width"/> × <paramref name="height"/> pixels (only the header matters to the core).</summary>
    internal static byte[] Png(int width, int height, int dpi = 0)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        void Chunk(string type, byte[] data)
        {
            bytes.AddRange(BigEndian(data.Length));
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(type));
            bytes.AddRange(data);
            bytes.AddRange([0, 0, 0, 0]); // the CRC is not checked by the core
        }

        Chunk("IHDR", [.. BigEndian(width), .. BigEndian(height), 8, 6, 0, 0, 0]);
        if (dpi > 0)
        {
            var perMetre = (int)Math.Round(dpi / 0.0254);
            Chunk("pHYs", [.. BigEndian(perMetre), .. BigEndian(perMetre), 1]);
        }

        Chunk("IDAT", [0]);
        return [.. bytes];

        static byte[] BigEndian(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
    }

    [Fact]
    public void The_size_and_resolution_come_from_the_header_of_PNG_JPEG_GIF_and_BMP()
    {
        var png = ImageData.FromBytes(Png(300, 150, dpi: 144))!;
        Assert.Equal((ImageFormat.Png, 300, 150), (png.Format, png.PixelWidth, png.PixelHeight));
        Assert.Equal(144, png.DpiX, 0);
        Assert.Equal((150.0, 75.0), (Math.Round(png.NaturalSize.Width), Math.Round(png.NaturalSize.Height)));

        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 1, 0, 72, 0, 72, 0, 0,
                       0xFF, 0xC0, 0, 11, 8, 0, 40, 0, 80, 3, 1, 0x22, 0];
        var decoded = ImageData.FromBytes(jpeg)!;
        Assert.Equal((ImageFormat.Jpeg, 80, 40, 72.0), (decoded.Format, decoded.PixelWidth, decoded.PixelHeight, decoded.DpiX));
        Assert.Equal("image/jpeg", decoded.ContentType);

        byte[] gif = [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 10, 0, 20, 0];
        Assert.Equal((10, 20), (ImageData.FromBytes(gif)!.PixelWidth, ImageData.FromBytes(gif)!.PixelHeight));

        var bmp = new byte[54];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.GetBytes(7).CopyTo(bmp, 18);
        BitConverter.GetBytes(-9).CopyTo(bmp, 22);
        Assert.Equal((7, 9), (ImageData.FromBytes(bmp)!.PixelWidth, ImageData.FromBytes(bmp)!.PixelHeight));

        Assert.Null(ImageData.FromBytes([1, 2, 3, 4]));
        Assert.Null(ImageData.FromBytes("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray()));
        Assert.Equal(ImageData.FromBytes(Png(300, 150)), ImageData.FromBytes(Png(300, 150))); // the same bytes: the same picture
    }

    [Fact]
    public void An_inserted_picture_is_selected_fits_the_text_width_and_sizes_with_undo()
    {
        var (session, layout) = Editor(Create("Text davor"));
        session.SetCaret(At(0, 5));
        session.InsertImage(ImageData.FromBytes(Png(2000, 1000))!); // 1500 pt wide at 96 dpi: wider than the text

        var (position, image) = session.SelectedImage!.Value;
        Assert.Equal(At(0, 5), position);
        var width = session.Document.FinalSection.ContentWidth;
        Assert.Equal(width, image.Width, 2);
        Assert.Equal(width / 2, image.Height, 2);

        session.SetImageSize(100, 50);
        Assert.Equal((100.0, 50.0), (session.SelectedImage!.Value.Image.Width, session.SelectedImage!.Value.Image.Height));
        session.SetImageDescription("Ein Diagramm");
        Assert.Equal("Ein Diagramm", session.SelectedImage!.Value.Image.Description);

        // The line is as tall as the picture; the display list draws it there.
        var line = layout.Pages[0].Lines[0];
        Assert.True(line.Line.Height >= 50);
        var item = Assert.Single(layout.GetDisplayList(0).Items.OfType<ImageItem>());
        Assert.Equal((100.0, 50.0), (item.Width, item.Height));
        var rect = layout.GetObjectRect(position)!.Value;
        Assert.Equal(item.X, rect.X, 3);
        Assert.Equal(position, layout.ImageAt(0, rect.X + 10, rect.Y + 10));

        session.ResetImageSize();
        Assert.Equal(width, session.SelectedImage!.Value.Image.Width, 2);
        session.Undo();
        session.Undo();
        Assert.Equal(100, session.SelectedImage!.Value.Image.Width, 2);
        AssertInvariants(session);
    }

    [Fact]
    public void A_picture_in_a_cell_fits_the_cell()
    {
        var (session, _) = Editor(Create(""));
        session.InsertTable(1, 2);
        session.InsertImage(ImageData.FromBytes(Png(2000, 1000))!);

        var image = session.SelectedImage!.Value.Image;
        Assert.True(image.Width < session.Document.FinalSection.ContentWidth / 2);
        Assert.Equal(session.AvailableWidth(session.Selection.Start.Block), image.Width, 2);
    }

    [Fact]
    public void Pictures_are_written_once_per_file_and_read_back_equal()
    {
        var (session, _) = Editor(Create("Bilder: "));
        session.SetCaret(At(0, 8));
        var picture = ImageData.FromBytes(DocxFixtures.Png)!;
        session.InsertImage(picture);
        session.SetCaret(session.Selection.End);
        session.InsertImage(picture);
        session.SetImageDescription("Zweites Bild");

        var bytes = DocxTestSupport.Write(session.Document);
        DocxTestSupport.AssertValid(bytes);
        using (var package = WordprocessingDocument.Open(new MemoryStream(bytes), false))
        {
            Assert.Single(package.MainDocumentPart!.ImageParts);
        }

        var read = DocxTestSupport.Read(bytes);
        DocxTestSupport.AssertSameContent(session.Document, read);
        Assert.Empty(read.UnsupportedContent);

        // Written again from the file it came from: the drawings as read, still one part.
        var again = DocxTestSupport.Write(read);
        DocxTestSupport.AssertValid(again);
        using var second = WordprocessingDocument.Open(new MemoryStream(again), false);
        Assert.Single(second.MainDocumentPart!.ImageParts);
        Assert.Equal(2, second.MainDocumentPart.Document!.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>().Select(properties => properties.Id!.Value).Distinct().Count());
    }

    [Fact]
    public void A_floating_picture_is_placed_by_its_anchor_and_keeps_it()
    {
        const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var anchor = "<w:r><w:drawing><wp:anchor distT=\"0\" distB=\"0\" distL=\"114300\" distR=\"114300\" simplePos=\"0\" relativeHeight=\"1\" behindDoc=\"0\" locked=\"0\" layoutInCell=\"1\" allowOverlap=\"1\">"
                     + "<wp:simplePos x=\"0\" y=\"0\"/><wp:positionH relativeFrom=\"column\"><wp:posOffset>1000</wp:posOffset></wp:positionH><wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>0</wp:posOffset></wp:positionV>"
                     + "<wp:extent cx=\"952500\" cy=\"476250\"/><wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/><wp:wrapSquare wrapText=\"bothSides\"/><wp:docPr id=\"7\" name=\"Bild 7\" descr=\"Logo\"/>"
                     + "<wp:cNvGraphicFramePr/><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:nvPicPr><pic:cNvPr id=\"0\" name=\"logo.png\"/><pic:cNvPicPr/></pic:nvPicPr>"
                     + "<pic:blipFill><a:blip r:embed=\"rIdBild\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"952500\" cy=\"476250\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic>"
                     + "</a:graphicData></a:graphic></wp:anchor></w:drawing></w:r>";
        var xml = $"<w:document xmlns:w=\"{W}\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" "
                  + "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
                  + $"<w:body><w:p>{anchor}<w:r><w:t>Text</w:t></w:r></w:p></w:body></w:document>";
        var bytes = WithImage(DocxFixtures.CreateRaw(xml), "rIdBild");

        var document = DocxTestSupport.Read(bytes);
        var image = Assert.IsType<ImageInline>(document.ParagraphAt(0)!.Inlines[0]);
        Assert.True(image.IsFloating);
        Assert.Equal(75, image.Width, 2);
        Assert.Equal("Logo", image.Description);
        Assert.Equal(DrawingWrap.Square, image.Anchor!.Wrap); // placed by its anchor, the text flows around it
        Assert.Equal(DrawingFrame.Column, image.Anchor.HorizontalFrame);
        Assert.Empty(document.UnsupportedContent);

        var written = DocxTestSupport.Write(document);
        DocxTestSupport.AssertValid(written);
        Assert.Contains("wp:anchor", DocxTestSupport.DocumentXml(written));
    }

    private static byte[] WithImage(byte[] docx, string relationshipId)
    {
        using var stream = new MemoryStream();
        stream.Write(docx);
        using (var package = WordprocessingDocument.Open(stream, true))
        {
            var part = package.MainDocumentPart!.AddImagePart(ImagePartType.Png, relationshipId);
            part.FeedData(new MemoryStream(DocxFixtures.Png));
        }

        return stream.ToArray();
    }
}
