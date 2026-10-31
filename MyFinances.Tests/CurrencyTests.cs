using Microsoft.EntityFrameworkCore;
using MyFinances.Entities;
using MyFinances.Services;

namespace MyFinances.Tests
{
    [Collection(DatabaseCollection.Name)]
    public class CurrencyTests(TestDatabase database)
    {
        [Fact]
        public void A_rate_book_never_assumes_two_currencies_are_equal()
        {
            var rates = new RateBook([]);

            Assert.Null(rates.Convert(100, Currency.ZWG, Currency.USD, new DateOnly(2026, 1, 1)));
            Assert.Equal(100, rates.Convert(100, Currency.USD, Currency.USD, new DateOnly(2026, 1, 1)));
        }

        [Fact]
        public void A_rate_can_be_read_either_way_round()
        {
            var rates = new RateBook([new ExchangeRate { FromCurrency = Currency.USD, ToCurrency = Currency.ZAR, Rate = 18.5m, EffectiveDate = new DateOnly(2026, 1, 1) }]);

            Assert.Equal(185m, rates.Convert(10, Currency.USD, Currency.ZAR, new DateOnly(2026, 2, 1)));
            Assert.Equal(10m, rates.Convert(185, Currency.ZAR, Currency.USD, new DateOnly(2026, 2, 1)));
        }

        [Fact]
        public async Task Each_transaction_keeps_the_rate_of_its_own_day()
        {
            await using var me = await Person.SignUpAsync(database);
            var zar = await me.AccountAsync("Rand account", Currency.ZAR);

            await me.RateAsync(Currency.USD, Currency.ZAR, 20, "2026-01-01");
            var early = await me.RecordAsync(TransactionType.Expense, 200, zar, "2026-01-15", "Shopping");

            // A later, different rate must not rewrite the January expense.
            await me.RateAsync(Currency.USD, Currency.ZAR, 10, "2026-02-01");
            var late = await me.RecordAsync(TransactionType.Expense, 200, zar, "2026-02-15", "Shopping");

            Assert.Equal(10m, early.ReportingAmount);
            Assert.Equal(new DateOnly(2026, 1, 1), early.ReportingRateDate);
            Assert.Equal(20m, late.ReportingAmount);
            Assert.Equal(200m, early.Amount);
            Assert.Equal(Currency.ZAR, early.Currency);
        }

        [Fact]
        public async Task Without_a_rate_an_amount_is_left_out_of_totals_until_one_is_recorded()
        {
            await using var me = await Person.SignUpAsync(database);
            var zwg = await me.AccountAsync("EcoCash", Currency.ZWG);

            var expense = await me.RecordAsync(TransactionType.Expense, 270, zwg, "2026-03-10", "Food and Groceries");

            Assert.Null(expense.ReportingAmount);

            var march = Periods.Month(new DateOnly(2026, 3, 1));
            var before = await me.Analytics.FlowsAsync(march);

            Assert.Equal(0, before.Expenses);
            Assert.Equal(1, before.Unconverted);

            await me.RateAsync(Currency.USD, Currency.ZWG, 27, "2026-03-01");
            await me.Currency.RefreshSnapshotsAsync(Currency.USD, onlyMissing: true);

            var after = await me.Analytics.FlowsAsync(march);

            Assert.Equal(10, after.Expenses);
            Assert.Equal(0, after.Unconverted);
        }

        [Fact]
        public async Task Changing_the_reporting_currency_converts_again_at_each_days_rate()
        {
            await using var me = await Person.SignUpAsync(database);
            var usd = await me.AccountAsync("Bank", Currency.USD);

            await me.RateAsync(Currency.USD, Currency.GBP, 0.8m, "2026-01-01");
            await me.RateAsync(Currency.USD, Currency.GBP, 0.75m, "2026-06-01");

            var january = await me.RecordAsync(TransactionType.Income, 100, usd, "2026-01-20", "Salary");

            var settings = await me.Db.UserSettings.FirstAsync();
            settings.ReportingCurrency = Currency.GBP;
            await me.Db.SaveChangesAsync();

            await me.Currency.RefreshSnapshotsAsync(Currency.GBP, onlyMissing: false);
            await me.Db.Entry(january).ReloadAsync();

            Assert.Equal(Currency.GBP, january.ReportingCurrency);
            Assert.Equal(80m, january.ReportingAmount);
        }

        [Fact]
        public async Task Balances_in_different_currencies_are_only_added_through_a_rate()
        {
            await using var me = await Person.SignUpAsync(database);
            await me.AccountAsync("Bank", Currency.USD, 100);
            await me.AccountAsync("EcoCash", Currency.ZWG, 2700);

            var rates = await me.Currency.LoadAsync();
            var total = new ConvertedTotal { Currency = Currency.USD };

            foreach (var b in await me.Balances.BalancesAsync())
                total.Add(b.Balance, b.Currency, rates, new DateOnly(2026, 6, 1));

            Assert.Equal(100, total.Amount);
            Assert.Equal(2700, total.Unconverted[Currency.ZWG]);
        }
    }
}
