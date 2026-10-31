using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;

namespace MyFinances.Services
{
    public record NetWorthLine(string Group, string Name, Currency Currency, decimal Original, decimal? Converted, bool IsLiability, string? Url = null);

    public class NetWorthStatement
    {
        public DateOnly AsOf { get; init; }

        public Currency Currency { get; init; }

        public List<NetWorthLine> Lines { get; } = new();

        public decimal Assets => Lines.Where(l => !l.IsLiability).Sum(l => l.Converted ?? 0);

        public decimal Liabilities => Lines.Where(l => l.IsLiability).Sum(l => l.Converted ?? 0);

        public decimal NetWorth => Assets - Liabilities;

        public List<NetWorthLine> Unconverted => Lines.Where(l => l.Converted == null).ToList();

        public decimal GroupTotal(string group) => Lines.Where(l => l.Group == group).Sum(l => l.Converted ?? 0);
    }

    /// <summary>
    /// Net worth = what you have − what you owe, on a given day, in the
    /// reporting currency at the rates recorded on or before that day.
    ///
    /// Counted once each, from where they are tracked:
    ///   + account balances (Accounts)
    ///   + money owed to you, still outstanding (Debts &amp; Loans)
    ///   + other assets at their latest valuation (Assets &amp; Liabilities)
    ///   − money you owe, still outstanding (Debts &amp; Loans)
    ///   − other liabilities at their latest valuation (Assets &amp; Liabilities)
    /// A balance with no rate to the reporting currency is listed but
    /// left out of the totals, never added at one-to-one.
    /// </summary>
    public class NetWorthService
    {
        public const string AccountsGroup = "Accounts";
        public const string OwedToMeGroup = "Owed to me";
        public const string AssetsGroup = "Other assets";
        public const string IOweGroup = "Debts I owe";
        public const string LiabilitiesGroup = "Other liabilities";

        private readonly ApplicationDbContext _context;
        private readonly BalanceService _balances;
        private readonly CurrencyService _currency;
        private readonly UserPreferences _preferences;

        public NetWorthService(
            ApplicationDbContext context,
            BalanceService balances,
            CurrencyService currency,
            UserPreferences preferences)
        {
            _context = context;
            _balances = balances;
            _currency = currency;
            _preferences = preferences;
        }

        public async Task<NetWorthStatement> StatementAsync(DateOnly? asOf = null, RateBook? rates = null)
        {
            var day = asOf ?? UserPreferences.Today;
            var settings = await _preferences.GetAsync();
            rates ??= await _currency.LoadAsync();

            var statement = new NetWorthStatement { AsOf = day, Currency = settings.ReportingCurrency };

            void Add(string group, string name, Currency currency, decimal amount, bool liability, string? url = null) =>
                statement.Lines.Add(new NetWorthLine(
                    group, name, currency, amount,
                    rates.Convert(amount, currency, settings.ReportingCurrency, day),
                    liability, url));

            foreach (var b in await _balances.BalancesAsync(includeArchived: true, asOf: day))
            {
                if (b.Account.IsArchived && b.Balance == 0)
                    continue;

                Add(AccountsGroup, b.Account.Name, b.Currency, b.Balance, false, $"/Accounts/Details/{b.Account.Id}");
            }

            var debts = await _context.Debts.AsNoTracking()
                .Where(d => d.StartDate <= day && d.Status != DebtStatus.WrittenOff)
                .Select(d => new
                {
                    d.Id,
                    d.Description,
                    d.Counterparty,
                    d.Direction,
                    d.Currency,
                    d.OriginalAmount,
                    Repaid = d.Payments.Where(p => p.Date <= day).Sum(p => (decimal?)p.Principal) ?? 0
                })
                .ToListAsync();

            foreach (var d in debts)
            {
                var outstanding = Math.Max(0, d.OriginalAmount - d.Repaid);

                if (outstanding == 0)
                    continue;

                var iOwe = d.Direction == DebtDirection.IOwe;

                Add(iOwe ? IOweGroup : OwedToMeGroup, $"{d.Description} ({d.Counterparty})", d.Currency, outstanding, iOwe, $"/Debts/Details/{d.Id}");
            }

            var holdings = await _context.Holdings.AsNoTracking()
                .Where(h => !h.IsClosed)
                .Select(h => new
                {
                    h.Id,
                    h.Name,
                    h.Kind,
                    h.Currency,
                    Latest = h.Valuations
                        .Where(v => v.Date <= day)
                        .OrderByDescending(v => v.Date)
                        .ThenByDescending(v => v.Id)
                        .Select(v => (decimal?)v.Value)
                        .FirstOrDefault()
                })
                .ToListAsync();

            foreach (var h in holdings.Where(h => h.Latest != null))
            {
                var liability = h.Kind == HoldingKind.Liability;

                Add(liability ? LiabilitiesGroup : AssetsGroup, h.Name, h.Currency, h.Latest!.Value, liability, $"/Holdings/Details/{h.Id}");
            }

            return statement;
        }

        /// <summary>Net worth at the end of each of the last few months, oldest first.</summary>
        public async Task<List<(DateOnly Month, decimal NetWorth, bool Complete)>> TrendAsync(int months)
        {
            var rates = await _currency.LoadAsync();
            var result = new List<(DateOnly, decimal, bool)>();
            var today = UserPreferences.Today;

            for (var i = months - 1; i >= 0; i--)
            {
                var month = Periods.MonthStart(today).AddMonths(-i);
                var end = i == 0 ? today : month.AddMonths(1).AddDays(-1);
                var statement = await StatementAsync(end, rates);

                result.Add((month, statement.NetWorth, statement.Unconverted.Count == 0));
            }

            return result;
        }
    }
}
