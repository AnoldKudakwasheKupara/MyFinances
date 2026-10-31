using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;

namespace MyFinances.Services
{
    /// <summary>
    /// Gives a new person their settings and the starting categories.
    /// Safe to run again: it only adds what is missing, so it also
    /// repairs an account whose setup was interrupted.
    /// </summary>
    public class UserSetupService
    {
        public static readonly IReadOnlyList<(string Name, string Icon)> IncomeCategories =
        [
            ("Salary", "briefcase"),
            ("Business Income", "shop"),
            ("Freelance Income", "laptop"),
            ("Investment Income", "graph-up-arrow"),
            ("Interest", "percent"),
            ("Gifts Received", "gift"),
            ("Other Income", "plus-circle")
        ];

        public static readonly IReadOnlyList<(string Name, string Icon)> ExpenseCategories =
        [
            ("Food and Groceries", "basket"),
            ("Transport and Fuel", "fuel-pump"),
            ("Rent and Accommodation", "house-door"),
            ("Utilities", "lightning-charge"),
            ("Internet and Mobile Data", "wifi"),
            ("Shopping", "bag"),
            ("Entertainment", "film"),
            ("Education", "mortarboard"),
            ("Healthcare", "heart-pulse"),
            ("Family Support", "people"),
            ("Church and Donations", "heart"),
            ("Insurance", "shield-check"),
            ("Loan Repayments", "bank"),
            ("Subscriptions", "arrow-repeat"),
            ("Other Expenses", "three-dots")
        ];

        private readonly ApplicationDbContext _context;
        private readonly CurrentUser _currentUser;

        public UserSetupService(ApplicationDbContext context, CurrentUser currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        public async Task EnsureSetUpAsync(int userId, Currency? reportingCurrency = null)
        {
            using var _ = _currentUser.ActAs(userId);

            if (!await _context.UserSettings.AnyAsync())
            {
                _context.UserSettings.Add(new UserSettings
                {
                    UserId = userId,
                    ReportingCurrency = reportingCurrency ?? Currency.USD
                });
            }

            var existing = await _context.Categories
                .Select(c => new { c.Kind, c.Name })
                .ToListAsync();

            void AddMissing(CategoryKind kind, IEnumerable<(string Name, string Icon)> defaults)
            {
                foreach (var (name, icon) in defaults)
                {
                    if (existing.Any(c => c.Kind == kind && c.Name == name))
                        continue;

                    _context.Categories.Add(new Category
                    {
                        UserId = userId,
                        Kind = kind,
                        Name = name,
                        Icon = icon
                    });
                }
            }

            AddMissing(CategoryKind.Income, IncomeCategories);
            AddMissing(CategoryKind.Expense, ExpenseCategories);

            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Removes a person's ledger, the one table the database does not
        /// clear when their sign-in is deleted. Run it in the same
        /// database transaction as the delete.
        /// </summary>
        public Task<int> ClearLedgerAsync(int userId) =>
            _context.Transactions
                .IgnoreQueryFilters()
                .Where(t => t.UserId == userId)
                .ExecuteDeleteAsync();
    }
}
