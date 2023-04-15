
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;


namespace CorporAIte.Extensions
{
    public static class FileExtensions
    {

        public static List<string> ConvertPdfToLines(this byte[] pdfBytes)
        {
            using var stream = new MemoryStream(pdfBytes);
            using var pdfReader = new PdfReader(stream);
            using var pdfDocument = new PdfDocument(pdfReader);

            var paragraphs = new List<string>();

            for (int pageNum = 1; pageNum <= pdfDocument.GetNumberOfPages(); pageNum++)
            {
                var page = pdfDocument.GetPage(pageNum);
                var strategy = new LocationTextExtractionStrategy();
                var pageText = PdfTextExtractor.GetTextFromPage(page, strategy);

                var regex = new Regex(@"(?<=\r\n)(?=[A-Z0-9])");
                var rawParagraphs = regex.Split(pageText);

                foreach (var rawParagraph in rawParagraphs)
                {
                    string paragraph = rawParagraph.Trim();

                    if (!string.IsNullOrWhiteSpace(paragraph))
                    {
                        paragraphs.Add(paragraph);
                    }
                }
            }

            return paragraphs;
        }

        public static List<string> ConvertDocxToLines(this byte[] docxBytes)
        {
            var result = new List<string>();

            using (var memoryStream = new MemoryStream(docxBytes))
            {
                using (var wordDocument = WordprocessingDocument.Open(memoryStream, false))
                {
                    var body = wordDocument.MainDocumentPart.Document.Body;
                    var paragraphs = body.Descendants<Paragraph>();


                    foreach (var paragraph in paragraphs)
                    {
                        var sb = new StringBuilder();
                        var runs = paragraph.Elements<Run>();

                        foreach (var run in runs)
                        {
                            var textElements = run.Elements<Text>();
                            var text = string.Join(string.Empty, textElements.Select(t => t.Text));
                            sb.Append(text);
                        }

                        if (!string.IsNullOrEmpty(sb.ToString()))
                        {
                            result.Add(sb.ToString());
                        }

                    }

                    return result;
                }
            }
        }

        public static List<string> ConvertCsvToList(this byte[] csvBytes)
        {
            var records = new List<string>();

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                PrepareHeaderForMatch = args => args.Header.ToLower(),
                TrimOptions = TrimOptions.Trim,
                IgnoreBlankLines = true,
                MissingFieldFound = null
            };
            using (var memoryStream = new MemoryStream(csvBytes))
            using (var reader = new StreamReader(memoryStream))
            using (var csv = new CsvReader(reader, config))
            {
                var recordsList = csv.GetRecords<dynamic>();

                foreach (var record in recordsList)
                {
                    var recordDictionary = record as IDictionary<string, object>;
                    var formattedRecord = new List<string>();

                    foreach (var entry in recordDictionary)
                    {
                        formattedRecord.Add($"{entry.Key}:{entry.Value.ToString().Trim()}");
                    }

                    records.Add(string.Join(";", formattedRecord));
                }
            }

            return records;
        }
    }
}