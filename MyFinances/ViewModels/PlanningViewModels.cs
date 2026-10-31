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

namespace MyFinances.ViewModels
{
    public class DebtFormViewModel
    {
        public int? Id { get; set; }

        public DebtDirection Direction { get; set; } = DebtDirection.IOwe;

        [Required]
        [StringLength(200)]
        public string Description { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Display(Name = "Lender or borrower")]
        public string Counterparty { get; set; } = string.Empty;

        [Display(Name = "Kind of debt")]
        public DebtType Type { get; set; } = DebtType.PersonalLoan;

        [Display(Name = "Original amount")]
        [Range(0.01, 9_999_999_999_999, ErrorMessage = "Enter an amount above zero.")]
        public decimal OriginalAmount { get; set; }

        public Currency Currency { get; set; }

        [Display(Name = "Start date")]
        public DateOnly StartDate { get; set; } = UserPreferences.Today;

        [Display(Name = "Due date")]
        public DateOnly? DueDate { get; set; }

        [Display(Name = "Interest rate (% a year)")]
        [Range(0, 1000)]
        public decimal? InterestRate { get; set; }

        [Display(Name = "Repayments")]
        public Frequency? RepaymentFrequency { get; set; }

        [Display(Name = "Repayment amount")]
        [Range(0, 9_999_999_999_999)]
        public decimal? RepaymentAmount { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        public DebtStatus Status { get; set; } = DebtStatus.Active;

        /// <summary>
        /// On a new debt only: the account the borrowed money arrived in,
        /// or the lent money left from. Leave empty if that is already in
        /// the ledger or the debt is older than your records.
        /// </summary>
        [Display(Name = "Money moved through")]
        public int? DisbursementAccountId { get; set; }

        public List<Account> Accounts { get; set; } = new();
    }

    public class DebtPaymentFormViewModel
    {
        public DateOnly Date { get; set; } = UserPreferences.Today;

        [Range(0, 9_999_999_999_999)]
        public decimal Principal { get; set; }

        [Range(0, 9_999_999_999_999)]
        public decimal Interest { get; set; }

        [Display(Name = "Account")]
        public int? AccountId { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }
    }

    public class DebtsIndexViewModel
    {
        public List<DebtStanding> IOwe { get; init; } = new();

        public List<DebtStanding> OwedToMe { get; init; } = new();

        public ConvertedTotal TotalIOwe { get; init; } = new();

        public ConvertedTotal TotalOwedToMe { get; init; } = new();
    }

    public class DebtDetailsViewModel
    {
        public DebtStanding Standing { get; init; } = default!;

        public List<DebtPayment> Payments { get; init; } = new();

        public List<Transaction> Opening { get; init; } = new();

        public DebtPaymentFormViewModel Form { get; init; } = new();

        public List<Account> Accounts { get; init; } = new();
    }
}

namespace MyFinances.ViewModels
{
    public class BillFormViewModel
    {
        public int? Id { get; set; }

        [Required]
        [StringLength(80)]
        [Display(Name = "Bill")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Kind of bill")]
        public BillType Type { get; set; } = BillType.Utilities;

        [Range(0.01, 9_999_999_999_999, ErrorMessage = "Enter an amount above zero.")]
        public decimal Amount { get; set; }

        public Currency Currency { get; set; }

        [Display(Name = "Category")]
        public int CategoryId { get; set; }

        [Display(Name = "Usually paid from")]
        public int? AccountId { get; set; }

        [Display(Name = "Repeats")]
        public Frequency Frequency { get; set; } = Frequency.Monthly;

        [Display(Name = "Next due")]
        public DateOnly NextDueDate { get; set; } = UserPreferences.Today;

        [Range(0, 60)]
        [Display(Name = "Remind me (days before)")]
        public int ReminderDays { get; set; } = 3;

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        [StringLength(500)]
        public string? Notes { get; set; }

        public List<Category> Categories { get; set; } = new();

        public List<Account> Accounts { get; set; } = new();
    }

    public class BillPaymentFormViewModel
    {
        [Display(Name = "Paid on")]
        public DateOnly PaidDate { get; set; } = UserPreferences.Today;

        [Range(0.01, 9_999_999_999_999, ErrorMessage = "Enter the amount paid.")]
        public decimal Amount { get; set; }

        [Display(Name = "Paid from")]
        public int? AccountId { get; set; }

        [Display(Name = "Record the expense in my transactions")]
        public bool RecordExpense { get; set; } = true;

        [StringLength(500)]
        public string? Notes { get; set; }
    }

    public class BillDetailsViewModel
    {
        public BillStatus Status { get; init; } = default!;

        public List<BillPayment> Payments { get; init; } = new();

        public BillPaymentFormViewModel Form { get; init; } = new();

        public List<Account> Accounts { get; init; } = new();
    }
}
