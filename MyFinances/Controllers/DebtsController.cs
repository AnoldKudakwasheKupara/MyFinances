using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;
using MyFinances.Services;
using MyFinances.ViewModels;

namespace MyFinances.Controllers
{
    public class DebtsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly DebtService _debts;
        private readonly CurrencyService _currency;
        private readonly UserPreferences _preferences;

        public DebtsController(
            ApplicationDbContext context,
            DebtService debts,
            CurrencyService currency,
            UserPreferences preferences)
        {
            _context = context;
            _debts = debts;
            _currency = currency;
            _preferences = preferences;
        }

        public async Task<IActionResult> Index()
        {
            var settings = await _preferences.GetAsync();
            var rates = await _currency.LoadAsync();
            var all = await _debts.ListAsync();

            var iOwe = new ConvertedTotal { Currency = settings.ReportingCurrency };
            var owed = new ConvertedTotal { Currency = settings.ReportingCurrency };

            foreach (var s in all.Where(s => s.Debt.Status == DebtStatus.Active))
            {
                var total = s.Debt.Direction == DebtDirection.IOwe ? iOwe : owed;
                total.Add(s.Outstanding, s.Debt.Currency, rates, UserPreferences.Today);
            }

            return View(new DebtsIndexViewModel
            {
                IOwe = all.Where(s => s.Debt.Direction == DebtDirection.IOwe).ToList(),
                OwedToMe = all.Where(s => s.Debt.Direction == DebtDirection.OwedToMe).ToList(),
                TotalIOwe = iOwe,
                TotalOwedToMe = owed
            });
        }

        public async Task<IActionResult> Details(int id)
        {
            var standing = await _debts.StandingAsync(id);

            return standing == null ? NotFound() : View(await DetailsModelAsync(standing));
        }

        public async Task<IActionResult> Create(DebtDirection direction = DebtDirection.IOwe)
        {
            var settings = await _preferences.GetAsync();

            return View("Form", await FillAsync(new DebtFormViewModel
            {
                Direction = direction,
                Currency = settings.ReportingCurrency
            }));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DebtFormViewModel form)
        {
            if (!ModelState.IsValid)
                return View("Form", await FillAsync(form));

            var result = await _debts.CreateAsync(form);

            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Message!);
                return View("Form", await FillAsync(form));
            }

            TempData["Flash"] = result.Message;

            return RedirectToAction(nameof(Details), new { id = form.Id });
        }

        public async Task<IActionResult> Edit(int id)
        {
            var d = await _context.Debts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

            if (d == null)
                return NotFound();

            return View("Form", await FillAsync(new DebtFormViewModel
            {
                Id = d.Id,
                Direction = d.Direction,
                Description = d.Description,
                Counterparty = d.Counterparty,
                Type = d.Type,
                OriginalAmount = d.OriginalAmount,
                Currency = d.Currency,
                StartDate = d.StartDate,
                DueDate = d.DueDate,
                InterestRate = d.InterestRate,
                RepaymentFrequency = d.RepaymentFrequency,
                RepaymentAmount = d.RepaymentAmount,
                Notes = d.Notes,
                Status = d.Status
            }));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, DebtFormViewModel form)
        {
            form.Id = id;

            if (!ModelState.IsValid)
                return View("Form", await FillAsync(form));

            var result = await _debts.UpdateAsync(id, form);

            if (!result.Found)
                return NotFound();

            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Message!);
                return View("Form", await FillAsync(form));
            }

            TempData["Flash"] = result.Message;

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Pay(int id, [Bind(Prefix = "Form")] DebtPaymentFormViewModel form)
        {
            var standing = await _debts.StandingAsync(id);

            if (standing == null)
                return NotFound();

            if (!ModelState.IsValid)
                return View(nameof(Details), await DetailsModelAsync(standing, form));

            var result = await _debts.PayAsync(id, form);

            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Message!);
                return View(nameof(Details), await DetailsModelAsync(standing, form));
            }

            TempData["Flash"] = result.Message;

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemovePayment(int id, int paymentId)
        {
            var result = await _debts.RemovePaymentAsync(id, paymentId);

            if (!result.Found)
                return NotFound();

            TempData["Flash"] = result.Message;

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _debts.DeleteAsync(id);

            if (!result.Found)
                return NotFound();

            TempData["Flash"] = result.Message;

            return RedirectToAction(nameof(Index));
        }

        private async Task<DebtFormViewModel> FillAsync(DebtFormViewModel form)
        {
            form.Accounts = await _context.Accounts.AsNoTracking().Where(a => !a.IsArchived).OrderBy(a => a.Name).ToListAsync();
            return form;
        }

        private async Task<DebtDetailsViewModel> DetailsModelAsync(DebtStanding standing, DebtPaymentFormViewModel? form = null)
        {
            var debt = standing.Debt;

            return new DebtDetailsViewModel
            {
                Standing = standing,
                Payments = await _context.DebtPayments.AsNoTracking()
                    .Include(p => p.Account)
                    .Where(p => p.DebtId == debt.Id)
                    .OrderByDescending(p => p.Date)
                    .ThenByDescending(p => p.Id)
                    .ToListAsync(),
                Opening = await _context.Transactions.AsNoTracking()
                    .Include(t => t.Account)
                    .Where(t => t.DebtId == debt.Id)
                    .ToListAsync(),
                Form = form ?? new DebtPaymentFormViewModel { Principal = Math.Min(debt.RepaymentAmount ?? 0, standing.Outstanding) },
                Accounts = await _context.Accounts.AsNoTracking()
                    .Where(a => !a.IsArchived && a.Currency == debt.Currency)
                    .OrderBy(a => a.Name)
                    .ToListAsync()
            };
        }
    }
}
