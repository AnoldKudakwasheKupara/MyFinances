using System.Globalization;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MyFinances.Services.Reporting
{
    /// <summary>
    /// Writes a report out as CSV or PDF from the same cells the page
    /// shows: the PDF prints each cell's text, the CSV its value.
    /// </summary>
    public static class ReportExport
    {
        public static byte[] Csv(Report report)
        {
            var csv = new StringBuilder();

            csv.AppendLine(TransactionSearch.Cell(report.Definition.Title) + "," + TransactionSearch.Cell(report.Period));
            csv.AppendLine(TransactionSearch.Cell($"Amounts in {report.Currency} unless a currency is shown"));
            csv.AppendLine();
            csv.AppendLine(string.Join(",", report.Columns.Select(c => TransactionSearch.Cell(c.Header))));

            foreach (var row in report.Rows)
                csv.AppendLine(Line(row));

            if (report.Totals != null)
                csv.AppendLine(Line(report.Totals));

            if (report.Notes.Any())
            {
                csv.AppendLine();

                foreach (var note in report.Notes)
                    csv.AppendLine(TransactionSearch.Cell(note));
            }

            return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        }

        private static string Line(IEnumerable<ReportCell> cells) =>
            string.Join(",", cells.Select(c =>
                c.Value != null
                    ? c.Value.Value.ToString("0.########", CultureInfo.InvariantCulture)
                    : TransactionSearch.Cell(c.Text)));

        public static byte[] Pdf(Report report, string ownerName)
        {
            var navy = "#0f1f3d";
            var teal = "#0f766e";
            var muted = "#64748b";
            var line = "#e3e8ef";

            var landscape = report.Columns.Count > 6;

            return Document.Create(document =>
            {
                document.Page(page =>
                {
                    page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
                    page.Margin(36);
                    page.DefaultTextStyle(t => t.FontSize(9).FontFamily(DocumentFonts.Family).FontColor(navy));

                    page.Header().PaddingBottom(14).Column(column =>
                    {
                        column.Item().Text("MyFinances").FontSize(10).SemiBold().FontColor(teal);
                        column.Item().Text(report.Definition.Title).FontSize(18).Bold();
                        column.Item().Text($"{report.Period} · {ownerName} · amounts in {report.Currency} unless shown").FontColor(muted);
                    });

                    page.Content().Column(column =>
                    {
                        if (!report.Rows.Any())
                        {
                            column.Item().PaddingVertical(20).Text("Nothing was recorded for this report in the period chosen.").FontColor(muted);
                        }
                        else
                        {
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    foreach (var c in report.Columns)
                                    {
                                        if (c.Numeric)
                                            columns.RelativeColumn(2);
                                        else
                                            columns.RelativeColumn(3);
                                    }
                                });

                                table.Header(header =>
                                {
                                    foreach (var c in report.Columns)
                                    {
                                        var cell = header.Cell().Background("#f4f6f9").PaddingVertical(5).PaddingHorizontal(4);
                                        (c.Numeric ? cell.AlignRight() : cell).Text(c.Header.ToUpperInvariant()).FontSize(7).SemiBold().FontColor(muted);
                                    }
                                });

                                void Row(List<ReportCell> cells, bool bold)
                                {
                                    for (var i = 0; i < report.Columns.Count; i++)
                                    {
                                        var text = i < cells.Count ? cells[i].Text : "";
                                        var cell = table.Cell().BorderBottom(bold ? 0 : 0.5f).BorderTop(bold ? 1 : 0).BorderColor(line).PaddingVertical(4).PaddingHorizontal(4);
                                        var span = (report.Columns[i].Numeric ? cell.AlignRight() : cell).Text(text);

                                        if (bold)
                                            span.SemiBold();
                                    }
                                }

                                foreach (var row in report.Rows)
                                    Row(row, false);

                                if (report.Totals != null)
                                    Row(report.Totals, true);
                            });
                        }

                        foreach (var note in report.Notes)
                            column.Item().PaddingTop(6).Text(note).FontSize(8).FontColor(muted);
                    });

                    page.Footer().Row(row =>
                    {
                        row.RelativeItem().Text($"Produced {Clock.Now:d MMM yyyy, HH:mm}").FontSize(7).FontColor(muted);
                        row.RelativeItem().AlignRight().Text(t =>
                        {
                            t.DefaultTextStyle(s => s.FontSize(7).FontColor(muted));
                            t.Span("Page ");
                            t.CurrentPageNumber();
                            t.Span(" of ");
                            t.TotalPages();
                        });
                    });
                });
            }).GeneratePdf();
        }
    }

    /// <summary>
    /// The PDF uses the same Montserrat as the app, loaded from the
    /// files shipped with it so nothing depends on the server's fonts.
    /// </summary>
    public static class DocumentFonts
    {
        public const string Family = "Montserrat";

        public static void Register(IWebHostEnvironment environment, ILogger logger)
        {
            var folder = Path.Combine(environment.WebRootPath, "fonts");

            foreach (var file in new[] { "Montserrat-Regular.ttf", "Montserrat-SemiBold.ttf", "Montserrat-Bold.ttf" })
            {
                var path = Path.Combine(folder, file);

                if (!File.Exists(path))
                {
                    logger.LogWarning("PDF font {Font} is missing; reports will use a fallback font.", file);
                    continue;
                }

                using var stream = File.OpenRead(path);
                QuestPDF.Drawing.FontManager.RegisterFont(stream);
            }
        }
    }
}
