using Microsoft.EntityFrameworkCore;
using MyFinances.Entities;
using MyFinances.Services;
using MyFinances.Services.Reporting;

namespace MyFinances.Tests
{
    [Collection(DatabaseCollection.Name)]
    public class LedgerTests(TestDatabase database)
    {
        [Fact]
        public async Task A_new_account_holds_its_opening_balance()
        {
            await using var me = await Person.SignUpAsync(database);

            var bank = await me.AccountAsync("Bank", Currency.USD, 250.75m);

            Assert.Equal(250.75m, await me.BalanceAsync(bank));
        }

        [Fact]
        public async Task Income_adds_to_the_balance_and_expenses_take_from_it()
        {
            await using var me = await Person.SignUpAsync(database);
            var bank = await me.AccountAsync("Bank", Currency.USD, 100);

            await me.RecordAsync(TransactionType.Income, 1000, bank, "2026-03-01", "Salary");
            await me.RecordAsync(TransactionType.Expense, 120.40m, bank, "2026-03-02", "Food and Groceries");

            Assert.Equal(979.60m, await me.BalanceAsync(bank));
        }

        [Fact]
        public async Task A_transfer_moves_money_without_being_income_or_spending()
        {
            await using var me = await Person.SignUpAsync(database);
            var bank = await me.AccountAsync("Bank", Currency.USD, 500);
            var cash = await me.AccountAsync("Cash", Currency.USD, 0);

            await me.RecordAsync(TransactionType.Transfer, 200, bank, "2026-03-05", to: cash);

            Assert.Equal(300, await me.BalanceAsync(bank));
            Assert.Equal(200, await me.BalanceAsync(cash));

            var flows = await me.Analytics.FlowsAsync(new DateRange(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)));

            Assert.Equal(0, flows.Income);
            Assert.Equal(0, flows.Expenses);
        }

        [Fact]
        public async Task A_transfer_between_currencies_needs_the_amount_received_and_keeps_both_sides()
        {
            await using var me = await Person.SignUpAsync(database);
            var usd = await me.AccountAsync("Bank", Currency.USD, 500);
            var zwg = await me.AccountAsync("EcoCash", Currency.ZWG, 0);

            var refused = await me.Ledger.CreateAsync(new TransactionInput
            {
                Type = TransactionType.Transfer, Amount = 100, AccountId = usd, ToAccountId = zwg,
                Date = new DateOnly(2026, 3, 5), Description = "Top up"
            });

            Assert.False(refused.Succeeded);

            var transfer = await me.RecordAsync(TransactionType.Transfer, 100, usd, "2026-03-05", to: zwg, received: 2650);

            Assert.Equal(26.5m, transfer.TransferRate);
            Assert.Equal(400, await me.BalanceAsync(usd));
            Assert.Equal(2650, await me.BalanceAsync(zwg));
        }

        [Fact]
        public async Task A_transfer_to_the_same_account_is_refused()
        {
            await using var me = await Person.SignUpAsync(database);
            var bank = await me.AccountAsync("Bank", Currency.USD, 500);

            var result = await me.Ledger.CreateAsync(new TransactionInput
            {
                Type = TransactionType.Transfer, Amount = 10, AccountId = bank, ToAccountId = bank,
                Date = new DateOnly(2026, 3, 5), Description = "Loop"
            });

            Assert.False(result.Succeeded);
        }

        [Fact]
        public async Task Editing_and_deleting_a_transaction_keeps_the_balance_right()
        {
            await using var me = await Person.SignUpAsync(database);
            var bank = await me.AccountAsync("Bank", Currency.USD, 100);
            var other = await me.AccountAsync("Other", Currency.USD, 0);

            var expense = await me.RecordAsync(TransactionType.Expense, 30, bank, "2026-04-01", "Transport and Fuel");

            var edit = await me.Ledger.UpdateAsync(expense.Id, new TransactionInput
            {
                Type = TransactionType.Expense, Amount = 45, AccountId = other,
                CategoryId = await me.CategoryAsync("Transport and Fuel"),
                Date = new DateOnly(2026, 4, 1), Description = "Fuel"
            });

            Assert.True(edit.Succeeded, edit.Message);
            Assert.Equal(100, await me.BalanceAsync(bank));
            Assert.Equal(-45, await me.BalanceAsync(other));

            var delete = await me.Ledger.DeleteAsync(expense.Id);

            Assert.True(delete.Succeeded);
            Assert.Equal(0, await me.BalanceAsync(other));
        }

        [Fact]
        public async Task An_expense_cannot_use_an_income_category()
        {
            await using var me = await Person.SignUpAsync(database);
            var bank = await me.AccountAsync("Bank", Currency.USD, 100);

            var result = await me.Ledger.CreateAsync(new TransactionInput
            {
                Type = TransactionType.Expense, Amount = 5, AccountId = bank,
                CategoryId = await me.CategoryAsync("Salary"),
                Date = new DateOnly(2026, 4, 1), Description = "Wrong"
            });

            Assert.False(result.Succeeded);
        }

        [Fact]
        public async Task A_transaction_before_the_account_opened_is_refused()
        {
            await using var me = await Person.SignUpAsync(database);
            var bank = await me.AccountAsync("Bank", Currency.USD, 100, opened: "2026-05-01");

            var result = await me.Ledger.CreateAsync(new TransactionInput
            {
                Type = TransactionType.Income, Amount = 5, AccountId = bank,
                CategoryId = await me.CategoryAsync("Salary"),
                Date = new DateOnly(2026, 4, 30), Description = "Too early"
            });

            Assert.False(result.Succeeded);
        }

        [Fact]
        public async Task Monthly_income_and_expenses_count_only_that_month()
        {
            await using var me = await Person.SignUpAsync(database);
            var bank = await me.AccountAsync("Bank", Currency.USD);

            await me.RecordAsync(TransactionType.Income, 1000, bank, "2026-05-31", "Salary");
            await me.RecordAsync(TransactionType.Income, 1200, bank, "2026-06-01", "Salary");
            await me.RecordAsync(TransactionType.Expense, 300, bank, "2026-06-15", "Rent and Accommodation");
            await me.RecordAsync(TransactionType.Expense, 50, bank, "2026-07-01", "Utilities");

            var june = await me.Analytics.FlowsAsync(Periods.Month(new DateOnly(2026, 6, 10)));

            Assert.Equal(1200, june.Income);
            Assert.Equal(300, june.Expenses);
            Assert.Equal(900, june.Net);
        }
    }
}
