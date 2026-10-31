using Microsoft.EntityFrameworkCore;
using MyFinances.Entities;
using MyFinances.Services;
using MyFinances.ViewModels;

namespace MyFinances.Tests
{
    [Collection(DatabaseCollection.Name)]
    public class NetWorthAndIsolationTests(TestDatabase database)
    {
        [Fact]
        public async Task Net_worth_is_what_you_have_less_what_you_owe_counted_once()
        {
            await using var me = await Person.SignUpAsync(database);
            var bank = await me.AccountAsync("Bank", Currency.USD, 2000, opened: "2026-01-01");

            // Borrowing puts money in the bank and a debt on the other side.
            var loan = new DebtFormViewModel
            {
                Direction = DebtDirection.IOwe, Description = "Loan", Counterparty = "Brother",
                OriginalAmount = 500, Currency = Currency.USD, StartDate = new DateOnly(2026, 2, 1),
                DisbursementAccountId = bank
            };
            await me.Debts.CreateAsync(loan);

            me.Db.Holdings.Add(new Holding
            {
                Kind = HoldingKind.Asset, Name = "Car", Category = HoldingCategory.Vehicle, Currency = Currency.USD,
                Valuations = { new HoldingValuation { Date = new DateOnly(2026, 1, 1), Value = 8000 }, new HoldingValuation { Date = new DateOnly(2026, 3, 1), Value = 7500 } }
            });
            me.Db.Holdings.Add(new Holding
            {
                Kind = HoldingKind.Liability, Name = "Store account", Category = HoldingCategory.OtherObligation, Currency = Currency.USD,
                Valuations = { new HoldingValuation { Date = new DateOnly(2026, 1, 1), Value = 300 } }
            });
            me.Db.Holdings.Add(new Holding
            {
                Kind = HoldingKind.Asset, Name = "Old phone", Category = HoldingCategory.Electronics, Currency = Currency.USD, IsClosed = true,
                Valuations = { new HoldingValuation { Date = new DateOnly(2026, 1, 1), Value = 100 } }
            });
            await me.Db.SaveChangesAsync();

            var statement = await me.NetWorth.StatementAsync(new DateOnly(2026, 3, 15));

            // 2,500 in the bank + 7,500 car − 500 loan − 300 store account.
            Assert.Equal(10000, statement.Assets);
            Assert.Equal(800, statement.Liabilities);
            Assert.Equal(9200, statement.NetWorth);

            // Before the loan and the revaluation.
            var january = await me.NetWorth.StatementAsync(new DateOnly(2026, 1, 15));
            Assert.Equal(2000 + 8000 - 300, january.NetWorth);
        }

        [Fact]
        public async Task People_only_ever_see_their_own_records()
        {
            await using var alice = await Person.SignUpAsync(database);
            await using var bob = await Person.SignUpAsync(database);

            var alicesBank = await alice.AccountAsync("Alice's bank", Currency.USD, 999);
            await alice.RecordAsync(TransactionType.Income, 10, alicesBank, "2026-02-01", "Salary");

            Assert.Empty(await bob.Db.Accounts.ToListAsync());
            Assert.Empty(await bob.Db.Transactions.ToListAsync());
            Assert.Null(await bob.Balances.BalanceAsync(alicesBank));
            Assert.All(await bob.Db.Categories.ToListAsync(), c => Assert.Equal(bob.Id, c.UserId));
        }

        [Fact]
        public async Task Nobody_can_record_against_someone_elses_account()
        {
            await using var alice = await Person.SignUpAsync(database);
            await using var bob = await Person.SignUpAsync(database);

            var alicesBank = await alice.AccountAsync("Alice's bank", Currency.USD, 999);

            var result = await bob.Ledger.CreateAsync(new TransactionInput
            {
                Type = TransactionType.Expense, Amount = 999, AccountId = alicesBank,
                CategoryId = await bob.CategoryAsync("Shopping"),
                Date = new DateOnly(2026, 2, 1), Description = "Not mine"
            });

            Assert.False(result.Succeeded);
            Assert.Equal(999, await alice.BalanceAsync(alicesBank));
        }

        [Fact]
        public async Task The_database_refuses_to_save_a_row_for_another_user()
        {
            await using var alice = await Person.SignUpAsync(database);
            await using var bob = await Person.SignUpAsync(database);

            bob.Db.Accounts.Add(new Account { UserId = alice.Id, Name = "Sneaky", Currency = Currency.USD, OpeningDate = new DateOnly(2026, 1, 1) });

            await Assert.ThrowsAsync<InvalidOperationException>(() => bob.Db.SaveChangesAsync());
        }
    }
}
