
using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CorporAIte.Extensions
{
    public static class FileExtensions
    {
        public static List<string> ConvertDocxToText(this byte[] docxBytes)
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