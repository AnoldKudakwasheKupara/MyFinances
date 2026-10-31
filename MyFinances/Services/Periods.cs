using MyFinances.Entities;

namespace MyFinances.Services
{
    public record DateRange(DateOnly Start, DateOnly End)
    {
        public bool Contains(DateOnly date) => date >= Start && date <= End;

        public int Days => End.DayNumber - Start.DayNumber + 1;
    }

    /// <summary>Calendar arithmetic shared by budgets, bills and reports.</summary>
    public static class Periods
    {
        public static DateOnly MonthStart(DateOnly date) => new(date.Year, date.Month, 1);

        public static DateRange Month(DateOnly date)
        {
            var start = MonthStart(date);
            return new DateRange(start, start.AddMonths(1).AddDays(-1));
        }

        public static DateRange Quarter(DateOnly date)
        {
            var start = new DateOnly(date.Year, (date.Month - 1) / 3 * 3 + 1, 1);
            return new DateRange(start, start.AddMonths(3).AddDays(-1));
        }

        public static DateRange Year(DateOnly date) =>
            new(new DateOnly(date.Year, 1, 1), new DateOnly(date.Year, 12, 31));

        /// <summary>The financial year containing a date, starting in the given month.</summary>
        public static DateRange FinancialYear(DateOnly date, int startMonth)
        {
            var year = date.Month >= startMonth ? date.Year : date.Year - 1;
            var start = new DateOnly(year, startMonth, 1);
            return new DateRange(start, start.AddYears(1).AddDays(-1));
        }

        public static int MonthsIn(BudgetPeriod period) => period switch
        {
            BudgetPeriod.Quarterly => 3,
            BudgetPeriod.Yearly => 12,
            _ => 1
        };

        /// <summary>
        /// The budget period containing a date. Periods run back to back
        /// from the first of the month the budget started in.
        /// </summary>
        public static DateRange BudgetWindow(Budget budget, DateOnly date)
        {
            var months = MonthsIn(budget.Period);
            var anchor = MonthStart(budget.StartDate);
            var elapsed = (date.Year - anchor.Year) * 12 + date.Month - anchor.Month;
            var index = (int)Math.Floor(elapsed / (double)months);
            var start = anchor.AddMonths(index * months);

            return new DateRange(start, start.AddMonths(months).AddDays(-1));
        }

        public static bool BudgetRuns(Budget budget, DateRange window) =>
            budget.StartDate <= window.End && (budget.EndDate == null || budget.EndDate >= window.Start);

        public static DateOnly Advance(DateOnly date, Frequency frequency, int times = 1) => frequency switch
        {
            Frequency.Weekly => date.AddDays(7 * times),
            Frequency.Quarterly => date.AddMonths(3 * times),
            Frequency.Yearly => date.AddYears(times),
            _ => date.AddMonths(times)
        };

        /// <summary>The same span immediately before, for comparisons.</summary>
        public static DateRange Previous(DateRange range)
        {
            var isWholeMonths = range.Start.Day == 1 && range.End.AddDays(1).Day == 1;

            if (isWholeMonths)
            {
                var months = (range.End.Year - range.Start.Year) * 12 + range.End.Month - range.Start.Month + 1;
                var start = range.Start.AddMonths(-months);
                return new DateRange(start, range.Start.AddDays(-1));
            }

            return new DateRange(range.Start.AddDays(-range.Days), range.Start.AddDays(-1));
        }
    }
}
