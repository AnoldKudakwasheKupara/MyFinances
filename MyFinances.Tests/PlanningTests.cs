using Microsoft.EntityFrameworkCore;
using MyFinances.Entities;
using MyFinances.Services;
using MyFinances.ViewModels;

namespace MyFinances.Tests
{
    [Collection(DatabaseCollection.Name)]
    public class PlanningTests(TestDatabase database)
    {
        [Fact]
        public async Task A_budget_counts_spending_in_its_category_and_period_and_warns()
        {
            await using var me = await Person.SignUpAsync(database);
            var bank = await me.AccountAsync("Bank", Currency.USD);
            var groceries = await me.CategoryAsync("Food and Groceries");

            var save = await me.Budgets.SaveAsync(null, new BudgetFormViewModel
            {
                CategoryId = groceries, Amount = 200, Currency = Currency.USD,
                Period = BudgetPeriod.Monthly, StartDate = new DateOnly(2026, 1, 1)
            });
            Assert.True(save.Succeeded, save.Message);

            await me.RecordAsync(TransactionType.Expense, 120, bank, "2026-03-03", "Food and Groceries");
            await me.RecordAsync(TransactionType.Expense, 50, bank, "2026-03-20", "Food and Groceries");
            await me.RecordAsync(TransactionType.Expense, 999, bank, "2026-03-20", "Shopping");
            await me.RecordAsync(TransactionType.Expense, 80, bank, "2026-04-02", "Food and Groceries");

            var march = (await me.Budgets.StatusAsync(new DateOnly(2026, 3, 15))).Single();

            Assert.Equal(170, march.Spent);
            Assert.Equal(30, march.Remaining);
            Assert.Equal(85, march.Percent);
            Assert.Equal(BudgetState.Warning, march.State);

            var april = (await me.Budgets.StatusAsync(new DateOnly(2026, 4, 15))).Single();

            Assert.Equal(80, april.Spent);
            Assert.Equal(BudgetState.OnTrack, april.State);
        }

        [Fact]
        public async Task A_budget_converts_spending_in_another_currency_into_its_own()
        {
            await using var me = await Person.SignUpAsync(database);
            var zar = await me.AccountAsync("Rand", Currency.ZAR);

            await me.RateAsync(Currency.USD, Currency.ZAR, 20, "2026-01-01");

            await me.Budgets.SaveAsync(null, new BudgetFormViewModel
            {
                CategoryId = await me.CategoryAsync("Transport and Fuel"), Amount = 50, Currency = Currency.USD,
                Period = BudgetPeriod.Monthly, StartDate = new DateOnly(2026, 1, 1)
            });

            await me.RecordAsync(TransactionType.Expense, 1200, zar, "2026-02-10", "Transport and Fuel");

            var status = (await me.Budgets.StatusAsync(new DateOnly(2026, 2, 10))).Single();

            Assert.Equal(60, status.Spent);
            Assert.Equal(BudgetState.Over, status.State);
        }

        [Fact]
        public async Task A_goal_contribution_moves_money_as_a_transfer_and_is_never_income()
        {
            await using var me = await Person.SignUpAsync(database);
            var bank = await me.AccountAsync("Bank", Currency.USD, 1000);
            var savings = await me.AccountAsync("Savings", Currency.USD);

            var goal = new GoalFormViewModel { Name = "Emergency Fund", TargetAmount = 500, Currency = Currency.USD, AccountId = savings };
            Assert.True((await me.Goals.SaveAsync(null, goal)).Succeeded);

            var result = await me.Goals.ContributeAsync(goal.Id!.Value, new ContributionFormViewModel
            {
                Amount = 300, Date = new DateOnly(2026, 2, 1), OtherAccountId = bank
            });

            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(300, await me.Goals.SavedAsync(goal.Id.Value));
            Assert.Equal(700, await me.BalanceAsync(bank));
            Assert.Equal(300, await me.BalanceAsync(savings));

            var flows = await me.Analytics.FlowsAsync(Periods.Month(new DateOnly(2026, 2, 1)));
            Assert.Equal(0, flows.Income);

            var tooMuch = await me.Goals.ContributeAsync(goal.Id.Value, new ContributionFormViewModel
            {
                Amount = 301, Withdraw = true, Date = new DateOnly(2026, 2, 2)
            });

            Assert.False(tooMuch.Succeeded);
        }

        [Fact]
        public async Task A_goal_marks_itself_reached_at_its_target()
        {
            await using var me = await Person.SignUpAsync(database);

            var goal = new GoalFormViewModel { Name = "Holiday", TargetAmount = 100, Currency = Currency.USD };
            await me.Goals.SaveAsync(null, goal);
            await me.Goals.ContributeAsync(goal.Id!.Value, new ContributionFormViewModel { Amount = 100, Date = new DateOnly(2026, 2, 1) });

            Assert.Equal(GoalStatus.Achieved, (await me.Db.SavingsGoals.FirstAsync(g => g.Id == goal.Id)).Status);
        }

        [Fact]
        public async Task A_debt_repayment_splits_principal_from_interest()
        {
            await using var me = await Person.SignUpAsync(database);
            var bank = await me.AccountAsync("Bank", Currency.USD, 0, opened: "2026-01-01");

            var debt = new DebtFormViewModel
            {
                Direction = DebtDirection.IOwe, Description = "Car loan", Counterparty = "Bank",
                OriginalAmount = 1000, Currency = Currency.USD, StartDate = new DateOnly(2026, 1, 5),
                DisbursementAccountId = bank
            };
            Assert.True((await me.Debts.CreateAsync(debt)).Succeeded);
            Assert.Equal(1000, await me.BalanceAsync(bank));

            var paid = await me.Debts.PayAsync(debt.Id!.Value, new DebtPaymentFormViewModel
            {
                Principal = 400, Interest = 25, AccountId = bank, Date = new DateOnly(2026, 2, 5)
            });
            Assert.True(paid.Succeeded, paid.Message);

            var standing = await me.Debts.StandingAsync(debt.Id.Value);
            Assert.Equal(600, standing!.Outstanding);
            Assert.Equal(25, standing.InterestPaid);
            Assert.Equal(575, await me.BalanceAsync(bank));

            var january = await me.Analytics.FlowsAsync(Periods.Month(new DateOnly(2026, 1, 1)));
            var february = await me.Analytics.FlowsAsync(Periods.Month(new DateOnly(2026, 2, 1)));

            Assert.Equal(0, january.Income);
            Assert.Equal(25, february.Expenses);
            Assert.Equal(400, february.DebtOut);

            var overpay = await me.Debts.PayAsync(debt.Id.Value, new DebtPaymentFormViewModel { Principal = 601, Date = new DateOnly(2026, 3, 5) });
            Assert.False(overpay.Succeeded);

            await me.Debts.PayAsync(debt.Id.Value, new DebtPaymentFormViewModel { Principal = 600, Date = new DateOnly(2026, 3, 5) });
            Assert.Equal(DebtStatus.Settled, (await me.Debts.StandingAsync(debt.Id.Value))!.Debt.Status);
            Assert.Equal(575, await me.BalanceAsync(bank));
        }

        [Fact]
        public async Task Paying_a_bill_records_the_expense_once_and_moves_to_the_next_due_date()
        {
            await using var me = await Person.SignUpAsync(database);
            var bank = await me.AccountAsync("Bank", Currency.USD, 500);

            var bill = new BillFormViewModel
            {
                Name = "Internet", Amount = 45, Currency = Currency.USD, AccountId = bank,
                CategoryId = await me.CategoryAsync("Internet and Mobile Data"),
                Frequency = Frequency.Monthly, NextDueDate = new DateOnly(2026, 3, 28)
            };
            Assert.True((await me.Bills.SaveAsync(null, bill)).Succeeded);

            var paid = await me.Bills.PayAsync(bill.Id!.Value, new BillPaymentFormViewModel
            {
                Amount = 45, PaidDate = new DateOnly(2026, 3, 27), RecordExpense = true, AccountId = bank
            });

            Assert.True(paid.Succeeded, paid.Message);
            Assert.Equal(455, await me.BalanceAsync(bank));

            var stored = await me.Db.Bills.FirstAsync(b => b.Id == bill.Id);
            Assert.Equal(new DateOnly(2026, 4, 28), stored.NextDueDate);

            // The database itself refuses a second payment of the same occurrence.
            me.Db.BillPayments.Add(new BillPayment { BillId = stored.Id, DueDate = new DateOnly(2026, 3, 28), PaidDate = new DateOnly(2026, 3, 29), Amount = 45 });
            await Assert.ThrowsAsync<DbUpdateException>(() => me.Db.SaveChangesAsync());
            me.Db.ChangeTracker.Clear();

            var undo = await me.Bills.UndoLatestAsync(stored.Id);
            Assert.True(undo.Succeeded);
            Assert.Equal(500, await me.BalanceAsync(bank));
            Assert.Equal(new DateOnly(2026, 3, 28), (await me.Db.Bills.FirstAsync(b => b.Id == bill.Id)).NextDueDate);
        }

        [Fact]
        public async Task A_paid_bill_without_the_expense_leaves_the_ledger_alone()
        {
            await using var me = await Person.SignUpAsync(database);
            var bank = await me.AccountAsync("Bank", Currency.USD, 500);

            var bill = new BillFormViewModel
            {
                Name = "Rent", Amount = 300, Currency = Currency.USD,
                CategoryId = await me.CategoryAsync("Rent and Accommodation"),
                Frequency = Frequency.Quarterly, NextDueDate = new DateOnly(2026, 1, 31)
            };
            await me.Bills.SaveAsync(null, bill);

            await me.Bills.PayAsync(bill.Id!.Value, new BillPaymentFormViewModel { Amount = 300, PaidDate = new DateOnly(2026, 1, 31), RecordExpense = false });

            Assert.Equal(500, await me.BalanceAsync(bank));
            Assert.Equal(new DateOnly(2026, 4, 30), (await me.Db.Bills.FirstAsync(b => b.Id == bill.Id)).NextDueDate);
        }
    }
}
