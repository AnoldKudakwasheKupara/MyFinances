using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;

namespace MyFinances.Services
{
    public enum ReminderLevel
    {
        Info,
        Warning,
        Urgent
    }

    public record Reminder(ReminderLevel Level, string Icon, string Title, string Detail, string Url);

    /// <summary>
    /// What needs the user's attention, worked out fresh from their
    /// records each time: bills due or overdue, budgets near or over,
    /// debts past their date, goals running late, and spending that
    /// cannot be converted. Nothing is stored, so a reminder goes away
    /// as soon as its cause does.
    /// </summary>
    public class ReminderService
    {
        private readonly ApplicationDbContext _context;
        private readonly BudgetService _budgets;
        private readonly DebtService _debts;
        private readonly GoalService _goals;
        private readonly CurrencyService _currency;
        private readonly UserPreferences _preferences;

        private List<Reminder>? _cached;

        public ReminderService(
            ApplicationDbContext context,
            BudgetService budgets,
            DebtService debts,
            GoalService goals,
            CurrencyService currency,
            UserPreferences preferences)
        {
            _context = context;
            _budgets = budgets;
            _debts = debts;
            _goals = goals;
            _currency = currency;
            _preferences = preferences;
        }

        public async Task<List<Reminder>> ListAsync()
        {
            if (_cached != null)
                return _cached;

            var today = UserPreferences.Today;
            var settings = await _preferences.GetAsync();
            var list = new List<Reminder>();

            var bills = await _context.Bills.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.NextDueDate).ToListAsync();

            foreach (var bill in bills)
            {
                var state = BillService.StateOf(bill, today);
                var days = bill.NextDueDate.DayNumber - today.DayNumber;

                if (state == BillState.Overdue)
                {
                    list.Add(new Reminder(ReminderLevel.Urgent, "calendar-x", $"{bill.Name} is overdue",
                        $"{Money.Format(bill.Amount, bill.Currency)} was due {bill.NextDueDate:d MMM} ({-days} day(s) ago).",
                        $"/Bills/Details/{bill.Id}"));
                }
                else if (state == BillState.DueSoon)
                {
                    list.Add(new Reminder(ReminderLevel.Warning, "calendar-event",
                        days == 0 ? $"{bill.Name} is due today" : $"{bill.Name} is due in {days} day(s)",
                        $"{Money.Format(bill.Amount, bill.Currency)} on {bill.NextDueDate:d MMM}.",
                        $"/Bills/Details/{bill.Id}"));
                }
            }

            foreach (var status in await _budgets.StatusAsync(today))
            {
                if (status.State == BudgetState.Over)
                {
                    list.Add(new Reminder(ReminderLevel.Urgent, "pie-chart", $"{status.Budget.Name} budget is over",
                        $"{Money.Format(status.Spent, status.Budget.Currency)} spent of {Money.Format(status.Budget.Amount, status.Budget.Currency)} ({status.Percent:0}%).",
                        $"/Budgets/Details/{status.Budget.Id}"));
                }
                else if (status.State == BudgetState.Warning)
                {
                    list.Add(new Reminder(ReminderLevel.Warning, "pie-chart", $"{status.Budget.Name} budget is at {status.Percent:0}%",
                        $"{Money.Format(status.Remaining, status.Budget.Currency)} left until {status.Window.End:d MMM}.",
                        $"/Budgets/Details/{status.Budget.Id}"));
                }
            }

            foreach (var debt in await _debts.ListAsync())
            {
                var d = debt.Debt;

                if (debt.IsOverdue)
                {
                    list.Add(new Reminder(ReminderLevel.Urgent, "cash-coin",
                        d.Direction == DebtDirection.IOwe ? $"Repayment to {d.Counterparty} is overdue" : $"{d.Counterparty} is overdue paying you",
                        $"{Money.Format(debt.Outstanding, d.Currency)} outstanding, due {d.DueDate:d MMM yyyy}.",
                        $"/Debts/Details/{d.Id}"));
                }
                else if (d.Status == DebtStatus.Active && debt.Outstanding > 0 && d.DueDate != null &&
                         d.DueDate.Value.DayNumber - today.DayNumber is >= 0 and <= 7)
                {
                    list.Add(new Reminder(ReminderLevel.Warning, "cash-coin", $"{d.Description} is due {d.DueDate:d MMM}",
                        $"{Money.Format(debt.Outstanding, d.Currency)} outstanding.",
                        $"/Debts/Details/{d.Id}"));
                }
            }

            foreach (var goal in await _goals.ListAsync(activeOnly: true))
            {
                var g = goal.Goal;

                if (g.TargetDate != null && g.TargetDate < today && goal.Remaining > 0)
                {
                    list.Add(new Reminder(ReminderLevel.Info, "piggy-bank", $"{g.Name} passed its target date",
                        $"{Money.Format(goal.Remaining, g.Currency)} still to save. Set a new date or keep going.",
                        $"/Goals/Details/{g.Id}"));
                }
            }

            var unconverted = await _currency.UnconvertedCountAsync(settings.ReportingCurrency);

            if (unconverted > 0)
            {
                list.Add(new Reminder(ReminderLevel.Info, "currency-exchange", "Some amounts have no exchange rate",
                    $"{unconverted} transaction(s) are left out of totals in {settings.ReportingCurrency} until a rate is recorded.",
                    "/ExchangeRates"));
            }

            return _cached = list
                .OrderByDescending(r => r.Level)
                .ToList();
        }

        /// <summary>The number on the bell: only what needs doing.</summary>
        public async Task<int> CountAsync() =>
            (await ListAsync()).Count(r => r.Level != ReminderLevel.Info);
    }
}
