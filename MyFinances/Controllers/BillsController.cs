using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;
using MyFinances.Services;
using MyFinances.ViewModels;

namespace MyFinances.Controllers
{
    public class BillsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly BillService _bills;
        private readonly CategoryService _categories;
        private readonly UserPreferences _preferences;

        public BillsController(
            ApplicationDbContext context,
            BillService bills,
            CategoryService categories,
            UserPreferences preferences)
        {
            _context = context;
            _bills = bills;
            _categories = categories;
            _preferences = preferences;
        }

        public async Task<IActionResult> Index() => View(await _bills.ListAsync());

        public async Task<IActionResult> Details(int id)
        {
            var status = (await _bills.ListAsync()).FirstOrDefault(b => b.Bill.Id == id);

            return status == null ? NotFound() : View(await DetailsModelAsync(status));
        }

        public async Task<IActionResult> Create()
        {
            var settings = await _preferences.GetAsync();

            return View("Form", await FillAsync(new BillFormViewModel
            {
                Currency = settings.ReportingCurrency,
                ReminderDays = settings.BillReminderDays,
                AccountId = settings.DefaultAccountId
            }));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Create(BillFormViewModel form) => SaveAsync(null, form);

        public async Task<IActionResult> Edit(int id)
        {
            var b = await _context.Bills.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

            if (b == null)
                return NotFound();

            return View("Form", await FillAsync(new BillFormViewModel
            {
                Id = b.Id,
                Name = b.Name,
                Type = b.Type,
                Amount = b.Amount,
                Currency = b.Currency,
                CategoryId = b.CategoryId,
                AccountId = b.AccountId,
                Frequency = b.Frequency,
                NextDueDate = b.NextDueDate,
                ReminderDays = b.ReminderDays,
                IsActive = b.IsActive,
                Notes = b.Notes
            }));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Edit(int id, BillFormViewModel form) => SaveAsync(id, form);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Pay(int id, [Bind(Prefix = "Form")] BillPaymentFormViewModel form)
        {
            var status = (await _bills.ListAsync()).FirstOrDefault(b => b.Bill.Id == id);

            if (status == null)
                return NotFound();

            if (!ModelState.IsValid)
                return View(nameof(Details), await DetailsModelAsync(status, form));

            var result = await _bills.PayAsync(id, form);

            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Message!);
                return View(nameof(Details), await DetailsModelAsync(status, form));
            }

            TempData["Flash"] = result.Message;

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Undo(int id) => Done(await _bills.UndoLatestAsync(id), id);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Skip(int id) => Done(await _bills.SkipAsync(id), id);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _bills.DeleteAsync(id);

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

        private IActionResult Done(ServiceResult result, int id)
        {
            if (!result.Found)
                return NotFound();

            TempData[result.Succeeded ? "Flash" : "FlashError"] = result.Message;

            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task<IActionResult> SaveAsync(int? id, BillFormViewModel form)
        {
            form.Id = id;

            if (!ModelState.IsValid)
                return View("Form", await FillAsync(form));

            var result = await _bills.SaveAsync(id, form);

            if (!result.Found)
                return NotFound();

            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Message!);
                return View("Form", await FillAsync(form));
            }

            TempData["Flash"] = result.Message;

            return RedirectToAction(nameof(Details), new { id = form.Id });
        }

        private async Task<BillFormViewModel> FillAsync(BillFormViewModel form)
        {
            form.Categories = await _categories.ActiveAsync(CategoryKind.Expense, form.CategoryId);
            form.Accounts = await _context.Accounts.AsNoTracking()
                .Where(a => !a.IsArchived || a.Id == form.AccountId)
                .OrderBy(a => a.Name)
                .ToListAsync();

            return form;
        }

        private async Task<BillDetailsViewModel> DetailsModelAsync(BillStatus status, BillPaymentFormViewModel? form = null)
        {
            var bill = status.Bill;

            return new BillDetailsViewModel
            {
                Status = status,
                Payments = await _context.BillPayments.AsNoTracking()
                    .Include(p => p.Transactions).ThenInclude(t => t.Account)
                    .Where(p => p.BillId == bill.Id)
                    .OrderByDescending(p => p.DueDate)
                    .ToListAsync(),
                Form = form ?? new BillPaymentFormViewModel { Amount = bill.Amount, AccountId = bill.AccountId },
                Accounts = await _context.Accounts.AsNoTracking()
                    .Where(a => !a.IsArchived && a.Currency == bill.Currency)
                    .OrderBy(a => a.Name)
                    .ToListAsync()
            };
        }
    }
}
