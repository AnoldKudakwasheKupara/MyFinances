using System.ComponentModel.DataAnnotations;
using MyFinances.Entities;
using MyFinances.Services;

namespace MyFinances.ViewModels
{
    public class BudgetFormViewModel
    {
        public int? Id { get; set; }

        [StringLength(80)]
        [Display(Name = "Budget name")]
        public string? Name { get; set; }

        [Display(Name = "Category")]
        public int CategoryId { get; set; }

        [Display(Name = "Repeats")]
        public BudgetPeriod Period { get; set; } = BudgetPeriod.Monthly;

        [Range(0.01, 9_999_999_999_999, ErrorMessage = "Enter an amount above zero.")]
        public decimal Amount { get; set; }

        public Currency Currency { get; set; }

        [Display(Name = "Starts")]
        public DateOnly StartDate { get; set; } = Periods.MonthStart(UserPreferences.Today);

        [Display(Name = "Ends (optional)")]
        public DateOnly? EndDate { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }

        public List<Category> Categories { get; set; } = new();
    }

    public class BudgetsIndexViewModel
    {
        public DateOnly Month { get; init; }

        public List<BudgetStatus> Budgets { get; init; } = new();

        public UserSettings Settings { get; init; } = new();

        public bool AnyBudgets { get; init; }
    }

    public class BudgetDetailsViewModel
    {
        public Budget Budget { get; init; } = default!;

        public List<BudgetStatus> History { get; init; } = new();

        public UserSettings Settings { get; init; } = new();
    }
}

namespace MyFinances.ViewModels
{
    public class GoalFormViewModel
    {
        public int? Id { get; set; }

        [Required]
        [StringLength(80)]
        [Display(Name = "Goal")]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [Display(Name = "Target amount")]
        [Range(0.01, 9_999_999_999_999, ErrorMessage = "Enter a target above zero.")]
        public decimal TargetAmount { get; set; }

        public Currency Currency { get; set; }

        [Display(Name = "Target date")]
        public DateOnly? TargetDate { get; set; }

        [Display(Name = "Kept in account")]
        public int? AccountId { get; set; }

        public GoalStatus Status { get; set; } = GoalStatus.Active;

        public string Icon { get; set; } = "piggy-bank";

        public List<Account> Accounts { get; set; } = new();

        public static readonly IReadOnlyList<(string Icon, string Example)> Ideas =
        [
            ("shield-check", "Emergency Fund"),
            ("car-front", "Buying a Car"),
            ("house-heart", "Buying a House"),
            ("shop", "Business Capital"),
            ("mortarboard", "Education"),
            ("airplane", "Holiday"),
            ("bag-heart", "Personal Purchases"),
            ("piggy-bank", "Something else")
        ];

        public static IEnumerable<string> Icons => Ideas.Select(i => i.Icon);
    }

    public class ContributionFormViewModel
    {
        public DateOnly Date { get; set; } = UserPreferences.Today;

        [Range(0.01, 9_999_999_999_999, ErrorMessage = "Enter an amount above zero.")]
        public decimal Amount { get; set; }

        /// <summary>Taking money back out of the goal.</summary>
        public bool Withdraw { get; set; }

        /// <summary>
        /// Where the money comes from (or goes back to). When given, a
        /// transfer to or from the goal's own account is recorded.
        /// </summary>
        [Display(Name = "Move money from")]
        public int? OtherAccountId { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }
    }

    public class GoalDetailsViewModel
    {
        public GoalProgress Progress { get; init; } = default!;

        public List<GoalContribution> Contributions { get; init; } = new();

        public ContributionFormViewModel Form { get; init; } = new();

        public List<Account> Accounts { get; init; } = new();
    }
}
