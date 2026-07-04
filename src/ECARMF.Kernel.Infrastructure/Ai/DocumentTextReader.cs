using System.Text;
using ECARMF.Kernel.Application.Ingestion;
using UglyToad.PdfPig;

namespace ECARMF.Kernel.Infrastructure.Ai;

/// <summary>
/// File-to-text stage in front of document extraction: PDFs go through
/// PdfPig text extraction; everything else is treated as UTF-8 text
/// (.txt, .eml, .md, .csv, pasted content saved to a file, ...).
/// </summary>
public class DocumentTextReader : IDocumentTextReader
{
    public (bool Success, string TextOrError) ReadText(string fileName, byte[] content)
    {
        if (content.Length == 0)
        {
            return (false, "The uploaded file is empty.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();

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
                return result.Length > 0
                    ? (true, result)
                    : (false, "No text could be extracted from the PDF (it may be a scanned image without a text layer).");
            }
            catch (Exception ex)
            {
                return (false, $"Could not read the PDF: {ex.Message}");
            }
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
}
