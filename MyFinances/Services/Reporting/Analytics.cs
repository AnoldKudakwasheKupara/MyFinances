using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;

namespace MyFinances.Services.Reporting
{
    /// <summary>One income or expense line, in the reporting currency.</summary>
    public record Flow(DateOnly Date, TransactionType Type, int? CategoryId, string Category, string Icon, decimal Amount, string Description, int AccountId);

    public class FlowSet
    {
        public List<Flow> Rows { get; init; } = new();

        /// <summary>Rows with no rate to the reporting currency, left out of every figure.</summary>
        public int Unconverted { get; init; }

        public decimal Income => Rows.Where(r => r.Type == TransactionType.Income).Sum(r => r.Amount);

        public decimal Expenses => Rows.Where(r => r.Type == TransactionType.Expense).Sum(r => r.Amount);

        public decimal DebtIn => Rows.Where(r => r.Type == TransactionType.DebtIn).Sum(r => r.Amount);

        public decimal DebtOut => Rows.Where(r => r.Type == TransactionType.DebtOut).Sum(r => r.Amount);

        public decimal Net => Income - Expenses;

        public IEnumerable<Flow> Of(TransactionType type) => Rows.Where(r => r.Type == type);
    }

    public record CategoryTotal(int? CategoryId, string Name, string Icon, decimal Amount, decimal Share);

    public record SeriesPoint(string Label, DateRange Range, decimal Income, decimal Expenses)
    {
        public decimal Net => Income - Expenses;
    }

    public enum SeriesGrain
    {
        Monthly,
        Quarterly,
        Yearly
    }

    /// <summary>
    /// Sums the ledger for the dashboard and the reports. Every figure
    /// uses each transaction's stored conversion to the reporting
    /// currency — the rate of its own day — and counts what could not
    /// be converted instead of guessing it. Transfers are never income
    /// or spending, and debt principal is kept apart from both.
    /// </summary>
    public class Analytics
    {
        private readonly ApplicationDbContext _context;
        private readonly UserPreferences _preferences;

        public Analytics(ApplicationDbContext context, UserPreferences preferences)
        {
            _context = context;
            _preferences = preferences;
        }

        public async Task<FlowSet> FlowsAsync(DateRange range)
        {
            var settings = await _preferences.GetAsync();
            var reporting = settings.ReportingCurrency;

            var rows = await _context.Transactions
                .AsNoTracking()
                .Where(t => t.Type != TransactionType.Transfer && t.Date >= range.Start && t.Date <= range.End)
                .Select(t => new
                {
                    t.Date,
                    t.Type,
                    t.CategoryId,
                    Category = t.Category != null ? t.Category.Name : null,
                    Icon = t.Category != null ? t.Category.Icon : null,
                    t.ReportingAmount,
                    t.ReportingCurrency,
                    t.Description,
                    t.AccountId
                })
                .ToListAsync();

            var usable = rows.Where(r => r.ReportingAmount != null && r.ReportingCurrency == reporting).ToList();

            return new FlowSet
            {
                Rows = usable
                    .Select(r => new Flow(r.Date, r.Type, r.CategoryId, r.Category ?? "Uncategorised", r.Icon ?? "tag", r.ReportingAmount!.Value, r.Description, r.AccountId))
                    .ToList(),
                Unconverted = rows.Count - usable.Count
            };
        }

        public static List<CategoryTotal> ByCategory(FlowSet flows, TransactionType type)
        {
            var rows = flows.Of(type).ToList();
            var total = rows.Sum(r => r.Amount);

            return rows
                .GroupBy(r => new { r.CategoryId, r.Category, r.Icon })
                .Select(g => new CategoryTotal(
                    g.Key.CategoryId,
                    g.Key.Category,
                    g.Key.Icon,
                    g.Sum(r => r.Amount),
                    total == 0 ? 0 : Math.Round(g.Sum(r => r.Amount) / total * 100, 1)))
                .OrderByDescending(c => c.Amount)
                .ToList();
        }

        /// <summary>Back-to-back periods ending with the one containing <paramref name="end"/>.</summary>
        public static List<DateRange> Buckets(SeriesGrain grain, DateOnly end, int count)
        {
            var list = new List<DateRange>();

            var current = grain switch
            {
                SeriesGrain.Quarterly => Periods.Quarter(end),
                SeriesGrain.Yearly => Periods.Year(end),
                _ => Periods.Month(end)
            };

            for (var i = 0; i < count; i++)
            {
                list.Insert(0, current);
                current = Periods.Previous(current);
            }

            return list;
        }

        public static List<DateRange> MonthsBetween(DateRange range)
        {
            var list = new List<DateRange>();

            for (var m = Periods.MonthStart(range.Start); m <= range.End; m = m.AddMonths(1))
                list.Add(Periods.Month(m));

            return list;
        }

        public async Task<(List<SeriesPoint> Points, int Unconverted)> SeriesAsync(IReadOnlyList<DateRange> buckets, SeriesGrain grain)
        {
            if (buckets.Count == 0)
                return (new List<SeriesPoint>(), 0);

            var flows = await FlowsAsync(new DateRange(buckets[0].Start, buckets[^1].End));

            var points = buckets
                .Select(b => new SeriesPoint(
                    Label(b, grain),
                    b,
                    flows.Of(TransactionType.Income).Where(f => b.Contains(f.Date)).Sum(f => f.Amount),
                    flows.Of(TransactionType.Expense).Where(f => b.Contains(f.Date)).Sum(f => f.Amount)))
                .ToList();

            return (points, flows.Unconverted);
        }

        public static string Label(DateRange range, SeriesGrain grain) => grain switch
        {
            SeriesGrain.Quarterly => $"Q{(range.Start.Month - 1) / 3 + 1} {range.Start.Year}",
            SeriesGrain.Yearly => range.Start.Year.ToString(),
            _ => range.Start.ToString("MMM yyyy")
        };
    }
}
