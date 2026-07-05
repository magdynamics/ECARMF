using System.Text;
using ECARMF.Kernel.Application.Ingestion;
using UglyToad.PdfPig;

namespace ECARMF.Kernel.Infrastructure.Ai;

/// <summary>
/// File-to-text stage in front of document extraction. PDFs go through
/// PdfPig text extraction; a PDF with no text layer (a scan) falls back to
/// the Windows built-in OCR engine, as do image uploads (photos and scans
/// of licenses, statements, receipts). Everything else is treated as UTF-8
/// text (.txt, .eml, .md, .csv, pasted content saved to a file, ...).
/// </summary>
public class DocumentTextReader : IDocumentTextReader
{
    private static readonly string[] ImageExtensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"];

    public (bool Success, string TextOrError) ReadText(string fileName, byte[] content)
    {
        if (content.Length == 0)
        {
            return (false, "The uploaded file is empty.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (ImageExtensions.Contains(extension))
        {
            return Ocr(() => WindowsOcr.ReadImageAsync(content));
        }

        if (extension == ".pdf")
        {
            try
            {
                using var document = PdfDocument.Open(content);
                var text = new StringBuilder();
                foreach (var page in document.GetPages())
                {
                    text.AppendLine(page.Text);
                }

                var result = text.ToString().Trim();
                if (result.Length > 0)
                {
                    return (true, result);
                }
            }
            catch (Exception ex)
            {
                return (false, $"Could not read the PDF: {ex.Message}");
            }

            // No text layer: this is a scan — render the pages and OCR them.
            return Ocr(() => WindowsOcr.ReadScannedPdfAsync(content));
        }

        try
        {
            return (true, Encoding.UTF8.GetString(content).TrimStart('﻿'));
        }
        catch (Exception ex)
        {
            return (false, $"Could not decode the file as text: {ex.Message}");
        }
    }

    private static (bool, string) Ocr(Func<Task<(bool, string)>> recognize)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return (false, "OCR requires Windows 10 (build 19041) or later on the host.");
        }

        // The reader port is synchronous; there is no synchronization context
        // in the API pipeline, so blocking on the WinRT task is safe here.
        return recognize().GetAwaiter().GetResult();
    }
}
