using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using System.Text;
using Windows.Data.Pdf;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ECARMF.Kernel.Infrastructure.Ai;

/// <summary>
/// On-prem OCR via the engine that ships inside Windows (Windows.Media.Ocr)
/// — no model downloads, no external service, nothing to install. Scanned
/// PDFs are rendered page by page with the built-in Windows.Data.Pdf
/// renderer before recognition.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public static class WindowsOcr
{
    /// <summary>Pages beyond this are skipped — a safety valve against a
    /// 500-page scan monopolizing the pipeline.</summary>
    private const int MaxPdfPages = 25;

    /// <summary>Render scale for PDF pages: OCR accuracy needs more pixels
    /// than screen rendering.</summary>
    private const double PdfRenderWidth = 2200;

    public static bool IsSupported =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && CreateEngine() is not null;

    public static async Task<(bool Success, string TextOrError)> ReadImageAsync(byte[] imageBytes)
    {
        var engine = CreateEngine();
        if (engine is null)
        {
            return (false, "No OCR language is available on this machine.");
        }

        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(imageBytes.AsBuffer());
            stream.Seek(0);

            var text = await RecognizeStreamAsync(engine, stream);
            return text.Length > 0
                ? (true, text)
                : (false, "OCR found no readable text in the image.");
        }
        catch (Exception ex)
        {
            return (false, $"OCR failed: {ex.Message}");
        }
    }

    public static async Task<(bool Success, string TextOrError)> ReadScannedPdfAsync(byte[] pdfBytes)
    {
        var engine = CreateEngine();
        if (engine is null)
        {
            return (false, "No OCR language is available on this machine.");
        }

        try
        {
            using var pdfStream = new InMemoryRandomAccessStream();
            await pdfStream.WriteAsync(pdfBytes.AsBuffer());
            pdfStream.Seek(0);

            var document = await PdfDocument.LoadFromStreamAsync(pdfStream);
            var text = new StringBuilder();
            var pages = Math.Min((int)document.PageCount, MaxPdfPages);
            for (var i = 0; i < pages; i++)
            {
                using var page = document.GetPage((uint)i);
                using var pageStream = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(pageStream, new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)PdfRenderWidth,
                    DestinationHeight = (uint)(PdfRenderWidth * page.Size.Height / Math.Max(1, page.Size.Width))
                });
                pageStream.Seek(0);
                text.AppendLine(await RecognizeStreamAsync(engine, pageStream));
            }

            var result = text.ToString().Trim();
            return result.Length > 0
                ? (true, result)
                : (false, "OCR found no readable text in the scanned PDF.");
        }
        catch (Exception ex)
        {
            return (false, $"OCR of the scanned PDF failed: {ex.Message}");
        }
    }

    private static OcrEngine? CreateEngine() =>
        OcrEngine.TryCreateFromUserProfileLanguages()
        ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));

    private static async Task<string> RecognizeStreamAsync(OcrEngine engine, IRandomAccessStream stream)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);

        // The engine caps input dimensions; downscale oversized photos.
        var transform = new BitmapTransform();
        var max = (double)OcrEngine.MaxImageDimension;
        if (decoder.PixelWidth > max || decoder.PixelHeight > max)
        {
            var scale = Math.Min(max / decoder.PixelWidth, max / decoder.PixelHeight);
            transform.ScaledWidth = (uint)(decoder.PixelWidth * scale);
            transform.ScaledHeight = (uint)(decoder.PixelHeight * scale);
        }

        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);

        var result = await engine.RecognizeAsync(bitmap);
        var text = new StringBuilder();
        foreach (var line in result.Lines)
        {
            text.AppendLine(line.Text);
        }

        return text.ToString().Trim();
    }
}
