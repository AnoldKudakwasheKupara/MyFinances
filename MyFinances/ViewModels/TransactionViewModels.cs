using System.ComponentModel.DataAnnotations;
using MyFinances.Entities;
using MyFinances.Services;

namespace MyFinances.ViewModels
{
    public class TransactionFormViewModel
    {
        public int? Id { get; set; }

        public TransactionType Type { get; set; } = TransactionType.Expense;

        public DateOnly Date { get; set; } = UserPreferences.Today;

        [Required]
        [StringLength(200)]
        public string Description { get; set; } = string.Empty;

        [Range(0.01, 9_999_999_999_999, ErrorMessage = "Enter an amount above zero.")]
        public decimal Amount { get; set; }

        [Display(Name = "Account")]
        public int AccountId { get; set; }

        [Display(Name = "To account")]
        public int? ToAccountId { get; set; }

        [Display(Name = "Amount received")]
        [Range(0.01, 9_999_999_999_999, ErrorMessage = "Enter the amount that arrived.")]
        public decimal? ToAmount { get; set; }

        [Display(Name = "Category")]
        public int? IncomeCategoryId { get; set; }

        [Display(Name = "Category")]
        public int? ExpenseCategoryId { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        [Display(Name = "Receipt")]
        public IFormFile? Receipt { get; set; }

        public bool RemoveReceipt { get; set; }

        public string? ExistingReceiptName { get; set; }

        public List<Account> Accounts { get; set; } = new();

        public List<Category> IncomeCategories { get; set; } = new();

        public List<Category> ExpenseCategories { get; set; } = new();

        public TransactionInput ToInput() => new()
        {
            Type = Type,
            Date = Date,
            Description = Description,
            Amount = Amount,
            AccountId = AccountId,
            ToAccountId = Type == TransactionType.Transfer ? ToAccountId : null,
            ToAmount = Type == TransactionType.Transfer ? ToAmount : null,
            CategoryId = Type switch
            {
                TransactionType.Income => IncomeCategoryId,
                TransactionType.Expense => ExpenseCategoryId,
                _ => null
            },
            Notes = Notes
        };
    }

    public class TransactionsIndexViewModel
    {
        public TransactionFilter Filter { get; init; } = new();

        public PagedResult<Transaction> Results { get; init; } = new();

        public List<Account> Accounts { get; init; } = new();

        public List<Category> Categories { get; init; } = new();

        public Currency ReportingCurrency { get; init; }

        public decimal TotalIn { get; init; }

        public decimal TotalOut { get; init; }

        public int Unconverted { get; init; }

        public bool HasAccounts { get; init; }
    }
}
