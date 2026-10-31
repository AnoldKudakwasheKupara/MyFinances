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
