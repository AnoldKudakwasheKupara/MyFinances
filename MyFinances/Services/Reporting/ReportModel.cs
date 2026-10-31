using MyFinances.Entities;

namespace MyFinances.Services.Reporting
{
    /// <summary>
    /// One cell: the text people see and, for numbers, the value behind
    /// it. The screen and the PDF print the text; the CSV writes the
    /// value. Both come from this one object, so an export can never
    /// disagree with the page.
    /// </summary>
    public record ReportCell(string Text, decimal? Value = null)
    {
        public static ReportCell Of(string? text) => new(text ?? "");

        public static ReportCell Money(decimal value, Currency currency) =>
            new(Services.Money.Format(value, currency), value);

        public static ReportCell Number(decimal value, string format = "0.#") =>
            new(value.ToString(format), value);

        public static ReportCell Percent(decimal? value) =>
            value == null ? new("—") : new($"{value:0.#}%", value);

        public static implicit operator ReportCell(string text) => new(text);
    }

    public record ReportColumn(string Header, bool Numeric = false);

    public record ReportChart(string Kind, object Data);

    public record ReportDefinition(string Key, string Title, string Icon, string Description, bool UsesRange = true);

    public class Report
    {
        public ReportDefinition Definition { get; init; } = default!;

        public DateRange Range { get; init; } = default!;

        public Currency Currency { get; init; }

        public List<ReportColumn> Columns { get; init; } = new();

        public List<List<ReportCell>> Rows { get; } = new();

        public List<ReportCell>? Totals { get; set; }

        public List<string> Notes { get; } = new();

        public ReportChart? Chart { get; set; }

        public string Period => Definition.UsesRange
            ? $"{Range.Start:d MMM yyyy} – {Range.End:d MMM yyyy}"
            : $"As of {Range.End:d MMM yyyy}";

        public void Add(params ReportCell[] cells) => Rows.Add(cells.ToList());
    }
}
