using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;

namespace MyFinances.Services
{
    public record RateQuote(decimal Rate, DateOnly EffectiveDate);

    /// <summary>
    /// The user's hand-entered exchange rates, loaded once and read
    /// many times. A rate holds from its date until the next one for
    /// the same pair. A pair can be read either way round: a USD→ZAR
    /// rate also answers ZAR→USD. Nothing else is assumed — with no
    /// recorded rate there is no conversion.
    /// </summary>
    public class RateBook
    {
        private readonly IReadOnlyList<ExchangeRate> _rates;

        public RateBook(IEnumerable<ExchangeRate> rates)
        {
            _rates = rates.ToList();
        }

        public RateQuote? Find(Currency from, Currency to, DateOnly on)
        {
            if (from == to)
                return new RateQuote(1m, on);

            var direct = _rates
                .Where(r => r.FromCurrency == from && r.ToCurrency == to && r.EffectiveDate <= on)
                .MaxBy(r => r.EffectiveDate);

            var inverse = _rates
                .Where(r => r.FromCurrency == to && r.ToCurrency == from && r.EffectiveDate <= on)
                .MaxBy(r => r.EffectiveDate);

            if (direct != null && (inverse == null || direct.EffectiveDate >= inverse.EffectiveDate))
                return new RateQuote(direct.Rate, direct.EffectiveDate);

            if (inverse != null && inverse.Rate != 0)
                return new RateQuote(Math.Round(1m / inverse.Rate, 8), inverse.EffectiveDate);

            return null;
        }

        /// <summary>The converted amount, or null when there is no rate.</summary>
        public decimal? Convert(decimal amount, Currency from, Currency to, DateOnly on)
        {
            var quote = Find(from, to, on);

            return quote == null ? null : Money.Round(amount * quote.Rate);
        }
    }

    /// <summary>
    /// Converts between currencies with the user's own rates, and keeps
    /// each transaction's reporting snapshot: the rate of its day,
    /// stored with it so later rates never rewrite old months.
    /// </summary>
    public class CurrencyService
    {
        private readonly ApplicationDbContext _context;

        public CurrencyService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<RateBook> LoadAsync() =>
            new(await _context.ExchangeRates.AsNoTracking().ToListAsync());

        public static bool Converts(TransactionType type) => type != TransactionType.Transfer;

        /// <summary>
        /// Fills in, or clears, the reporting columns of a transaction.
        /// Transfers have none: they are neither income nor spending.
        /// </summary>
        public static void Snapshot(Transaction transaction, Currency reportingCurrency, RateBook rates)
        {
            if (!Converts(transaction.Type))
            {
                transaction.ReportingCurrency = null;
                transaction.ReportingRate = null;
                transaction.ReportingAmount = null;
                transaction.ReportingRateDate = null;
                return;
            }

            var quote = rates.Find(transaction.Currency, reportingCurrency, transaction.Date);

            transaction.ReportingCurrency = reportingCurrency;
            transaction.ReportingRate = quote?.Rate;
            transaction.ReportingRateDate = quote?.EffectiveDate;
            transaction.ReportingAmount = quote == null
                ? null
                : Money.Round(transaction.Amount * quote.Rate);
        }

        /// <summary>
        /// Takes the snapshot again for rows that need it: those still
        /// without a rate, or every row when the reporting currency has
        /// changed. Each uses the rate of its own date.
        /// </summary>
        public async Task<int> RefreshSnapshotsAsync(Currency reportingCurrency, bool onlyMissing)
        {
            var rates = await LoadAsync();

            var query = _context.Transactions.Where(t => t.Type != TransactionType.Transfer);

            if (onlyMissing)
            {
                query = query.Where(t =>
                    t.ReportingAmount == null || t.ReportingCurrency != reportingCurrency);
            }

            var rows = await query.ToListAsync();

            var changed = 0;

            foreach (var row in rows)
            {
                var before = (row.ReportingCurrency, row.ReportingAmount);

                Snapshot(row, reportingCurrency, rates);

                if (before != (row.ReportingCurrency, row.ReportingAmount))
                    changed++;
            }

            await _context.SaveChangesAsync();

            return changed;
        }

        public async Task<int> UnconvertedCountAsync(Currency reportingCurrency) =>
            await _context.Transactions.CountAsync(t =>
                t.Type != TransactionType.Transfer &&
                (t.ReportingAmount == null || t.ReportingCurrency != reportingCurrency));
    }
}
