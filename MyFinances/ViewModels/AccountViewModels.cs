using System.ComponentModel.DataAnnotations;
using MyFinances.Entities;
using MyFinances.Services;

namespace MyFinances.ViewModels
{
    public class AccountFormViewModel
    {
        public int? Id { get; set; }

        [Required]
        [StringLength(80)]
        [Display(Name = "Account name")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Account type")]
        public AccountType Type { get; set; } = AccountType.Bank;

        public Currency Currency { get; set; } = Currency.USD;

        [Display(Name = "Opening balance")]
        [Range(-9_999_999_999_999, 9_999_999_999_999, ErrorMessage = "Enter a realistic amount.")]
        public decimal OpeningBalance { get; set; }

        [Display(Name = "Opening date")]
        public DateOnly OpeningDate { get; set; } = UserPreferences.Today;

        [StringLength(500)]
        public string? Description { get; set; }

        /// <summary>Once money has moved, the currency is fixed.</summary>
        public bool CurrencyLocked { get; set; }
    }

    public class AccountsIndexViewModel
    {
        public List<AccountBalance> Active { get; init; } = new();

        public List<AccountBalance> Archived { get; init; } = new();

        public Dictionary<Currency, decimal> ByCurrency { get; init; } = new();

        public ConvertedTotal Total { get; init; } = new();

        public bool ShowArchived { get; init; }
    }

    public class AccountDetailsViewModel
    {
        public Account Account { get; init; } = default!;

        public decimal Balance { get; init; }

        public decimal InThisMonth { get; init; }

        public decimal OutThisMonth { get; init; }

        public List<Transaction> Recent { get; init; } = new();

        public int TransactionCount { get; init; }

        public bool CanDelete { get; init; }
    }
}
