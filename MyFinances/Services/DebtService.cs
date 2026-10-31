using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;
using MyFinances.ViewModels;

namespace MyFinances.Services
{
    public record DebtStanding(Debt Debt, decimal PrincipalPaid, decimal InterestPaid)
    {
        public decimal Outstanding => Math.Max(0, Debt.OriginalAmount - PrincipalPaid);

        public decimal Percent => Debt.OriginalAmount <= 0 ? 100 : Math.Round(PrincipalPaid / Debt.OriginalAmount * 100, 1);

        public bool IsOverdue =>
            Debt.Status == DebtStatus.Active &&
            Outstanding > 0 &&
            Debt.DueDate != null &&
            Debt.DueDate.Value < UserPreferences.Today;
    }

    /// <summary>
    /// Money owed and owing. The balance outstanding is the original
    /// amount less principal repaid, never typed in.
    ///
    /// A payment splits principal from interest. Principal moves money
    /// between an account and the debt, so it is neither income nor
    /// spending; interest is a cost when you owe and earnings when you
    /// are owed. A payment made without an account touches only the
    /// debt, for money already recorded in the ledger, so nothing is
    /// counted twice.
    /// </summary>
    public class DebtService
    {
        public const string InterestPaidCategory = "Loan Repayments";
        public const string InterestEarnedCategory = "Interest";

        private readonly ApplicationDbContext _context;
        private readonly LedgerService _ledger;

        public DebtService(ApplicationDbContext context, LedgerService ledger)
        {
            _context = context;
            _ledger = ledger;
        }

        public async Task<List<DebtStanding>> ListAsync()
        {
            var debts = await _context.Debts
                .AsNoTracking()
                .OrderBy(d => d.Status)
                .ThenBy(d => d.DueDate)
                .ThenBy(d => d.Counterparty)
                .ToListAsync();

            var paid = await _context.DebtPayments
                .GroupBy(p => p.DebtId)
                .Select(g => new { g.Key, Principal = g.Sum(p => p.Principal), Interest = g.Sum(p => p.Interest) })
                .ToDictionaryAsync(x => x.Key);

            return debts
                .Select(d => paid.TryGetValue(d.Id, out var p)
                    ? new DebtStanding(d, p.Principal, p.Interest)
                    : new DebtStanding(d, 0, 0))
                .ToList();
        }

        public async Task<DebtStanding?> StandingAsync(int id) =>
            (await ListAsync()).FirstOrDefault(s => s.Debt.Id == id);

        public async Task<ServiceResult> CreateAsync(DebtFormViewModel form)
        {
            var debt = new Debt { Status = DebtStatus.Active };
            Copy(form, debt);

            if (form.DisbursementAccountId != null)
            {
                var opening = await _ledger.BuildAsync(new TransactionInput
                {
                    // Borrowing brings money in; lending sends it out.
                    Type = form.Direction == DebtDirection.IOwe ? TransactionType.DebtIn : TransactionType.DebtOut,
                    Date = form.StartDate,
                    Amount = form.OriginalAmount,
                    AccountId = form.DisbursementAccountId.Value,
                    Description = (form.Direction == DebtDirection.IOwe ? "Borrowed from " : "Lent to ") + form.Counterparty.Trim()
                });

                if (!opening.Succeeded)
                    return ServiceResult.Refused(opening.Error!);

                if (opening.Transaction!.Currency != form.Currency)
                    return ServiceResult.Refused("The account must be in the same currency as the debt.");

                debt.Transactions.Add(opening.Transaction);
            }

            _context.Debts.Add(debt);
            await _context.SaveChangesAsync();

            form.Id = debt.Id;

            return ServiceResult.Ok($"{debt.Description} was added.");
        }

        public async Task<ServiceResult> UpdateAsync(int id, DebtFormViewModel form)
        {
            var debt = await _context.Debts.Include(d => d.Transactions).FirstOrDefaultAsync(d => d.Id == id);

            if (debt == null)
                return ServiceResult.Missing();

            var principalPaid = await _context.DebtPayments.Where(p => p.DebtId == id).SumAsync(p => (decimal?)p.Principal) ?? 0;

            if (form.OriginalAmount < principalPaid)
                return ServiceResult.Refused($"{Money.Format(principalPaid, debt.Currency)} has already been repaid, so the amount cannot be less.");

            if (debt.Currency != form.Currency && (principalPaid > 0 || debt.Transactions.Any()))
                return ServiceResult.Refused("The currency cannot change once money has moved for this debt.");

            if (debt.Direction != form.Direction && debt.Transactions.Any())
                return ServiceResult.Refused("Whether you owe or are owed cannot change once money has moved for this debt.");

            if (debt.Transactions.Any() && debt.OriginalAmount != form.OriginalAmount)
            {
                // The money that changed hands at the start follows the amount.
                foreach (var t in debt.Transactions)
                {
                    t.Amount = Money.Round(form.OriginalAmount);
                    t.ReportingAmount = t.ReportingRate == null ? null : Money.Round(t.Amount * t.ReportingRate.Value);
                }
            }

            Copy(form, debt);
            debt.Status = form.Status;

            await _context.SaveChangesAsync();

            return ServiceResult.Ok("The debt was updated.");
        }

        public async Task<ServiceResult> PayAsync(int id, DebtPaymentFormViewModel form)
        {
            var standing = await StandingAsync(id);

            if (standing == null)
                return ServiceResult.Missing();

            var debt = await _context.Debts.FirstAsync(d => d.Id == id);

            var principal = Money.Round(form.Principal);
            var interest = Money.Round(form.Interest);

            if (principal < 0 || interest < 0 || principal + interest <= 0)
                return ServiceResult.Refused("Enter the principal, the interest, or both.");

            if (principal > standing.Outstanding)
                return ServiceResult.Refused($"Only {Money.Format(standing.Outstanding, debt.Currency)} is outstanding.");

            var payment = new DebtPayment
            {
                DebtId = id,
                Date = form.Date,
                Principal = principal,
                Interest = interest,
                AccountId = form.AccountId,
                Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim()
            };

            if (form.AccountId != null)
            {
                var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == form.AccountId);

                if (account == null)
                    return ServiceResult.Refused("Pick one of your accounts.");

                if (account.Currency != debt.Currency)
                    return ServiceResult.Refused($"{account.Name} is in {account.Currency}; this debt is in {debt.Currency}.");

                var iOwe = debt.Direction == DebtDirection.IOwe;
                var label = (iOwe ? "Repayment to " : "Repayment from ") + debt.Counterparty;

                if (principal > 0)
                {
                    var row = await _ledger.BuildAsync(new TransactionInput
                    {
                        Type = iOwe ? TransactionType.DebtOut : TransactionType.DebtIn,
                        Date = form.Date,
                        Amount = principal,
                        AccountId = account.Id,
                        Description = label
                    });

                    if (!row.Succeeded)
                        return ServiceResult.Refused(row.Error!);

                    payment.Transactions.Add(row.Transaction!);
                }

                if (interest > 0)
                {
                    var category = await InterestCategoryAsync(iOwe ? CategoryKind.Expense : CategoryKind.Income);

                    var row = await _ledger.BuildAsync(new TransactionInput
                    {
                        Type = iOwe ? TransactionType.Expense : TransactionType.Income,
                        Date = form.Date,
                        Amount = interest,
                        AccountId = account.Id,
                        CategoryId = category.Id,
                        Description = "Interest — " + debt.Counterparty
                    });

                    if (!row.Succeeded)
                        return ServiceResult.Refused(row.Error!);

                    payment.Transactions.Add(row.Transaction!);
                }
            }

            _context.DebtPayments.Add(payment);

            if (principal >= standing.Outstanding && debt.Status == DebtStatus.Active)
                debt.Status = DebtStatus.Settled;

            await _context.SaveChangesAsync();

            return ServiceResult.Ok(debt.Status == DebtStatus.Settled
                ? "Payment recorded. This debt is now settled."
                : $"Payment recorded. {Money.Format(standing.Outstanding - principal, debt.Currency)} is still outstanding.");
        }

        public async Task<ServiceResult> RemovePaymentAsync(int id, int paymentId)
        {
            var payment = await _context.DebtPayments
                .Include(p => p.Transactions)
                .Include(p => p.Debt)
                .FirstOrDefaultAsync(p => p.Id == paymentId && p.DebtId == id);

            if (payment == null)
                return ServiceResult.Missing();

            _context.Transactions.RemoveRange(payment.Transactions);
            _context.DebtPayments.Remove(payment);

            if (payment.Debt!.Status == DebtStatus.Settled && payment.Principal > 0)
                payment.Debt.Status = DebtStatus.Active;

            await _context.SaveChangesAsync();

            return ServiceResult.Ok("The payment was removed" + (payment.Transactions.Any() ? ", with its ledger entries." : "."));
        }

        public async Task<ServiceResult> DeleteAsync(int id)
        {
            var debt = await _context.Debts
                .Include(d => d.Transactions)
                .Include(d => d.Payments).ThenInclude(p => p.Transactions)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (debt == null)
                return ServiceResult.Missing();

            _context.Transactions.RemoveRange(debt.Transactions);
            _context.Transactions.RemoveRange(debt.Payments.SelectMany(p => p.Transactions));
            _context.Debts.Remove(debt);

            await _context.SaveChangesAsync();

            return ServiceResult.Ok($"{debt.Description} and its payments were deleted.");
        }

        /// <summary>
        /// Interest goes to the user's own category of that name; if it
        /// was deleted or renamed it is made again, so it never lands
        /// uncategorised.
        /// </summary>
        private async Task<Category> InterestCategoryAsync(CategoryKind kind)
        {
            var name = kind == CategoryKind.Expense ? InterestPaidCategory : InterestEarnedCategory;

            var category = await _context.Categories.FirstOrDefaultAsync(c => c.Kind == kind && c.Name == name);

            if (category == null)
            {
                category = new Category { Kind = kind, Name = name, Icon = kind == CategoryKind.Expense ? "bank" : "percent" };
                _context.Categories.Add(category);
                await _context.SaveChangesAsync();
            }
            else if (!category.IsActive)
            {
                category.IsActive = true;
            }

            return category;
        }

        private static void Copy(DebtFormViewModel form, Debt debt)
        {
            debt.Direction = form.Direction;
            debt.Description = form.Description.Trim();
            debt.Counterparty = form.Counterparty.Trim();
            debt.Type = form.Type;
            debt.OriginalAmount = Money.Round(form.OriginalAmount);
            debt.Currency = form.Currency;
            debt.StartDate = form.StartDate;
            debt.DueDate = form.DueDate;
            debt.InterestRate = form.InterestRate;
            debt.RepaymentFrequency = form.RepaymentFrequency;
            debt.RepaymentAmount = form.RepaymentAmount == null ? null : Money.Round(form.RepaymentAmount.Value);
            debt.Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim();
        }
    }
}
