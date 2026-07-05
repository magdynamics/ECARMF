using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Text;
using ECARMF.Kernel.Infrastructure.Ai;

namespace ECARMF.Kernel.Tests;

/// <summary>Scanned documents become text fully on-prem: image uploads OCR
/// through the engine built into Windows; PDFs with a text layer never
/// touch OCR; plain text passes through unchanged.</summary>
[SupportedOSPlatform("windows")]
public class OcrReaderTests
{
    private readonly DocumentTextReader _reader = new();

    private static byte[] RenderTextImage(string text)
    {
        using var bitmap = new Bitmap(900, 260);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        using var font = new Font("Arial", 28, FontStyle.Bold);
        graphics.DrawString(text, font, Brushes.Black, 20, 40);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    [Fact]
    public void Image_of_text_is_read_via_ocr()
    {
        var png = RenderTextImage("BUSINESS LICENSE 20260731\nAMOUNT DUE 4500");

        var (success, text) = _reader.ReadText("license-scan.png", png);

        Assert.True(success, text);
        Assert.Contains("LICENSE", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("20260731", text);
        Assert.Contains("4500", text);
    }

    [Fact]
    public void Plain_text_files_pass_through_unchanged()
    {
        var (success, text) = _reader.ReadText("note.txt", Encoding.UTF8.GetBytes("hello world"));

        Assert.True(success);
        Assert.Equal("hello world", text);
    }

    [Fact]
    public void Unreadable_image_reports_a_clear_error_instead_of_silence()
    {
        // A blank image has no text: the pipeline must say so, not return "".
        using var bitmap = new Bitmap(400, 200);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);

        var (success, message) = _reader.ReadText("blank.png", stream.ToArray());

        Assert.False(success);
        Assert.Contains("no readable text", message);
    }
}
