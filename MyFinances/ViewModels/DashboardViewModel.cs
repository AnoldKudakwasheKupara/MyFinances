using MyFinances.Entities;
using MyFinances.Services;
using MyFinances.Services.Reporting;

namespace MyFinances.ViewModels
{
    public class DashboardViewModel
    {
        public DashboardPeriod Period { get; init; }

        public DateRange Range { get; init; } = default!;

        public SeriesGrain Grain { get; init; }

        public Currency Currency { get; init; }

        public bool HasAccounts { get; init; }

        public bool HasTransactions { get; init; }

        public ConvertedTotal Available { get; set; } = new();

        public decimal? AvailableBefore { get; set; }

        public FlowSet Flows { get; set; } = new();

        public FlowSet? FlowsBefore { get; set; }

        public NetWorthStatement NetWorth { get; set; } = new();

        public decimal? NetWorthBefore { get; set; }

        public List<CategoryTotal> Spending { get; set; } = new();

        public List<SeriesPoint> Series { get; set; } = new();

        public List<AccountBalance> Accounts { get; set; } = new();

        public List<Transaction> Recent { get; set; } = new();

        public List<BudgetStatus> Budgets { get; set; } = new();

        public List<GoalProgress> Goals { get; set; } = new();

        public List<BillStatus> Bills { get; set; } = new();

        public Flow? LargestExpense { get; set; }

        public decimal DailySpending { get; set; }

        public int TransactionCount { get; set; }

        /// <summary>Share of income kept, when there was income.</summary>
        public decimal? SavingsRate =>
            Flows.Income > 0 ? Math.Round(Flows.Net / Flows.Income * 100, 1) : null;

        /// <summary>
        /// Percentage change from an earlier figure, or null when there
        /// is nothing sensible to compare with.
        /// </summary>
        public static decimal? Change(decimal now, decimal? before) =>
            before is null or 0 ? null : Math.Round((now - before.Value) / Math.Abs(before.Value) * 100, 1);
    }
}
