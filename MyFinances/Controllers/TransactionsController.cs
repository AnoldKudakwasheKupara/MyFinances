using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;
using MyFinances.Services;
using MyFinances.ViewModels;

namespace MyFinances.Controllers
{
    public class TransactionsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly LedgerService _ledger;
        private readonly TransactionSearch _search;
        private readonly CategoryService _categories;
        private readonly UserPreferences _preferences;
        private readonly ReceiptStorage _receipts;
        private readonly CurrentUser _currentUser;

        public TransactionsController(
            ApplicationDbContext context,
            LedgerService ledger,
            TransactionSearch search,
            CategoryService categories,
            UserPreferences preferences,
            ReceiptStorage receipts,
            CurrentUser currentUser)
        {
            _context = context;
            _ledger = ledger;
            _search = search;
            _categories = categories;
            _preferences = preferences;
            _receipts = receipts;
            _currentUser = currentUser;
        }

        public async Task<IActionResult> Index([FromQuery] TransactionFilter filter)
        {
            if (filter.From != null && filter.To != null && filter.From > filter.To)
                (filter.From, filter.To) = (filter.To, filter.From);

            var settings = await _preferences.GetAsync();
            var totals = await _search.TotalsAsync(filter, settings.ReportingCurrency);

            return View(new TransactionsIndexViewModel
            {
                Filter = filter,
                Results = await _search.PageAsync(filter),
                Accounts = await _context.Accounts.AsNoTracking().OrderBy(a => a.IsArchived).ThenBy(a => a.Name).ToListAsync(),
                Categories = await _context.Categories.AsNoTracking().OrderBy(c => c.Kind).ThenBy(c => c.Name).ToListAsync(),
                ReportingCurrency = settings.ReportingCurrency,
                TotalIn = totals.In,
                TotalOut = totals.Out,
                Unconverted = totals.Unconverted,
                HasAccounts = await _context.Accounts.AnyAsync()
            });
        }

        public async Task<IActionResult> Export([FromQuery] TransactionFilter filter)
        {
            var bytes = await _search.CsvAsync(filter);

            return File(bytes, "text/csv", $"transactions-{UserPreferences.Today:yyyy-MM-dd}.csv");
        }

        public async Task<IActionResult> Details(int id)
        {
            var transaction = await _context.Transactions
                .AsNoTracking()
                .Include(t => t.Account)
                .Include(t => t.ToAccount)
                .Include(t => t.Category)
                .FirstOrDefaultAsync(t => t.Id == id);

            return transaction == null ? NotFound() : View(transaction);
        }

        public async Task<IActionResult> Create(TransactionType type = TransactionType.Expense, int? accountId = null)
        {
            if (type is TransactionType.DebtIn or TransactionType.DebtOut)
                type = TransactionType.Expense;

            var settings = await _preferences.GetAsync();

            var form = new TransactionFormViewModel
            {
                Type = type,
                AccountId = accountId ?? settings.DefaultAccountId ?? 0
            };

            await FillAsync(form);

            if (!form.Accounts.Any())
            {
                TempData["FlashError"] = "Add an account first, so the money has somewhere to come from or go to.";
                return RedirectToAction("Create", "Accounts");
            }

            return View("Form", form);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(ReceiptStorage.MaxBytes + 1024 * 1024)]
        public async Task<IActionResult> Create(TransactionFormViewModel form, string? next = null)
        {
            Tidy(form);

            if (!ModelState.IsValid)
                return View("Form", await FillAsync(form));

            var result = await _ledger.CreateAsync(form.ToInput(), form.Receipt);

            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Error!);
                return View("Form", await FillAsync(form));
            }

            TempData["Flash"] = $"{form.Type.Text()} of {Money.Format(result.Transaction!.Amount, result.Transaction.Currency)} recorded.";

            return next == "another"
                ? RedirectToAction(nameof(Create), new { type = form.Type, accountId = form.AccountId })
                : RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var t = await _context.Transactions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

            if (t == null)
                return NotFound();

            if (t.IsLinked)
            {
                TempData["FlashError"] = LedgerService.LinkedMessage(t);
                return RedirectToAction(nameof(Details), new { id });
            }

            var form = new TransactionFormViewModel
            {
                Id = t.Id,
                Type = t.Type,
                Date = t.Date,
                Description = t.Description,
                Amount = t.Amount,
                AccountId = t.AccountId,
                ToAccountId = t.ToAccountId,
                ToAmount = t.ToAmount,
                IncomeCategoryId = t.Type == TransactionType.Income ? t.CategoryId : null,
                ExpenseCategoryId = t.Type == TransactionType.Expense ? t.CategoryId : null,
                Notes = t.Notes,
                ExistingReceiptName = t.ReceiptFileName
            };

            return View("Form", await FillAsync(form, t));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(ReceiptStorage.MaxBytes + 1024 * 1024)]
        public async Task<IActionResult> Edit(int id, TransactionFormViewModel form)
        {
            form.Id = id;
            Tidy(form);

            var existing = await _context.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);

            if (existing == null)
                return NotFound();

            form.ExistingReceiptName = existing.ReceiptFileName;

            if (!ModelState.IsValid)
                return View("Form", await FillAsync(form, existing));

            var result = await _ledger.UpdateAsync(id, form.ToInput(), form.Receipt, form.RemoveReceipt);

            if (!result.Found)
                return NotFound();

            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Message!);
                return View("Form", await FillAsync(form, existing));
            }

            TempData["Flash"] = result.Message;

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _ledger.DeleteAsync(id);

            if (!result.Found)
                return NotFound();

            if (!result.Succeeded)
            {
                TempData["FlashError"] = result.Message;
                return RedirectToAction(nameof(Details), new { id });
            }

            TempData["Flash"] = result.Message;

            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Hands over a receipt only after loading its transaction
        /// through the user's own records, so nobody can fetch another
        /// person's file by guessing an id.
        /// </summary>
        public async Task<IActionResult> Receipt(int id)
        {
            var t = await _context.Transactions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

            if (t?.ReceiptPath == null)
                return NotFound();

            var stream = _receipts.Open(_currentUser.RequiredUserId, t.ReceiptPath);

            if (stream == null)
                return NotFound();

            Response.Headers["X-Content-Type-Options"] = "nosniff";

            return File(stream, t.ReceiptContentType ?? "application/octet-stream", t.ReceiptFileName ?? t.ReceiptPath);
        }

        /// <summary>Ignore checks on fields the chosen type does not use.</summary>
        private void Tidy(TransactionFormViewModel form)
        {
            if (form.Type != TransactionType.Transfer)
            {
                ModelState.Remove(nameof(form.ToAmount));
                ModelState.Remove(nameof(form.ToAccountId));
                form.ToAmount = null;
            }

            if (form.Type is not (TransactionType.Income or TransactionType.Expense or TransactionType.Transfer))
                ModelState.AddModelError(nameof(form.Type), "Pick income, expense or transfer.");
        }

        private async Task<TransactionFormViewModel> FillAsync(TransactionFormViewModel form, Transaction? existing = null)
        {
            var keep = new[] { existing?.AccountId ?? 0, existing?.ToAccountId ?? 0 };

            form.Accounts = await _context.Accounts
                .AsNoTracking()
                .Where(a => !a.IsArchived || keep.Contains(a.Id))
                .OrderBy(a => a.Name)
                .ToListAsync();

            form.IncomeCategories = await _categories.ActiveAsync(CategoryKind.Income, existing?.CategoryId);
            form.ExpenseCategories = await _categories.ActiveAsync(CategoryKind.Expense, existing?.CategoryId);

            return form;
        }
    }
}
