using MyFinances.Data;

namespace MyFinances.Entities
{
    /// <summary>
    /// Somewhere money is kept. The balance is never stored: it is the
    /// opening balance plus everything in the ledger since, worked out
    /// by <see cref="Services.BalanceService"/>.
    /// </summary>
    public class Account : IUserOwned
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public ApplicationUser? User { get; set; }

        public string Name { get; set; } = string.Empty;

        public AccountType Type { get; set; }

        public Currency Currency { get; set; }

        public decimal OpeningBalance { get; set; }

        public DateOnly OpeningDate { get; set; }

        public string? Description { get; set; }

        public bool IsArchived { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }

    public class Category : IUserOwned
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public string Name { get; set; } = string.Empty;

        public CategoryKind Kind { get; set; }

        /// <summary>A Bootstrap Icons name, such as "cart".</summary>
        public string Icon { get; set; } = "tag";

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// One unit of <see cref="FromCurrency"/> is worth <see cref="Rate"/>
    /// of <see cref="ToCurrency"/> from <see cref="EffectiveDate"/>
    /// until a later rate for the same pair. Always entered by hand;
    /// MyFinances never assumes two currencies are worth the same.
    /// </summary>
    public class ExchangeRate : IUserOwned
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public Currency FromCurrency { get; set; }

        public Currency ToCurrency { get; set; }

        public decimal Rate { get; set; }

        public DateOnly EffectiveDate { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    public class UserSettings : IUserOwned
    {
        public int UserId { get; set; }

        public ApplicationUser? User { get; set; }

        public Currency ReportingCurrency { get; set; } = Currency.USD;

        /// <summary>A .NET date pattern, one of <see cref="Services.DateFormats"/>.</summary>
        public string DateFormat { get; set; } = "dd MMM yyyy";

        public int FinancialYearStartMonth { get; set; } = 1;

        public DashboardPeriod DefaultDashboardPeriod { get; set; }

        public int BudgetWarningPercent { get; set; } = 80;

        public int BudgetLimitPercent { get; set; } = 100;

        public ThemePreference Theme { get; set; } = ThemePreference.Light;

        /// <summary>Days ahead a new bill reminds by default.</summary>
        public int BillReminderDays { get; set; } = 3;

        public bool EmailReminders { get; set; }

        /// <summary>The day the last reminder digest went out, so it goes once a day.</summary>
        public DateOnly? LastReminderEmailOn { get; set; }

        public bool ShowArchivedAccounts { get; set; }

        /// <summary>Picked first on a new transaction.</summary>
        public int? DefaultAccountId { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
