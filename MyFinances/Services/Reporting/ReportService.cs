using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;

namespace MyFinances.Services.Reporting
{
    /// <summary>
    /// The ten reports. Each is built as a <see cref="Report"/> table
    /// that the page, the CSV and the PDF all print from. Money in the
    /// reporting currency uses each transaction's stored conversion;
    /// budgets, goals and debts stay in their own currency.
    /// </summary>
    public class ReportService
    {
        public static readonly IReadOnlyList<ReportDefinition> All =
        [
            new("income", "Income", "arrow-down-left", "Money coming in, by category."),
            new("expenses", "Expenses", "arrow-up-right", "Money going out, by category."),
            new("income-vs-expenses", "Income vs expenses", "bar-chart", "Month by month, with what was left over."),
            new("spending-by-category", "Spending by category", "pie-chart", "Where your money went, compared with the period before."),
            new("account-balances", "Account balances", "bank", "What each account held on a day, in its own currency and yours.", UsesRange: false),
            new("cash-flow", "Monthly cash flow", "water", "Everything that moved in and out of your accounts, including debt principal."),
            new("budget-performance", "Budget performance", "speedometer", "Each budget against what was actually spent, period by period."),
            new("savings-goals", "Savings goals", "piggy-bank", "Progress on every goal and what was put aside in the period."),
            new("debt-summary", "Debt summary", "cash-coin", "What is owed each way, repaid, and paid in interest."),
            new("net-worth", "Net worth", "graph-up-arrow", "Everything you own less everything you owe, and how it has moved.", UsesRange: false)
        ];

        private readonly ApplicationDbContext _context;
        private readonly Analytics _analytics;
        private readonly BalanceService _balances;
        private readonly CurrencyService _currency;
        private readonly BudgetService _budgets;
        private readonly GoalService _goals;
        private readonly DebtService _debts;
        private readonly NetWorthService _netWorth;
        private readonly UserPreferences _preferences;

        public ReportService(
            ApplicationDbContext context,
            Analytics analytics,
            BalanceService balances,
            CurrencyService currency,
            BudgetService budgets,
            GoalService goals,
            DebtService debts,
            NetWorthService netWorth,
            UserPreferences preferences)
        {
            _context = context;
            _analytics = analytics;
            _balances = balances;
            _currency = currency;
            _budgets = budgets;
            _goals = goals;
            _debts = debts;
            _netWorth = netWorth;
            _preferences = preferences;
        }

        public static ReportDefinition? Find(string? key) =>
            All.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));

        public async Task<Report> BuildAsync(ReportDefinition definition, DateRange range)
        {
            var settings = await _preferences.GetAsync();

            var report = new Report
            {
                Definition = definition,
                Range = range,
                Currency = settings.ReportingCurrency
            };

            switch (definition.Key)
            {
                case "income": await ByCategoryAsync(report, TransactionType.Income); break;
                case "expenses": await ByCategoryAsync(report, TransactionType.Expense); break;
                case "income-vs-expenses": await IncomeVsExpensesAsync(report); break;
                case "spending-by-category": await SpendingAsync(report); break;
                case "account-balances": await BalancesAsync(report); break;
                case "cash-flow": await CashFlowAsync(report); break;
                case "budget-performance": await BudgetsAsync(report); break;
                case "savings-goals": await GoalsAsync(report); break;
                case "debt-summary": await DebtsAsync(report); break;
                case "net-worth": await NetWorthAsync(report); break;
            }

            return report;
        }

        private static void NoteUnconverted(Report report, int count)
        {
            if (count > 0)
            {
                report.Notes.Add(
                    $"{count} transaction(s) in the period have no exchange rate to {report.Currency} " +
                    "and are left out of these figures.");
            }
        }

        private static object Share(Report report, IEnumerable<CategoryTotal> totals)
        {
            var list = totals.ToList();
            var top = list.Take(7).ToList();
            var rest = list.Skip(7).Sum(t => t.Amount);

            var labels = top.Select(t => t.Name).ToList();
            var values = top.Select(t => t.Amount).ToList();

            if (rest > 0)
            {
                labels.Add("Other");
                values.Add(rest);
            }

            return new { labels, values, currency = report.Currency.ToString() };
        }

        private async Task ByCategoryAsync(Report report, TransactionType type)
        {
            var flows = await _analytics.FlowsAsync(report.Range);
            var totals = Analytics.ByCategory(flows, type);

            report.Columns.AddRange([new("Category"), new("Transactions", true), new($"Amount ({report.Currency})", true), new("Share", true)]);

            foreach (var t in totals)
            {
                report.Add(
                    t.Name,
                    ReportCell.Number(flows.Of(type).Count(f => f.CategoryId == t.CategoryId), "0"),
                    ReportCell.Money(t.Amount, report.Currency),
                    ReportCell.Percent(t.Share));
            }

            report.Totals =
            [
                "Total",
                ReportCell.Number(flows.Of(type).Count(), "0"),
                ReportCell.Money(totals.Sum(t => t.Amount), report.Currency),
                ReportCell.Percent(totals.Any() ? 100 : null)
            ];

            if (totals.Any())
                report.Chart = new ReportChart("share", Share(report, totals));

            NoteUnconverted(report, flows.Unconverted);
        }

        private async Task IncomeVsExpensesAsync(Report report)
        {
            var (points, unconverted) = await _analytics.SeriesAsync(Analytics.MonthsBetween(report.Range), SeriesGrain.Monthly);

            report.Columns.AddRange([new("Month"), new("Income", true), new("Expenses", true), new("Net", true), new("Saved", true)]);

            foreach (var p in points)
            {
                report.Add(
                    p.Label,
                    ReportCell.Money(p.Income, report.Currency),
                    ReportCell.Money(p.Expenses, report.Currency),
                    ReportCell.Money(p.Net, report.Currency),
                    ReportCell.Percent(p.Income > 0 ? Math.Round(p.Net / p.Income * 100, 1) : null));
            }

            var income = points.Sum(p => p.Income);
            var expenses = points.Sum(p => p.Expenses);

            report.Totals =
            [
                "Total",
                ReportCell.Money(income, report.Currency),
                ReportCell.Money(expenses, report.Currency),
                ReportCell.Money(income - expenses, report.Currency),
                ReportCell.Percent(income > 0 ? Math.Round((income - expenses) / income * 100, 1) : null)
            ];

            report.Chart = new ReportChart("flows", new
            {
                labels = points.Select(p => p.Label),
                income = points.Select(p => p.Income),
                expenses = points.Select(p => p.Expenses),
                currency = report.Currency.ToString()
            });

            report.Notes.Add("Months are counted whole, so the first and last month may run past the dates chosen.");
            NoteUnconverted(report, unconverted);
        }

        private async Task SpendingAsync(Report report)
        {
            var flows = await _analytics.FlowsAsync(report.Range);
            var before = await _analytics.FlowsAsync(Periods.Previous(report.Range));

            var now = Analytics.ByCategory(flows, TransactionType.Expense);
            var then = Analytics.ByCategory(before, TransactionType.Expense).ToDictionary(c => c.Name, c => c.Amount);

            report.Columns.AddRange([new("Category"), new("Spent", true), new("Share", true), new("Period before", true), new("Change", true)]);

            foreach (var c in now)
            {
                var previous = then.GetValueOrDefault(c.Name);

                report.Add(
                    c.Name,
                    ReportCell.Money(c.Amount, report.Currency),
                    ReportCell.Percent(c.Share),
                    ReportCell.Money(previous, report.Currency),
                    previous == 0 ? "new" : ReportCell.Percent(Math.Round((c.Amount - previous) / previous * 100, 1)));
            }

            foreach (var gone in then.Where(t => now.All(n => n.Name != t.Key)))
                report.Add(gone.Key, ReportCell.Money(0, report.Currency), ReportCell.Percent(0), ReportCell.Money(gone.Value, report.Currency), ReportCell.Percent(-100));

            report.Totals =
            [
                "Total",
                ReportCell.Money(flows.Expenses, report.Currency),
                ReportCell.Percent(now.Any() ? 100 : null),
                ReportCell.Money(before.Expenses, report.Currency),
                ReportCell.Percent(before.Expenses == 0 ? null : Math.Round((flows.Expenses - before.Expenses) / before.Expenses * 100, 1))
            ];

            if (now.Any())
                report.Chart = new ReportChart("share", Share(report, now));

            var previousRange = Periods.Previous(report.Range);
            report.Notes.Add($"The period before is {previousRange.Start:d MMM yyyy} – {previousRange.End:d MMM yyyy}.");
            NoteUnconverted(report, flows.Unconverted + before.Unconverted);
        }

        private async Task BalancesAsync(Report report)
        {
            var day = report.Range.End;
            var rates = await _currency.LoadAsync();
            var balances = await _balances.BalancesAsync(includeArchived: true, asOf: day);

            report.Columns.AddRange([new("Account"), new("Type"), new("Currency"), new("Balance", true), new($"In {report.Currency}", true), new("Rate used", true)]);

            var total = new ConvertedTotal { Currency = report.Currency };

            foreach (var b in balances.Where(b => !b.Account.IsArchived || b.Balance != 0))
            {
                var quote = rates.Find(b.Currency, report.Currency, day);
                total.Add(b.Balance, b.Currency, rates, day);

                report.Add(
                    b.Account.Name + (b.Account.IsArchived ? " (archived)" : ""),
                    b.Account.Type.Text(),
                    b.Currency.ToString(),
                    ReportCell.Money(b.Balance, b.Currency),
                    quote == null ? "no rate" : ReportCell.Money(Money.Round(b.Balance * quote.Rate), report.Currency),
                    quote == null ? "—" : b.Currency == report.Currency ? "—" : new ReportCell($"{quote.Rate:0.########} ({quote.EffectiveDate:d MMM yyyy})", quote.Rate));
            }

            report.Totals = ["Total", "", "", "", ReportCell.Money(total.Amount, report.Currency), ""];

            foreach (var (currency, amount) in total.Unconverted)
                report.Notes.Add($"{Money.Format(amount, currency)} is left out of the total: no {currency} → {report.Currency} rate on or before this day.");

            report.Notes.Add("Balances are the opening balance plus every transaction up to and including this day.");
        }

        private async Task CashFlowAsync(Report report)
        {
            var months = Analytics.MonthsBetween(report.Range);
            var flows = await _analytics.FlowsAsync(new DateRange(months[0].Start, months[^1].End));

            report.Columns.AddRange([
                new("Month"), new("Income", true), new("Debt received", true),
                new("Expenses", true), new("Debt paid", true), new("Net cash flow", true)
            ]);

            decimal Sum(DateRange m, TransactionType type) => flows.Of(type).Where(f => m.Contains(f.Date)).Sum(f => f.Amount);

            foreach (var m in months)
            {
                var income = Sum(m, TransactionType.Income);
                var debtIn = Sum(m, TransactionType.DebtIn);
                var expenses = Sum(m, TransactionType.Expense);
                var debtOut = Sum(m, TransactionType.DebtOut);

                report.Add(
                    m.Start.ToString("MMM yyyy"),
                    ReportCell.Money(income, report.Currency),
                    ReportCell.Money(debtIn, report.Currency),
                    ReportCell.Money(expenses, report.Currency),
                    ReportCell.Money(debtOut, report.Currency),
                    ReportCell.Money(income + debtIn - expenses - debtOut, report.Currency));
            }

            report.Totals =
            [
                "Total",
                ReportCell.Money(flows.Income, report.Currency),
                ReportCell.Money(flows.DebtIn, report.Currency),
                ReportCell.Money(flows.Expenses, report.Currency),
                ReportCell.Money(flows.DebtOut, report.Currency),
                ReportCell.Money(flows.Income + flows.DebtIn - flows.Expenses - flows.DebtOut, report.Currency)
            ];

            report.Notes.Add("Transfers between your own accounts are not cash flow and are left out.");
            report.Notes.Add("Debt received and paid is principal only; interest is already in income or expenses.");
            NoteUnconverted(report, flows.Unconverted);
        }

        private async Task BudgetsAsync(Report report)
        {
            var settings = await _preferences.GetAsync();
            var rates = await _currency.LoadAsync();

            var budgets = await _context.Budgets.AsNoTracking().Include(b => b.Category).OrderBy(b => b.Name).ToListAsync();

            var spending = await _context.Transactions.AsNoTracking()
                .Where(t => t.Type == TransactionType.Expense && t.Date <= report.Range.End)
                .ToListAsync();

            report.Columns.AddRange([new("Budget"), new("Period"), new("Budgeted", true), new("Spent", true), new("Remaining", true), new("Used", true), new("Status")]);

            foreach (var budget in budgets)
            {
                var window = Periods.BudgetWindow(budget, report.Range.Start);

                while (window.Start <= report.Range.End)
                {
                    if (Periods.BudgetRuns(budget, window))
                    {
                        var (spent, unconverted) = BudgetService.Total(
                            spending.Where(t => t.CategoryId == budget.CategoryId && window.Contains(t.Date)),
                            budget.Currency,
                            rates);

                        var status = BudgetService.Rate(budget, window, spent, unconverted, settings);

                        report.Add(
                            budget.Name,
                            $"{window.Start:d MMM yyyy} – {window.End:d MMM yyyy}",
                            ReportCell.Money(budget.Amount, budget.Currency),
                            ReportCell.Money(spent, budget.Currency),
                            ReportCell.Money(status.Remaining, budget.Currency),
                            ReportCell.Percent(status.Percent),
                            status.State switch { BudgetState.Over => "Over", BudgetState.Warning => "Near limit", _ => "On track" });

                        if (unconverted > 0)
                            report.Notes.Add($"{budget.Name}, {window.Start:MMM yyyy}: {unconverted} expense(s) had no rate to {budget.Currency} and are not counted.");
                    }

                    window = Periods.BudgetWindow(budget, window.End.AddDays(1));
                }
            }

            report.Notes.Add($"Near limit from {settings.BudgetWarningPercent}%, over from {settings.BudgetLimitPercent}%. Amounts are in each budget's own currency.");
        }

        private async Task GoalsAsync(Report report)
        {
            var goals = await _goals.ListAsync();

            var inPeriod = await _context.GoalContributions.AsNoTracking()
                .Where(c => c.Date >= report.Range.Start && c.Date <= report.Range.End)
                .GroupBy(c => c.GoalId)
                .Select(g => new { g.Key, Total = g.Sum(c => c.Amount) })
                .ToDictionaryAsync(x => x.Key, x => x.Total);

            report.Columns.AddRange([new("Goal"), new("Status"), new("Target", true), new("Saved", true), new("Remaining", true), new("Done", true), new("In this period", true), new("Target date")]);

            foreach (var g in goals)
            {
                report.Add(
                    g.Goal.Name,
                    g.Goal.Status.ToString(),
                    ReportCell.Money(g.Goal.TargetAmount, g.Goal.Currency),
                    ReportCell.Money(g.Saved, g.Goal.Currency),
                    ReportCell.Money(g.Remaining, g.Goal.Currency),
                    ReportCell.Percent(g.Percent),
                    ReportCell.Money(inPeriod.GetValueOrDefault(g.Goal.Id), g.Goal.Currency),
                    g.Goal.TargetDate?.ToString("d MMM yyyy") ?? "—");
            }

            report.Notes.Add("Saved is everything put towards the goal to date; each goal is in its own currency.");
        }

        private async Task DebtsAsync(Report report)
        {
            var debts = await _debts.ListAsync();

            var paid = await _context.DebtPayments.AsNoTracking()
                .Where(p => p.Date >= report.Range.Start && p.Date <= report.Range.End)
                .GroupBy(p => p.DebtId)
                .Select(g => new { g.Key, Principal = g.Sum(p => p.Principal), Interest = g.Sum(p => p.Interest) })
                .ToDictionaryAsync(x => x.Key);

            report.Columns.AddRange([
                new("Debt"), new("Direction"), new("Original", true), new("Repaid", true), new("Interest", true),
                new("Outstanding", true), new("Paid in period", true), new("Due"), new("Status")
            ]);

            foreach (var s in debts)
            {
                var d = s.Debt;
                var period = paid.TryGetValue(d.Id, out var p) ? p.Principal + p.Interest : 0;

                report.Add(
                    $"{d.Description} ({d.Counterparty})",
                    d.Direction == DebtDirection.IOwe ? "I owe" : "Owed to me",
                    ReportCell.Money(d.OriginalAmount, d.Currency),
                    ReportCell.Money(s.PrincipalPaid, d.Currency),
                    ReportCell.Money(s.InterestPaid, d.Currency),
                    ReportCell.Money(s.Outstanding, d.Currency),
                    ReportCell.Money(period, d.Currency),
                    d.DueDate?.ToString("d MMM yyyy") ?? "—",
                    s.IsOverdue ? "Overdue" : d.Status.Text());
            }

            report.Notes.Add("Repaid and interest are totals to date; paid in period covers the dates chosen. Each debt is in its own currency.");
        }

        private async Task NetWorthAsync(Report report)
        {
            var statement = await _netWorth.StatementAsync(report.Range.End);

            report.Columns.AddRange([new("Group"), new("Item"), new("Currency"), new("Value", true), new($"In {report.Currency}", true)]);

            foreach (var line in statement.Lines.OrderBy(l => l.IsLiability).ThenBy(l => l.Group))
            {
                var sign = line.IsLiability ? -1 : 1;

                report.Add(
                    line.Group,
                    line.Name,
                    line.Currency.ToString(),
                    ReportCell.Money(sign * line.Original, line.Currency),
                    line.Converted == null ? "no rate" : ReportCell.Money(sign * line.Converted.Value, report.Currency));
            }

            report.Totals = ["Net worth", "", "", "", ReportCell.Money(statement.NetWorth, report.Currency)];

            var trend = await _netWorth.TrendAsync(12);

            report.Chart = new ReportChart("line", new
            {
                label = "Net worth",
                labels = trend.Select(t => t.Month.ToString("MMM yyyy")),
                values = trend.Select(t => t.NetWorth),
                currency = report.Currency.ToString()
            });

            foreach (var line in statement.Unconverted)
                report.Notes.Add($"{line.Name} ({Money.Format(line.Original, line.Currency)}) is left out: no rate to {report.Currency}.");

            report.Notes.Add("Accounts and debts are counted from their own pages; other assets and liabilities at their latest valuation on or before the day.");
        }
    }
}
