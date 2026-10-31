using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;

namespace MyFinances.Services
{
    public record AccountBalance(Account Account, decimal Balance)
    {
        public Currency Currency => Account.Currency;
    }

    /// <summary>
    /// Works out what each account holds. Nothing is stored: a balance
    /// is the opening balance plus every ledger row since, so it is
    /// right however transactions are added, changed or removed.
    ///
    /// In:  income, money received on a debt, transfers arriving (their
    ///      received amount).
    /// Out: expenses, money paid on a debt, transfers leaving.
    /// </summary>
    public class BalanceService
    {
        private readonly ApplicationDbContext _context;

        public BalanceService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Dictionary<int, decimal>> MovementsAsync(DateOnly? asOf = null)
        {
            var ledger = _context.Transactions.AsQueryable();

            if (asOf != null)
                ledger = ledger.Where(t => t.Date <= asOf.Value);

            var leaving = await ledger
                .GroupBy(t => t.AccountId)
                .Select(g => new
                {
                    AccountId = g.Key,
                    In = g.Where(t => t.Type == TransactionType.Income || t.Type == TransactionType.DebtIn)
                        .Sum(t => (decimal?)t.Amount) ?? 0,
                    Out = g.Where(t => t.Type == TransactionType.Expense ||
                                       t.Type == TransactionType.DebtOut ||
                                       t.Type == TransactionType.Transfer)
                        .Sum(t => (decimal?)t.Amount) ?? 0
                })
                .ToListAsync();

            var arriving = await ledger
                .Where(t => t.Type == TransactionType.Transfer && t.ToAccountId != null)
                .GroupBy(t => t.ToAccountId!.Value)
                .Select(g => new { AccountId = g.Key, In = g.Sum(t => t.ToAmount ?? t.Amount) })
                .ToListAsync();

            var movement = new Dictionary<int, decimal>();

            foreach (var row in leaving)
                movement[row.AccountId] = row.In - row.Out;

            foreach (var row in arriving)
                movement[row.AccountId] = movement.GetValueOrDefault(row.AccountId) + row.In;

            return movement;
        }

        public async Task<List<AccountBalance>> BalancesAsync(bool includeArchived = true, DateOnly? asOf = null)
        {
            var accounts = await _context.Accounts
                .AsNoTracking()
                .Where(a => includeArchived || !a.IsArchived)
                .OrderBy(a => a.IsArchived)
                .ThenBy(a => a.Name)
                .ToListAsync();

            if (asOf != null)
                accounts = accounts.Where(a => a.OpeningDate <= asOf.Value).ToList();

            var movement = await MovementsAsync(asOf);

            return accounts
                .Select(a => new AccountBalance(a, a.OpeningBalance + movement.GetValueOrDefault(a.Id)))
                .ToList();
        }

        public async Task<decimal?> BalanceAsync(int accountId)
        {
            var account = await _context.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId);

            if (account == null)
                return null;

            var movement = await MovementsAsync();

            return account.OpeningBalance + movement.GetValueOrDefault(accountId);
        }
    }

    /// <summary>
    /// A total across currencies in the reporting currency, with what
    /// had to be left out for want of a rate, so nothing is silently
    /// added at one-to-one.
    /// </summary>
    public class ConvertedTotal
    {
        public Currency Currency { get; init; }

        public decimal Amount { get; set; }

        public Dictionary<Currency, decimal> Unconverted { get; } = new();

        public bool IsComplete => Unconverted.Count == 0;

        public void Add(decimal amount, Currency from, RateBook rates, DateOnly on)
        {
            var converted = rates.Convert(amount, from, Currency, on);

            if (converted == null)
                Unconverted[from] = Unconverted.GetValueOrDefault(from) + amount;
            else
                Amount += converted.Value;
        }
    }
}
