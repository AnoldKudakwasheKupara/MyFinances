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

        [Fact]
        public async Task Closing_an_account_removes_every_record_and_leaves_others_alone()
        {
            await using var alice = await Person.SignUpAsync(database);
            await using var bob = await Person.SignUpAsync(database);

            var bank = await alice.AccountAsync("Bank", Currency.USD, 1000);
            var savings = await alice.AccountAsync("Savings", Currency.USD);
            await alice.RecordAsync(TransactionType.Expense, 20, bank, "2026-02-01", "Shopping");
            await alice.RecordAsync(TransactionType.Transfer, 50, bank, "2026-02-02", to: savings);
            await alice.RateAsync(Currency.ZAR, Currency.USD, 0.055m, "2026-01-01");

            var goal = new GoalFormViewModel { Name = "Fund", TargetAmount = 500, Currency = Currency.USD, AccountId = savings };
            await alice.Goals.SaveAsync(null, goal);
            await alice.Goals.ContributeAsync(goal.Id!.Value, new ContributionFormViewModel { Amount = 30, Date = new DateOnly(2026, 2, 3), OtherAccountId = bank });

            var debt = new DebtFormViewModel
            {
                Direction = DebtDirection.IOwe, Description = "Loan", Counterparty = "Bank",
                OriginalAmount = 300, Currency = Currency.USD, StartDate = new DateOnly(2026, 1, 5), DisbursementAccountId = bank
            };
            await alice.Debts.CreateAsync(debt);
            await alice.Debts.PayAsync(debt.Id!.Value, new DebtPaymentFormViewModel { Principal = 100, Interest = 5, AccountId = bank, Date = new DateOnly(2026, 2, 5) });

            var bill = new BillFormViewModel
            {
                Name = "Internet", Amount = 45, Currency = Currency.USD, AccountId = bank,
                CategoryId = await alice.CategoryAsync("Internet and Mobile Data"),
                Frequency = Frequency.Monthly, NextDueDate = new DateOnly(2026, 3, 28)
            };
            await alice.Bills.SaveAsync(null, bill);
            await alice.Bills.PayAsync(bill.Id!.Value, new BillPaymentFormViewModel { Amount = 45, PaidDate = new DateOnly(2026, 3, 27), RecordExpense = true, AccountId = bank });

            alice.Db.Holdings.Add(new Holding
            {
                Kind = HoldingKind.Asset, Name = "Car", Category = HoldingCategory.Vehicle, Currency = Currency.USD,
                Valuations = { new HoldingValuation { Date = new DateOnly(2026, 1, 1), Value = 8000 } }
            });
            await alice.Db.SaveChangesAsync();

            var bobsBank = await bob.AccountAsync("Bob's bank", Currency.USD, 10);
            await bob.RecordAsync(TransactionType.Income, 5, bobsBank, "2026-02-01", "Salary");

            // As the close-account page does it, in a fresh request: the
            // ledger, then the sign-in, together.
            await using (var request = database.NewContext(alice.User))
            {
                var setup = new UserSetupService(request, alice.User);
                await request.InTransactionAsync(async () =>
                {
                    await setup.ClearLedgerAsync(alice.Id);
                    request.Users.Remove(await request.Users.SingleAsync(u => u.Id == alice.Id));
                    return await request.SaveChangesAsync();
                });
            }

            alice.Db.ChangeTracker.Clear();
            Assert.Empty(await alice.Db.Accounts.ToListAsync());
            Assert.Empty(await alice.Db.Transactions.ToListAsync());
            Assert.Empty(await alice.Db.Categories.ToListAsync());
            Assert.Empty(await alice.Db.ExchangeRates.ToListAsync());
            Assert.Empty(await alice.Db.SavingsGoals.ToListAsync());
            Assert.Empty(await alice.Db.GoalContributions.ToListAsync());
            Assert.Empty(await alice.Db.Debts.ToListAsync());
            Assert.Empty(await alice.Db.DebtPayments.ToListAsync());
            Assert.Empty(await alice.Db.Bills.ToListAsync());
            Assert.Empty(await alice.Db.BillPayments.ToListAsync());
            Assert.Empty(await alice.Db.Holdings.ToListAsync());
            Assert.Empty(await alice.Db.HoldingValuations.ToListAsync());
            Assert.Empty(await alice.Db.UserSettings.ToListAsync());

            Assert.Equal(15, await bob.BalanceAsync(bobsBank));
        }
    }
}
