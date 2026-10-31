using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;
using MyFinances.ViewModels;

namespace MyFinances.Services
{
    public enum BillState
    {
        Upcoming,
        DueSoon,
        Overdue,
        Inactive
    }

    public record BillStatus(Bill Bill, BillState State)
    {
        public int DaysUntilDue => Bill.NextDueDate.DayNumber - UserPreferences.Today.DayNumber;
    }

    /// <summary>
    /// Recurring bills. Each one waits on its next due date; paying it
    /// settles that occurrence — once only, the database refuses a
    /// second payment for the same date — and moves the bill on to the
    /// next. The expense can be written to the ledger at the same time.
    /// </summary>
    public class BillService
    {
        private readonly ApplicationDbContext _context;
        private readonly LedgerService _ledger;

        public BillService(ApplicationDbContext context, LedgerService ledger)
        {
            _context = context;
            _ledger = ledger;
        }

        public static BillState StateOf(Bill bill, DateOnly today)
        {
            if (!bill.IsActive)
                return BillState.Inactive;

            if (bill.NextDueDate < today)
                return BillState.Overdue;

            return bill.NextDueDate.DayNumber - today.DayNumber <= Math.Max(bill.ReminderDays, 0)
                ? BillState.DueSoon
                : BillState.Upcoming;
        }

        public async Task<List<BillStatus>> ListAsync()
        {
            var today = UserPreferences.Today;

            var bills = await _context.Bills
                .AsNoTracking()
                .Include(b => b.Category)
                .Include(b => b.Account)
                .OrderByDescending(b => b.IsActive)
                .ThenBy(b => b.NextDueDate)
                .ToListAsync();

            return bills.Select(b => new BillStatus(b, StateOf(b, today))).ToList();
        }

        public async Task<ServiceResult> SaveAsync(int? id, BillFormViewModel form)
        {
            var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == form.CategoryId);

            if (category == null || category.Kind != CategoryKind.Expense)
                return ServiceResult.Refused("Pick an expense category for the bill.");

            if (form.AccountId != null)
            {
                var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == form.AccountId);

                if (account == null)
                    return ServiceResult.Refused("Pick one of your accounts.");

                if (account.Currency != form.Currency)
                    return ServiceResult.Refused($"{account.Name} is in {account.Currency}; the bill is in {form.Currency}.");
            }

            Bill? bill;

            if (id == null)
            {
                bill = new Bill { IsActive = true };
                _context.Bills.Add(bill);
            }
            else
            {
                bill = await _context.Bills.FirstOrDefaultAsync(b => b.Id == id);

                if (bill == null)
                    return ServiceResult.Missing();

                bill.IsActive = form.IsActive;
            }

            bill.Name = form.Name.Trim();
            bill.Type = form.Type;
            bill.Amount = Money.Round(form.Amount);
            bill.Currency = form.Currency;
            bill.CategoryId = category.Id;
            bill.AccountId = form.AccountId;
            bill.Frequency = form.Frequency;
            bill.NextDueDate = form.NextDueDate;
            bill.ReminderDays = form.ReminderDays;
            bill.Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim();

            await _context.SaveChangesAsync();

            form.Id = bill.Id;

            return ServiceResult.Ok(id == null ? $"{bill.Name} was added." : $"{bill.Name} was updated.");
        }

        public async Task<ServiceResult> PayAsync(int id, BillPaymentFormViewModel form)
        {
            var bill = await _context.Bills.FirstOrDefaultAsync(b => b.Id == id);

            if (bill == null)
                return ServiceResult.Missing();

            if (!bill.IsActive)
                return ServiceResult.Refused("This bill is paused. Turn it back on to record a payment.");

            if (form.Amount <= 0)
                return ServiceResult.Refused("Enter the amount paid.");

            var dueDate = bill.NextDueDate;

            if (await _context.BillPayments.AnyAsync(p => p.BillId == id && p.DueDate == dueDate))
                return ServiceResult.Refused($"The {dueDate:d MMM yyyy} payment has already been recorded.");

            var payment = new BillPayment
            {
                BillId = id,
                DueDate = dueDate,
                PaidDate = form.PaidDate,
                Amount = Money.Round(form.Amount),
                Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim()
            };

            if (form.RecordExpense)
            {
                var accountId = form.AccountId ?? bill.AccountId;

                if (accountId == null)
                    return ServiceResult.Refused("Pick the account it was paid from, or untick recording the expense.");

                var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);

                if (account != null && account.Currency != bill.Currency)
                    return ServiceResult.Refused($"{account.Name} is in {account.Currency}; the bill is in {bill.Currency}.");

                var expense = await _ledger.BuildAsync(new TransactionInput
                {
                    Type = TransactionType.Expense,
                    Date = form.PaidDate,
                    Amount = payment.Amount,
                    AccountId = accountId.Value,
                    CategoryId = bill.CategoryId,
                    Description = bill.Name,
                    Notes = $"Bill due {dueDate:d MMM yyyy}"
                });

                if (!expense.Succeeded)
                    return ServiceResult.Refused(expense.Error!);

                payment.Transactions.Add(expense.Transaction!);
            }

            _context.BillPayments.Add(payment);
            bill.NextDueDate = Periods.Advance(dueDate, bill.Frequency);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
            {
                // Two clicks raced: the unique index let only one through.
                return ServiceResult.Refused($"The {dueDate:d MMM yyyy} payment has already been recorded.");
            }

            return ServiceResult.Ok(
                $"{bill.Name} for {dueDate:d MMM yyyy} is paid" +
                (form.RecordExpense ? " and the expense recorded." : ".") +
                $" Next due {bill.NextDueDate:d MMM yyyy}.");
        }

        /// <summary>
        /// Takes back the most recent payment, with its expense, and puts
        /// the bill back on that due date. Only the latest can be undone,
        /// so the schedule cannot get out of step.
        /// </summary>
        public async Task<ServiceResult> UndoLatestAsync(int id)
        {
            var bill = await _context.Bills.FirstOrDefaultAsync(b => b.Id == id);

            if (bill == null)
                return ServiceResult.Missing();

            var latest = await _context.BillPayments
                .Include(p => p.Transactions)
                .Where(p => p.BillId == id)
                .OrderByDescending(p => p.DueDate)
                .FirstOrDefaultAsync();

            if (latest == null)
                return ServiceResult.Refused("There is no payment to undo.");

            _context.Transactions.RemoveRange(latest.Transactions);
            _context.BillPayments.Remove(latest);
            bill.NextDueDate = latest.DueDate;

            await _context.SaveChangesAsync();

            return ServiceResult.Ok($"The {latest.DueDate:d MMM yyyy} payment was undone. It is due again.");
        }

        /// <summary>Moves past an occurrence that will not be paid, without recording anything.</summary>
        public async Task<ServiceResult> SkipAsync(int id)
        {
            var bill = await _context.Bills.FirstOrDefaultAsync(b => b.Id == id);

            if (bill == null)
                return ServiceResult.Missing();

            var skipped = bill.NextDueDate;
            bill.NextDueDate = Periods.Advance(skipped, bill.Frequency);

            await _context.SaveChangesAsync();

            return ServiceResult.Ok($"Skipped {skipped:d MMM yyyy}. Next due {bill.NextDueDate:d MMM yyyy}.");
        }

        public async Task<ServiceResult> DeleteAsync(int id)
        {
            var bill = await _context.Bills
                .Include(b => b.Payments).ThenInclude(p => p.Transactions)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bill == null)
                return ServiceResult.Missing();

            if (bill.Payments.Any(p => p.Transactions.Any()))
            {
                return ServiceResult.Refused(
                    "Payments of this bill are in your ledger. Pause the bill instead to keep that history, " +
                    "or undo its payments first.");
            }

            _context.Bills.Remove(bill);
            await _context.SaveChangesAsync();

            return ServiceResult.Ok($"{bill.Name} was deleted.");
        }
    }
}
