using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;

namespace Orvanta.Jobs.Core.Cv;

public sealed class UnsupportedCvException(string message) : Exception(message);

/// <summary>Extracts plain text from PDF, DOCX or TXT CVs.</summary>
public static class CvTextExtractor
{
    public const long MaxBytes = 5 * 1024 * 1024;

    public static readonly IReadOnlyDictionary<string, string> SupportedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".txt"] = "text/plain"
    };

    public static string Extract(string fileName, byte[] content)
    {
        var extension = Path.GetExtension(fileName);
        var text = extension.ToLowerInvariant() switch
        {
            ".pdf" => FromPdf(content),
            ".docx" => FromDocx(content),
            ".txt" => Encoding.UTF8.GetString(content),
            _ => throw new UnsupportedCvException("Upload your CV as a PDF, DOCX or TXT file.")
        };

        text = Normalize(text);
        if (text.Length < 50)
        {
            throw new UnsupportedCvException("We couldn't read text from this CV. If it's a scanned image, export it as a text-based PDF or DOCX.");
        }

        return text;
    }

    private static string FromPdf(byte[] content)
    {
        try
        {
            using var document = PdfDocument.Open(content);
            var builder = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                builder.AppendLine(string.Join(' ', page.GetWords().Select(w => w.Text)));
            }
            return builder.ToString();
        }
        catch (Exception ex) when (ex is not UnsupportedCvException)
        {
            throw new UnsupportedCvException("This PDF couldn't be opened. It may be password-protected or damaged.");
        }
    }

    private static string FromDocx(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content);
            using var document = WordprocessingDocument.Open(stream, false);
            var body = document.MainDocumentPart?.Document?.Body;
            if (body is null) return string.Empty;

            return string.Join('\n', body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().Select(p => p.InnerText));
        }
        catch (Exception ex) when (ex is not UnsupportedCvException)
        {
            throw new UnsupportedCvException("This DOCX couldn't be opened. Please re-save it from Word and try again.");
        }
    }

    private static string Normalize(string text) =>
        string.Join('\n', text.Replace("\r", string.Empty)
            .Split('\n')
            .Select(line => string.Join(' ', line.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .Where(line => line.Length > 0));
}
