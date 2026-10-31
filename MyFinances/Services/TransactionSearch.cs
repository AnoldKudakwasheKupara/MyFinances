using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;
using MyFinances.ViewModels;

namespace MyFinances.Services
{
    public class TransactionFilter
    {
        public string? Q { get; set; }

        public DateOnly? From { get; set; }

        public DateOnly? To { get; set; }

        public int? AccountId { get; set; }

        public int? CategoryId { get; set; }

        public TransactionType? Type { get; set; }

        /// <summary>date, amount or description.</summary>
        public string Sort { get; set; } = "date";

        public bool Asc { get; set; }

        public int Page { get; set; } = 1;

        public bool IsFiltered =>
            !string.IsNullOrWhiteSpace(Q) || From != null || To != null ||
            AccountId != null || CategoryId != null || Type != null;

        public Dictionary<string, string?> RouteValues(int? page = null, string? sort = null, bool? asc = null) => new()
        {
            ["q"] = Q,
            ["from"] = From?.ToString("yyyy-MM-dd"),
            ["to"] = To?.ToString("yyyy-MM-dd"),
            ["accountId"] = AccountId?.ToString(),
            ["categoryId"] = CategoryId?.ToString(),
            ["type"] = Type?.ToString(),
            ["sort"] = sort ?? Sort,
            ["asc"] = (asc ?? Asc) ? "true" : null,
            ["page"] = (page ?? Page) > 1 ? (page ?? Page).ToString() : null
        };
    }

    public class TransactionSearch
    {
        public const int PageSize = 25;

        private readonly ApplicationDbContext _context;

        public TransactionSearch(ApplicationDbContext context)
        {
            _context = context;
        }

        public IQueryable<Transaction> Query(TransactionFilter filter)
        {
            var query = _context.Transactions
                .AsNoTracking()
                .Include(t => t.Account)
                .Include(t => t.ToAccount)
                .Include(t => t.Category)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.Q))
            {
                var pattern = "%" + filter.Q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

                query = query.Where(t =>
                    EF.Functions.Like(t.Description, pattern, "\\") ||
                    (t.Notes != null && EF.Functions.Like(t.Notes, pattern, "\\")) ||
                    (t.Category != null && EF.Functions.Like(t.Category.Name, pattern, "\\")));
            }

            if (filter.From != null)
                query = query.Where(t => t.Date >= filter.From.Value);

            if (filter.To != null)
                query = query.Where(t => t.Date <= filter.To.Value);

            if (filter.AccountId != null)
                query = query.Where(t => t.AccountId == filter.AccountId || t.ToAccountId == filter.AccountId);

            if (filter.CategoryId != null)
                query = query.Where(t => t.CategoryId == filter.CategoryId);

            if (filter.Type != null)
                query = query.Where(t => t.Type == filter.Type);

            return (filter.Sort, filter.Asc) switch
            {
                ("amount", true) => query.OrderBy(t => t.ReportingAmount ?? t.Amount).ThenBy(t => t.Id),
                ("amount", false) => query.OrderByDescending(t => t.ReportingAmount ?? t.Amount).ThenByDescending(t => t.Id),
                ("description", true) => query.OrderBy(t => t.Description).ThenBy(t => t.Id),
                ("description", false) => query.OrderByDescending(t => t.Description).ThenByDescending(t => t.Id),
                (_, true) => query.OrderBy(t => t.Date).ThenBy(t => t.Id),
                _ => query.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            };
        }

        public Task<PagedResult<Transaction>> PageAsync(TransactionFilter filter) =>
            Query(filter).ToPagedResultAsync(filter.Page, PageSize);

        /// <summary>Income and spending in the filtered set, in the reporting currency.</summary>
        public async Task<(decimal In, decimal Out, int Unconverted)> TotalsAsync(TransactionFilter filter, Currency reporting)
        {
            var rows = await Query(filter)
                .Where(t => t.Type == TransactionType.Income || t.Type == TransactionType.Expense)
                .Select(t => new { t.Type, t.ReportingAmount, t.ReportingCurrency })
                .ToListAsync();

            var usable = rows.Where(r => r.ReportingAmount != null && r.ReportingCurrency == reporting).ToList();

            return (
                usable.Where(r => r.Type == TransactionType.Income).Sum(r => r.ReportingAmount!.Value),
                usable.Where(r => r.Type == TransactionType.Expense).Sum(r => r.ReportingAmount!.Value),
                rows.Count - usable.Count);
        }

        public async Task<byte[]> CsvAsync(TransactionFilter filter)
        {
            var rows = await Query(filter).ToListAsync();

            var csv = new StringBuilder();

            csv.AppendLine(string.Join(",",
                "Date", "Type", "Description", "Category", "Account", "To account",
                "Amount", "Currency", "Amount received", "Received currency",
                "Reporting amount", "Reporting currency", "Rate used", "Rate date", "Notes"));

            foreach (var t in rows)
            {
                csv.AppendLine(string.Join(",",
                    t.Date.ToString("yyyy-MM-dd"),
                    Cell(t.Type.Text()),
                    Cell(t.Description),
                    Cell(t.Category?.Name),
                    Cell(t.Account?.Name),
                    Cell(t.ToAccount?.Name),
                    Number(t.Amount),
                    t.Currency,
                    Number(t.Type == TransactionType.Transfer ? t.ToAmount : null),
                    t.Type == TransactionType.Transfer ? t.ToAccount?.Currency.ToString() : "",
                    Number(t.ReportingAmount),
                    t.ReportingCurrency?.ToString() ?? "",
                    t.ReportingRate?.ToString("0.########", CultureInfo.InvariantCulture) ?? "",
                    t.ReportingRateDate?.ToString("yyyy-MM-dd") ?? "",
                    Cell(t.Notes)));
            }

            return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        }

        public static string Number(decimal? value) =>
            value?.ToString("0.00", CultureInfo.InvariantCulture) ?? "";

        /// <summary>
        /// Quotes a text cell, and stops a spreadsheet from treating text
        /// that starts with = + - or @ as a formula.
        /// </summary>
        public static string Cell(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            if ("=+-@\t\r".Contains(value[0]))
                value = "'" + value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
