using System.ComponentModel.DataAnnotations;
using MyFinances.Entities;

namespace MyFinances.ViewModels
{
    public record CategoryRow(Category Category, int TransactionCount);

    public class CategoryFormViewModel
    {
        public int? Id { get; set; }

        [Required]
        [StringLength(60)]
        public string Name { get; set; } = string.Empty;

        public CategoryKind Kind { get; set; } = CategoryKind.Expense;

        [StringLength(40)]
        [RegularExpression("^[a-z0-9-]*$", ErrorMessage = "Pick an icon from the list.")]
        public string? Icon { get; set; } = "tag";
    }

    public class CategoriesViewModel
    {
        public List<CategoryRow> Income { get; init; } = new();

        public List<CategoryRow> Expense { get; init; } = new();

        public CategoryFormViewModel Form { get; init; } = new();

        public static readonly IReadOnlyList<string> Icons =
        [
            "tag", "briefcase", "shop", "laptop", "graph-up-arrow", "percent", "gift", "plus-circle",
            "basket", "cart", "fuel-pump", "bus-front", "car-front", "house-door", "lightning-charge",
            "droplet", "wifi", "phone", "bag", "film", "controller", "music-note", "mortarboard", "book",
            "heart-pulse", "capsule", "people", "heart", "shield-check", "bank", "arrow-repeat",
            "cup-hot", "airplane", "scissors", "tools", "paw", "three-dots"
        ];
    }

    public class ExchangeRateFormViewModel
    {
        [Display(Name = "From")]
        public Currency FromCurrency { get; set; } = Currency.USD;

        [Display(Name = "To")]
        public Currency ToCurrency { get; set; } = Currency.ZWG;

        [Range(0.00000001, 1_000_000_000, ErrorMessage = "Enter a rate above zero.")]
        public decimal Rate { get; set; }

        [Display(Name = "Effective from")]
        public DateOnly EffectiveDate { get; set; } = Services.UserPreferences.Today;
    }

    public class ExchangeRatesViewModel
    {
        public List<ExchangeRate> Rates { get; init; } = new();

        public ExchangeRateFormViewModel Form { get; init; } = new();

        public Currency ReportingCurrency { get; init; }

        public int Unconverted { get; init; }

        /// <summary>Currencies the user holds money in that cannot reach the reporting currency today.</summary>
        public List<Currency> MissingToday { get; init; } = new();
    }

    public class SettingsViewModel
    {
        [Required]
        [StringLength(100)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Display(Name = "Main (reporting) currency")]
        public Currency ReportingCurrency { get; set; }

        [Display(Name = "Date format")]
        public string DateFormat { get; set; } = "dd MMM yyyy";

        [Range(1, 12)]
        [Display(Name = "Financial year starts in")]
        public int FinancialYearStartMonth { get; set; } = 1;

        [Display(Name = "Dashboard opens on")]
        public DashboardPeriod DefaultDashboardPeriod { get; set; }

        [Range(1, 99, ErrorMessage = "Pick a warning between 1% and 99%.")]
        [Display(Name = "Warn when a budget reaches (%)")]
        public int BudgetWarningPercent { get; set; } = 80;

        [Range(50, 200, ErrorMessage = "Pick a limit between 50% and 200%.")]
        [Display(Name = "Treat a budget as over at (%)")]
        public int BudgetLimitPercent { get; set; } = 100;

        public ThemePreference Theme { get; set; }

        [Range(0, 60)]
        [Display(Name = "Remind me about bills (days ahead)")]
        public int BillReminderDays { get; set; } = 3;

        [Display(Name = "Email me a reminder digest")]
        public bool EmailReminders { get; set; }

        [Display(Name = "Show archived accounts in lists")]
        public bool ShowArchivedAccounts { get; set; }

        [Display(Name = "Default account for new transactions")]
        public int? DefaultAccountId { get; set; }

        public List<Account> Accounts { get; set; } = new();

        public bool EmailAvailable { get; set; }
    }
}
