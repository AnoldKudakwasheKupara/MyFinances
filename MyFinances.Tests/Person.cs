using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;
using MyFinances.Services;
using MyFinances.Services.Reporting;
using MyFinances.ViewModels;

namespace MyFinances.Tests
{
    /// <summary>
    /// One signed-up person with the services the app would give them,
    /// built by hand instead of through a web request. Each test makes
    /// its own, so tests never see each other's records.
    /// </summary>
    public sealed class Person : IAsyncDisposable
    {
        public int Id { get; private init; }

        public CurrentUser User { get; private init; } = default!;

        public ApplicationDbContext Db { get; private init; } = default!;

        public UserPreferences Preferences { get; private init; } = default!;

        public CurrencyService Currency { get; private init; } = default!;

        public BalanceService Balances { get; private init; } = default!;

        public LedgerService Ledger { get; private init; } = default!;

        public BudgetService Budgets { get; private init; } = default!;

        public GoalService Goals { get; private init; } = default!;

        public DebtService Debts { get; private init; } = default!;

        public BillService Bills { get; private init; } = default!;

        public NetWorthService NetWorth { get; private init; } = default!;

        public Analytics Analytics { get; private init; } = default!;

        public static async Task<Person> SignUpAsync(TestDatabase database, Entities.Currency reporting = Entities.Currency.USD)
        {
            int id;

            var setupUser = new CurrentUser(null);

            await using (var setup = database.NewContext(setupUser))
            {
                var user = new ApplicationUser
                {
                    UserName = $"{Guid.NewGuid():N}@test.local",
                    Email = $"{Guid.NewGuid():N}@test.local",
                    FullName = "Test Person",
                    SecurityStamp = Guid.NewGuid().ToString()
                };

                setup.Users.Add(user);
                await setup.SaveChangesAsync();
                id = user.Id;

                await new UserSetupService(setup, setupUser).EnsureSetUpAsync(id, reporting);
            }

            var current = new CurrentUser(null);
            current.ActAs(id);

            var db = database.NewContext(current);
            var preferences = new UserPreferences(db, current);
            var currency = new CurrencyService(db);
            var balances = new BalanceService(db);
            var receipts = new ReceiptStorage(Path.Combine(Path.GetTempPath(), "myfinances-test-receipts"));
            var ledger = new LedgerService(db, currency, preferences, receipts, current);
            var budgets = new BudgetService(db, currency, preferences);

            return new Person
            {
                Id = id,
                User = current,
                Db = db,
                Preferences = preferences,
                Currency = currency,
                Balances = balances,
                Ledger = ledger,
                Budgets = budgets,
                Goals = new GoalService(db, ledger),
                Debts = new DebtService(db, ledger),
                Bills = new BillService(db, ledger),
                NetWorth = new NetWorthService(db, balances, currency, preferences),
                Analytics = new Analytics(db, preferences)
            };
        }

        public async Task<int> AccountAsync(string name, Currency currency, decimal opening = 0, string opened = "2026-01-01")
        {
            var account = new Account
            {
                Name = name,
                Type = AccountType.Bank,
                Currency = currency,
                OpeningBalance = opening,
                OpeningDate = DateOnly.Parse(opened)
            };

            Db.Accounts.Add(account);
            await Db.SaveChangesAsync();

            return account.Id;
        }

        public async Task<int> CategoryAsync(string name) =>
            (await Db.Categories.FirstAsync(c => c.Name == name)).Id;

        public async Task RateAsync(Currency from, Currency to, decimal rate, string from_date)
        {
            Db.ExchangeRates.Add(new ExchangeRate
            {
                FromCurrency = from,
                ToCurrency = to,
                Rate = rate,
                EffectiveDate = DateOnly.Parse(from_date)
            });

            await Db.SaveChangesAsync();
        }

        public async Task<Transaction> RecordAsync(TransactionType type, decimal amount, int account, string date,
            string? category = null, int? to = null, decimal? received = null)
        {
            var result = await Ledger.CreateAsync(new TransactionInput
            {
                Type = type,
                Amount = amount,
                AccountId = account,
                Date = DateOnly.Parse(date),
                Description = $"{type} {amount}",
                CategoryId = category == null ? null : await CategoryAsync(category),
                ToAccountId = to,
                ToAmount = received
            });

            Assert.True(result.Succeeded, result.Error);

            return result.Transaction!;
        }

        public async Task<decimal> BalanceAsync(int account) =>
            (await Balances.BalanceAsync(account))!.Value;

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
