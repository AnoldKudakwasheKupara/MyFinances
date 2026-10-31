using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;
using MyFinances.ViewModels;

namespace MyFinances.Services
{
    public class AccountService
    {
        private readonly ApplicationDbContext _context;
        private readonly BalanceService _balances;
        private readonly CurrencyService _currency;
        private readonly UserPreferences _preferences;

        public AccountService(
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

        public async Task<AccountsIndexViewModel> IndexAsync(bool showArchived)
        {
            var settings = await _preferences.GetAsync();
            var rates = await _currency.LoadAsync();
            var balances = await _balances.BalancesAsync();

            var active = balances.Where(b => !b.Account.IsArchived).ToList();

            var total = new ConvertedTotal { Currency = settings.ReportingCurrency };

            foreach (var item in active)
                total.Add(item.Balance, item.Currency, rates, UserPreferences.Today);

            return new AccountsIndexViewModel
            {
                Active = active,
                Archived = balances.Where(b => b.Account.IsArchived).ToList(),
                ByCurrency = active
                    .GroupBy(b => b.Currency)
                    .OrderBy(g => g.Key)
                    .ToDictionary(g => g.Key, g => g.Sum(b => b.Balance)),
                Total = total,
                ShowArchived = showArchived
            };
        }

        public async Task<AccountDetailsViewModel?> DetailsAsync(int id)
        {
            var account = await _context.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);

            if (account == null)
                return null;

            var today = UserPreferences.Today;
            var monthStart = new DateOnly(today.Year, today.Month, 1);

            var touching = _context.Transactions
                .Where(t => t.AccountId == id || t.ToAccountId == id);

            var month = await touching
                .Where(t => t.Date >= monthStart && t.Date <= today)
                .Select(t => new { t.Type, t.AccountId, t.Amount, t.ToAmount })
                .ToListAsync();

            decimal moneyIn = 0, moneyOut = 0;

            foreach (var t in month)
            {
                if (t.Type == TransactionType.Transfer && t.AccountId != id)
                    moneyIn += t.ToAmount ?? t.Amount;
                else if (t.Type is TransactionType.Income or TransactionType.DebtIn)
                    moneyIn += t.Amount;
                else
                    moneyOut += t.Amount;
            }

            return new AccountDetailsViewModel
            {
                Account = account,
                Balance = await _balances.BalanceAsync(id) ?? account.OpeningBalance,
                InThisMonth = moneyIn,
                OutThisMonth = moneyOut,
                Recent = await touching
                    .AsNoTracking()
                    .Include(t => t.Category)
                    .Include(t => t.Account)
                    .Include(t => t.ToAccount)
                    .OrderByDescending(t => t.Date)
                    .ThenByDescending(t => t.Id)
                    .Take(15)
                    .ToListAsync(),
                TransactionCount = await touching.CountAsync(),
                CanDelete = !await InUseAsync(id)
            };
        }

        public async Task<AccountFormViewModel?> FormAsync(int id)
        {
            var account = await _context.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);

            if (account == null)
                return null;

            return new AccountFormViewModel
            {
                Id = account.Id,
                Name = account.Name,
                Type = account.Type,
                Currency = account.Currency,
                OpeningBalance = account.OpeningBalance,
                OpeningDate = account.OpeningDate,
                Description = account.Description,
                CurrencyLocked = await HasLedgerAsync(id)
            };
        }

        public async Task<int> CreateAsync(AccountFormViewModel form)
        {
            var account = new Account
            {
                Name = form.Name.Trim(),
                Type = form.Type,
                Currency = form.Currency,
                OpeningBalance = Money.Round(form.OpeningBalance),
                OpeningDate = form.OpeningDate,
                Description = Clean(form.Description)
            };

            _context.Accounts.Add(account);
            await _context.SaveChangesAsync();

            return account.Id;
        }

        public async Task<ServiceResult> UpdateAsync(int id, AccountFormViewModel form)
        {
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == id);

            if (account == null)
                return ServiceResult.Missing();

            if (form.Currency != account.Currency && await HasLedgerAsync(id))
            {
                return ServiceResult.Refused(
                    "The currency cannot change once money has moved in or out of this account. " +
                    "Open a new account in the other currency and transfer between them.");
            }

            var earliest = await _context.Transactions
                .Where(t => t.AccountId == id || t.ToAccountId == id)
                .MinAsync(t => (DateOnly?)t.Date);

            if (earliest != null && form.OpeningDate > earliest.Value)
            {
                return ServiceResult.Refused(
                    $"The opening date cannot be after the first transaction on {earliest.Value:d MMM yyyy}.");
            }

            account.Name = form.Name.Trim();
            account.Type = form.Type;
            account.Currency = form.Currency;
            account.OpeningBalance = Money.Round(form.OpeningBalance);
            account.OpeningDate = form.OpeningDate;
            account.Description = Clean(form.Description);

            await _context.SaveChangesAsync();

            return ServiceResult.Ok("Account updated.");
        }

        public async Task<ServiceResult> SetArchivedAsync(int id, bool archived)
        {
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == id);

            if (account == null)
                return ServiceResult.Missing();

            account.IsArchived = archived;
            await _context.SaveChangesAsync();

            return ServiceResult.Ok(archived
                ? $"{account.Name} is archived. Its history stays in your reports."
                : $"{account.Name} is active again.");
        }

        /// <summary>
        /// Only an account nothing refers to can go; anything with
        /// history is archived instead, so no report loses its past.
        /// </summary>
        public async Task<ServiceResult> DeleteAsync(int id)
        {
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == id);

            if (account == null)
                return ServiceResult.Missing();

            if (await InUseAsync(id))
            {
                return ServiceResult.Refused(
                    "This account has history, so it can only be archived. Archiving hides it " +
                    "from everyday lists but keeps every transaction in your reports.");
            }

            var settings = await _context.UserSettings.FirstOrDefaultAsync();

            if (settings?.DefaultAccountId == id)
                settings.DefaultAccountId = null;

            _context.Accounts.Remove(account);
            await _context.SaveChangesAsync();

            return ServiceResult.Ok($"{account.Name} was deleted.");
        }

        public Task<bool> HasLedgerAsync(int id) =>
            _context.Transactions.AnyAsync(t => t.AccountId == id || t.ToAccountId == id);

        public async Task<bool> InUseAsync(int id) =>
            await HasLedgerAsync(id) ||
            await _context.SavingsGoals.AnyAsync(g => g.AccountId == id) ||
            await _context.Bills.AnyAsync(b => b.AccountId == id) ||
            await _context.DebtPayments.AnyAsync(p => p.AccountId == id);

        private static string? Clean(string? text) =>
            string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
