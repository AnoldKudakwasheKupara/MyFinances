using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;
using MyFinances.ViewModels;

namespace MyFinances.Services
{
    public enum BudgetState
    {
        OnTrack,
        Warning,
        Over
    }

    public record BudgetStatus(
        Budget Budget,
        DateRange Window,
        decimal Spent,
        int Unconverted)
    {
        public decimal Remaining => Budget.Amount - Spent;

        public decimal Percent => Budget.Amount <= 0 ? 0 : Math.Round(Spent / Budget.Amount * 100, 1);

        public BudgetState State { get; init; }
    }

    /// <summary>
    /// Compares spending in a category with its budget, period by
    /// period. Spending in another currency is converted into the
    /// budget's: the stored conversion when that is the budget's
    /// currency, otherwise the recorded rate of the spending's date.
    /// Anything without a rate is counted separately, never guessed.
    /// </summary>
    public class BudgetService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrencyService _currency;
        private readonly UserPreferences _preferences;

        public BudgetService(ApplicationDbContext context, CurrencyService currency, UserPreferences preferences)
        {
            _context = context;
            _currency = currency;
            _preferences = preferences;
        }

        public async Task<List<BudgetStatus>> StatusAsync(DateOnly on, bool includeEnded = false)
        {
            var budgets = await _context.Budgets
                .AsNoTracking()
                .Include(b => b.Category)
                .OrderBy(b => b.Name)
                .ToListAsync();

            var results = new List<BudgetStatus>();

            foreach (var budget in budgets)
            {
                var window = Periods.BudgetWindow(budget, on);

                if (!includeEnded && !Periods.BudgetRuns(budget, window))
                    continue;

                results.Add(await StatusAsync(budget, window));
            }

            return results;
        }

        public async Task<BudgetStatus> StatusAsync(Budget budget, DateRange window)
        {
            var settings = await _preferences.GetAsync();
            var rates = await _currency.LoadAsync();

            var spending = await _context.Transactions
                .AsNoTracking()
                .Where(t => t.Type == TransactionType.Expense &&
                            t.CategoryId == budget.CategoryId &&
                            t.Date >= window.Start && t.Date <= window.End)
                .ToListAsync();

            var (spent, unconverted) = Total(spending, budget.Currency, rates);

            return Rate(budget, window, spent, unconverted, settings);
        }

        public static (decimal Spent, int Unconverted) Total(IEnumerable<Transaction> spending, Currency currency, RateBook rates)
        {
            decimal spent = 0;
            var unconverted = 0;

            foreach (var t in spending)
            {
                decimal? amount =
                    t.Currency == currency ? t.Amount
                    : t.ReportingCurrency == currency && t.ReportingAmount != null ? t.ReportingAmount
                    : rates.Convert(t.Amount, t.Currency, currency, t.Date);

                if (amount == null)
                    unconverted++;
                else
                    spent += amount.Value;
            }

            return (spent, unconverted);
        }

        public static BudgetStatus Rate(Budget budget, DateRange window, decimal spent, int unconverted, UserSettings settings)
        {
            var status = new BudgetStatus(budget, window, spent, unconverted);

            return status with
            {
                State = status.Percent >= settings.BudgetLimitPercent ? BudgetState.Over
                    : status.Percent >= settings.BudgetWarningPercent ? BudgetState.Warning
                    : BudgetState.OnTrack
            };
        }

        /// <summary>The last few periods of one budget, newest first.</summary>
        public async Task<List<BudgetStatus>> HistoryAsync(Budget budget, int periods)
        {
            var list = new List<BudgetStatus>();
            var window = Periods.BudgetWindow(budget, UserPreferences.Today);

            for (var i = 0; i < periods; i++)
            {
                if (window.End < budget.StartDate)
                    break;

                if (Periods.BudgetRuns(budget, window))
                    list.Add(await StatusAsync(budget, window));

                window = Periods.BudgetWindow(budget, window.Start.AddDays(-1));
            }

            return list;
        }

        public async Task<ServiceResult> SaveAsync(int? id, BudgetFormViewModel form)
        {
            var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == form.CategoryId);

            if (category == null || category.Kind != CategoryKind.Expense)
                return ServiceResult.Refused("Budgets are set on an expense category.");

            if (form.EndDate != null && form.EndDate < form.StartDate)
                return ServiceResult.Refused("The end date cannot be before the start date.");

            Budget? budget;

            if (id == null)
            {
                budget = new Budget();
                _context.Budgets.Add(budget);
            }
            else
            {
                budget = await _context.Budgets.FirstOrDefaultAsync(b => b.Id == id);

                if (budget == null)
                    return ServiceResult.Missing();
            }

            budget.Name = string.IsNullOrWhiteSpace(form.Name) ? category.Name : form.Name.Trim();
            budget.CategoryId = category.Id;
            budget.Period = form.Period;
            budget.Amount = Money.Round(form.Amount);
            budget.Currency = form.Currency;
            budget.StartDate = form.StartDate;
            budget.EndDate = form.EndDate;
            budget.Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim();

            await _context.SaveChangesAsync();

            form.Id = budget.Id;

            return ServiceResult.Ok(id == null ? $"The {budget.Name} budget was created." : "The budget was updated.");
        }

        public async Task<ServiceResult> DeleteAsync(int id)
        {
            var budget = await _context.Budgets.FirstOrDefaultAsync(b => b.Id == id);

            if (budget == null)
                return ServiceResult.Missing();

            _context.Budgets.Remove(budget);
            await _context.SaveChangesAsync();

            return ServiceResult.Ok($"The {budget.Name} budget was deleted. Your transactions are unchanged.");
        }
    }
}
