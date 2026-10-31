using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;
using MyFinances.Services.Reporting;
using MyFinances.ViewModels;

namespace MyFinances.Services
{
    /// <summary>
    /// Everything on the dashboard, from the user's own records. Each
    /// figure is compared with the period before only when there is
    /// history from before this period to compare with.
    /// </summary>
    public class DashboardService
    {
        private readonly ApplicationDbContext _context;
        private readonly Analytics _analytics;
        private readonly BalanceService _balances;
        private readonly CurrencyService _currency;
        private readonly NetWorthService _netWorth;
        private readonly BudgetService _budgets;
        private readonly GoalService _goals;
        private readonly BillService _bills;
        private readonly UserPreferences _preferences;

        public DashboardService(
            ApplicationDbContext context,
            Analytics analytics,
            BalanceService balances,
            CurrencyService currency,
            NetWorthService netWorth,
            BudgetService budgets,
            GoalService goals,
            BillService bills,
            UserPreferences preferences)
        {
            _context = context;
            _analytics = analytics;
            _balances = balances;
            _currency = currency;
            _netWorth = netWorth;
            _budgets = budgets;
            _goals = goals;
            _bills = bills;
            _preferences = preferences;
        }

        public static DateRange RangeOf(DashboardPeriod period, DateOnly today, int financialYearStart) => period switch
        {
            DashboardPeriod.LastMonth => Periods.Month(today.AddMonths(-1)),
            DashboardPeriod.ThisQuarter => Periods.Quarter(today),
            DashboardPeriod.ThisYear => Periods.Year(today),
            DashboardPeriod.FinancialYear => Periods.FinancialYear(today, financialYearStart),
            _ => Periods.Month(today)
        };

        public async Task<DashboardViewModel> BuildAsync(DashboardPeriod? requested, SeriesGrain grain)
        {
            var settings = await _preferences.GetAsync();
            var today = UserPreferences.Today;
            var period = requested ?? settings.DefaultDashboardPeriod;
            var range = RangeOf(period, today, settings.FinancialYearStartMonth);
            var previous = Periods.Previous(range);
            var currency = settings.ReportingCurrency;

            var model = new DashboardViewModel
            {
                Period = period,
                Range = range,
                Grain = grain,
                Currency = currency,
                HasAccounts = await _context.Accounts.AnyAsync(),
                HasTransactions = await _context.Transactions.AnyAsync()
            };

            if (!model.HasAccounts)
                return model;

            var rates = await _currency.LoadAsync();

            // Balances: today, and at the end of the previous period.
            var balances = await _balances.BalancesAsync(includeArchived: false);
            model.Accounts = balances;

            var available = new ConvertedTotal { Currency = currency };
            foreach (var b in balances)
                available.Add(b.Balance, b.Currency, rates, today);
            model.Available = available;

            var hasEarlierHistory =
                await _context.Accounts.AnyAsync(a => a.OpeningDate < range.Start) ||
                await _context.Transactions.AnyAsync(t => t.Date < range.Start);

            if (hasEarlierHistory)
            {
                var before = new ConvertedTotal { Currency = currency };
                foreach (var b in await _balances.BalancesAsync(includeArchived: false, asOf: previous.End))
                    before.Add(b.Balance, b.Currency, rates, previous.End);
                model.AvailableBefore = before.Amount;
            }

            // Income and spending in the period and the one before.
            var flows = await _analytics.FlowsAsync(range);
            model.Flows = flows;

            if (hasEarlierHistory)
                model.FlowsBefore = await _analytics.FlowsAsync(previous);

            model.Spending = Analytics.ByCategory(flows, TransactionType.Expense);

            var netWorth = await _netWorth.StatementAsync(today, rates);
            model.NetWorth = netWorth;

            if (hasEarlierHistory)
                model.NetWorthBefore = (await _netWorth.StatementAsync(previous.End, rates)).NetWorth;

            var (count, buckets) = grain switch
            {
                SeriesGrain.Quarterly => (4, SeriesGrain.Quarterly),
                SeriesGrain.Yearly => (3, SeriesGrain.Yearly),
                _ => (6, SeriesGrain.Monthly)
            };

            model.Series = (await _analytics.SeriesAsync(Analytics.Buckets(buckets, today, count), buckets)).Points;

            model.Recent = await _context.Transactions.AsNoTracking()
                .Include(t => t.Account)
                .Include(t => t.ToAccount)
                .Include(t => t.Category)
                .OrderByDescending(t => t.Date)
                .ThenByDescending(t => t.Id)
                .Take(8)
                .ToListAsync();

            model.Budgets = await _budgets.StatusAsync(today);
            model.Goals = (await _goals.ListAsync(activeOnly: true)).Take(4).ToList();
            model.Bills = (await _bills.ListAsync())
                .Where(b => b.Bill.IsActive && b.Bill.NextDueDate <= today.AddDays(30))
                .Take(5)
                .ToList();

            var expenses = flows.Of(TransactionType.Expense).ToList();
            var elapsed = Math.Max(1, (today < range.End ? today : range.End).DayNumber - range.Start.DayNumber + 1);

            model.LargestExpense = expenses.MaxBy(e => e.Amount);
            model.DailySpending = Money.Round(flows.Expenses / elapsed);
            model.TransactionCount = await _context.Transactions.CountAsync(t => t.Date >= range.Start && t.Date <= range.End);

            return model;
        }
    }
}
