using System.ComponentModel.DataAnnotations;
using MyFinances.Entities;
using MyFinances.Services;

namespace MyFinances.ViewModels
{
    public class HoldingFormViewModel
    {
        public int? Id { get; set; }

        public HoldingKind Kind { get; set; } = HoldingKind.Asset;

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public HoldingCategory Category { get; set; } = HoldingCategory.Property;

        public Currency Currency { get; set; }

        [Display(Name = "Acquired on")]
        public DateOnly? AcquiredOn { get; set; }

        [Display(Name = "Current value")]
        [Range(0, 9_999_999_999_999, ErrorMessage = "Enter a value of zero or more.")]
        public decimal? Value { get; set; }

        [Display(Name = "Valued on")]
        public DateOnly ValuedOn { get; set; } = UserPreferences.Today;

        [Display(Name = "Sold or settled")]
        public bool IsClosed { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }
    }

    public class ValuationFormViewModel
    {
        public DateOnly Date { get; set; } = UserPreferences.Today;

        [Range(0, 9_999_999_999_999, ErrorMessage = "Enter a value of zero or more.")]
        public decimal Value { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }
    }

    public class HoldingsIndexViewModel
    {
        public NetWorthStatement Statement { get; init; } = new();

        public List<(Holding Holding, HoldingValuation? Latest)> Holdings { get; init; } = new();
    }
}
