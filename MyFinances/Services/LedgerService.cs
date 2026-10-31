using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;

namespace MyFinances.Services
{
    /// <summary>What a person (or another module) asks to record.</summary>
    public class TransactionInput
    {
        public TransactionType Type { get; set; }

        public DateOnly Date { get; set; }

        public string Description { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public int AccountId { get; set; }

        public int? ToAccountId { get; set; }

        /// <summary>For a transfer between currencies: what arrived.</summary>
        public decimal? ToAmount { get; set; }

        public int? CategoryId { get; set; }

        public string? Notes { get; set; }
    }

    public record LedgerResult(bool Succeeded, string? Error, Transaction? Transaction)
    {
        public static LedgerResult Fail(string error) => new(false, error, null);
    }

    /// <summary>
    /// The only way rows get into the ledger. It checks that each one
    /// makes sense — a positive amount, an account of the user's, the
    /// right kind of category, a transfer between two different
    /// accounts with both sides known — and takes the reporting
    /// snapshot. Balances are never touched: they follow from the rows.
    /// </summary>
    public class LedgerService
    {
        public const decimal MaxAmount = 9_999_999_999_999m;

        private readonly ApplicationDbContext _context;
        private readonly CurrencyService _currency;
        private readonly UserPreferences _preferences;
        private readonly ReceiptStorage _receipts;
        private readonly CurrentUser _currentUser;

        public LedgerService(
            ApplicationDbContext context,
            CurrencyService currency,
            UserPreferences preferences,
            ReceiptStorage receipts,
            CurrentUser currentUser)
        {
            _context = context;
            _currency = currency;
            _preferences = preferences;
            _receipts = receipts;
            _currentUser = currentUser;
        }

        public async Task<LedgerResult> CreateAsync(TransactionInput input, IFormFile? receipt = null)
        {
            var transaction = new Transaction();

            var error = await ApplyAsync(transaction, input, allowSystemTypes: false, existing: null);

            if (error != null)
                return LedgerResult.Fail(error);

            string? stored = null;

            if (receipt != null)
            {
                var saved = await _receipts.SaveAsync(receipt, _currentUser.RequiredUserId);

                if (saved.Error != null)
                    return LedgerResult.Fail(saved.Error);

                stored = saved.StoredName;
                transaction.ReceiptPath = stored;
                transaction.ReceiptFileName = Path.GetFileName(receipt.FileName);
                transaction.ReceiptContentType = saved.ContentType;
            }

            _context.Transactions.Add(transaction);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch
            {
                if (stored != null)
                    _receipts.Delete(_currentUser.RequiredUserId, stored);

                throw;
            }

            return new LedgerResult(true, null, transaction);
        }

        public async Task<ServiceResult> UpdateAsync(
            int id,
            TransactionInput input,
            IFormFile? receipt = null,
            bool removeReceipt = false)
        {
            var transaction = await _context.Transactions.FirstOrDefaultAsync(t => t.Id == id);

            if (transaction == null)
                return ServiceResult.Missing();

            if (transaction.IsLinked)
                return ServiceResult.Refused(LinkedMessage(transaction));

            var snapshot = new Transaction
            {
                AccountId = transaction.AccountId,
                ToAccountId = transaction.ToAccountId,
                CategoryId = transaction.CategoryId
            };

            var error = await ApplyAsync(transaction, input, allowSystemTypes: false, existing: snapshot);

            if (error != null)
                return ServiceResult.Refused(error);

            var userId = _currentUser.RequiredUserId;
            var oldReceipt = transaction.ReceiptPath;
            string? newReceipt = null;

            if (receipt != null)
            {
                var saved = await _receipts.SaveAsync(receipt, userId);

                if (saved.Error != null)
                    return ServiceResult.Refused(saved.Error);

                newReceipt = saved.StoredName;
                transaction.ReceiptPath = newReceipt;
                transaction.ReceiptFileName = Path.GetFileName(receipt.FileName);
                transaction.ReceiptContentType = saved.ContentType;
            }
            else if (removeReceipt)
            {
                transaction.ReceiptPath = null;
                transaction.ReceiptFileName = null;
                transaction.ReceiptContentType = null;
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch
            {
                if (newReceipt != null)
                    _receipts.Delete(userId, newReceipt);

                throw;
            }

            if (oldReceipt != null && oldReceipt != transaction.ReceiptPath)
                _receipts.Delete(userId, oldReceipt);

            return ServiceResult.Ok("The transaction was updated.");
        }

        public async Task<ServiceResult> DeleteAsync(int id)
        {
            var transaction = await _context.Transactions.FirstOrDefaultAsync(t => t.Id == id);

            if (transaction == null)
                return ServiceResult.Missing();

            if (transaction.IsLinked)
                return ServiceResult.Refused(LinkedMessage(transaction));

            var receipt = transaction.ReceiptPath;

            _context.Transactions.Remove(transaction);
            await _context.SaveChangesAsync();

            if (receipt != null)
                _receipts.Delete(_currentUser.RequiredUserId, receipt);

            return ServiceResult.Ok("The transaction was deleted. Balances and reports have been updated.");
        }

        /// <summary>
        /// Builds a ledger row for another module (bills, debts, goals)
        /// to attach to its own record and save alongside it.
        /// </summary>
        public async Task<LedgerResult> BuildAsync(TransactionInput input)
        {
            var transaction = new Transaction();

            var error = await ApplyAsync(transaction, input, allowSystemTypes: true, existing: null);

            return error == null
                ? new LedgerResult(true, null, transaction)
                : LedgerResult.Fail(error);
        }

        /// <summary>
        /// Checks the request and copies it onto the row. Returns why it
        /// was refused, or null when the row is ready to save.
        /// <paramref name="existing"/> carries the row's earlier account
        /// and category, which may stay even if since archived.
        /// </summary>
        private async Task<string?> ApplyAsync(
            Transaction target,
            TransactionInput input,
            bool allowSystemTypes,
            Transaction? existing)
        {
            if (!allowSystemTypes && input.Type is TransactionType.DebtIn or TransactionType.DebtOut)
                return "Debt payments are recorded from Debts & Loans.";

            if (string.IsNullOrWhiteSpace(input.Description))
                return "Enter a description.";

            if (input.Amount <= 0)
                return "Enter an amount above zero. Whether money comes in or goes out follows from the type.";

            if (input.Amount > MaxAmount)
                return "That amount is too large.";

            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == input.AccountId);

            if (account == null)
                return "Pick one of your accounts.";

            if (account.IsArchived && existing?.AccountId != account.Id)
                return $"{account.Name} is archived. Restore it first to record new money against it.";

            if (input.Date < account.OpeningDate)
                return $"{account.Name} was opened on {account.OpeningDate:d MMM yyyy}; the date cannot be earlier.";

            target.Type = input.Type;
            target.Date = input.Date;
            target.Description = input.Description.Trim();
            target.Amount = Money.Round(input.Amount);
            target.AccountId = account.Id;
            target.Currency = account.Currency;
            target.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();

            if (input.Type == TransactionType.Transfer)
            {
                if (input.ToAccountId == null)
                    return "Pick the account the money goes to.";

                if (input.ToAccountId == account.Id)
                    return "A transfer needs two different accounts.";

                var to = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == input.ToAccountId);

                if (to == null)
                    return "Pick one of your accounts to transfer to.";

                if (to.IsArchived && existing?.ToAccountId != to.Id)
                    return $"{to.Name} is archived. Restore it first to transfer into it.";

                if (input.Date < to.OpeningDate)
                    return $"{to.Name} was opened on {to.OpeningDate:d MMM yyyy}; the date cannot be earlier.";

                target.ToAccountId = to.Id;
                target.CategoryId = null;

                if (to.Currency == account.Currency)
                {
                    target.ToAmount = target.Amount;
                    target.TransferRate = null;
                }
                else
                {
                    if (input.ToAmount is not > 0)
                    {
                        return $"{account.Name} is in {account.Currency} and {to.Name} is in {to.Currency}. " +
                               $"Enter how much {to.Currency} arrived, so the conversion is recorded.";
                    }

                    if (input.ToAmount > MaxAmount)
                        return "The amount received is too large.";

                    target.ToAmount = Money.Round(input.ToAmount.Value);
                    target.TransferRate = Math.Round(target.ToAmount.Value / target.Amount, 8);
                }
            }
            else
            {
                target.ToAccountId = null;
                target.ToAmount = null;
                target.TransferRate = null;

                if (input.Type is TransactionType.Income or TransactionType.Expense)
                {
                    var kind = input.Type == TransactionType.Income ? CategoryKind.Income : CategoryKind.Expense;

                    if (input.CategoryId == null)
                        return $"Pick an {kind.ToString().ToLower()} category.";

                    var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == input.CategoryId);

                    if (category == null)
                        return "Pick one of your categories.";

                    if (category.Kind != kind)
                    {
                        return $"{category.Name} is an {category.Kind.ToString().ToLower()} category; " +
                               $"pick an {kind.ToString().ToLower()} one.";
                    }

                    if (!category.IsActive && existing?.CategoryId != category.Id)
                        return $"{category.Name} has been deactivated. Pick another category or activate it again.";

                    target.CategoryId = category.Id;
                }
                else
                {
                    target.CategoryId = input.CategoryId;
                }
            }

            var settings = await _preferences.GetAsync();

            CurrencyService.Snapshot(target, settings.ReportingCurrency, await _currency.LoadAsync());

            return null;
        }

        public static string LinkedMessage(Transaction t) =>
            t.BillPaymentId != null
                ? "This payment was recorded from Bills. Change or undo it there, so the bill stays right."
                : t.GoalContributionId != null
                    ? "This was recorded from a savings goal. Change or remove the contribution there."
                    : "This was recorded from Debts & Loans. Change or remove the payment there, so the balance owed stays right.";
    }

    public static class DatabaseTransactions
    {
        /// <summary>
        /// Runs work that writes several rows as one database
        /// transaction, retried as a whole if the connection drops.
        /// </summary>
        public static Task<T> InTransactionAsync<T>(this ApplicationDbContext context, Func<Task<T>> work)
        {
            var strategy = context.Database.CreateExecutionStrategy();

            return strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync();

                var result = await work();

                await transaction.CommitAsync();

                return result;
            });
        }
    }
}
