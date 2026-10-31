using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;
using MyFinances.Services;
using MyFinances.ViewModels;

namespace MyFinances.Controllers
{
    public class HoldingsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly NetWorthService _netWorth;
        private readonly UserPreferences _preferences;

        public HoldingsController(ApplicationDbContext context, NetWorthService netWorth, UserPreferences preferences)
        {
            _context = context;
            _netWorth = netWorth;
            _preferences = preferences;
        }

        public async Task<IActionResult> Index()
        {
            var holdings = await _context.Holdings.AsNoTracking()
                .Include(h => h.Valuations)
                .OrderBy(h => h.IsClosed)
                .ThenBy(h => h.Kind)
                .ThenBy(h => h.Name)
                .ToListAsync();

            return View(new HoldingsIndexViewModel
            {
                Statement = await _netWorth.StatementAsync(),
                Holdings = holdings
                    .Select(h => (h, h.Valuations.OrderByDescending(v => v.Date).ThenByDescending(v => v.Id).FirstOrDefault()))
                    .ToList()
            });
        }

        public async Task<IActionResult> Details(int id)
        {
            var holding = await _context.Holdings.AsNoTracking()
                .Include(h => h.Valuations)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (holding == null)
                return NotFound();

            ViewData["Valuation"] = new ValuationFormViewModel
            {
                Value = holding.Valuations.OrderByDescending(v => v.Date).FirstOrDefault()?.Value ?? 0
            };

            return View(holding);
        }

        public async Task<IActionResult> Create(HoldingKind kind = HoldingKind.Asset)
        {
            var settings = await _preferences.GetAsync();

            return View("Form", new HoldingFormViewModel
            {
                Kind = kind,
                Category = kind == HoldingKind.Asset ? HoldingCategory.Property : HoldingCategory.Loan,
                Currency = settings.ReportingCurrency
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(HoldingFormViewModel form)
        {
            if (form.Value == null)
                ModelState.AddModelError(nameof(form.Value), "Enter what it is worth today.");

            if (!ModelState.IsValid)
                return View("Form", form);

            var holding = new Holding();
            Copy(form, holding);
            holding.Valuations.Add(new HoldingValuation { Date = form.ValuedOn, Value = Money.Round(form.Value!.Value), Notes = "First valuation" });

            _context.Holdings.Add(holding);
            await _context.SaveChangesAsync();

            TempData["Flash"] = $"{holding.Name} was added.";

            return RedirectToAction(nameof(Details), new { id = holding.Id });
        }

        public async Task<IActionResult> Edit(int id)
        {
            var h = await _context.Holdings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

            if (h == null)
                return NotFound();

            return View("Form", new HoldingFormViewModel
            {
                Id = h.Id,
                Kind = h.Kind,
                Name = h.Name,
                Category = h.Category,
                Currency = h.Currency,
                AcquiredOn = h.AcquiredOn,
                IsClosed = h.IsClosed,
                Notes = h.Notes
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, HoldingFormViewModel form)
        {
            form.Id = id;
            ModelState.Remove(nameof(form.Value));

            if (!ModelState.IsValid)
                return View("Form", form);

            var holding = await _context.Holdings.FirstOrDefaultAsync(h => h.Id == id);

            if (holding == null)
                return NotFound();

            Copy(form, holding);
            await _context.SaveChangesAsync();

            TempData["Flash"] = $"{holding.Name} was updated.";

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Revalue(int id, [Bind(Prefix = "Valuation")] ValuationFormViewModel form)
        {
            var holding = await _context.Holdings.FirstOrDefaultAsync(h => h.Id == id);

            if (holding == null)
                return NotFound();

            if (!ModelState.IsValid)
            {
                TempData["FlashError"] = "Enter a value of zero or more.";
                return RedirectToAction(nameof(Details), new { id });
            }

            _context.HoldingValuations.Add(new HoldingValuation
            {
                HoldingId = id,
                Date = form.Date,
                Value = Money.Round(form.Value),
                Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim()
            });

            holding.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            TempData["Flash"] = $"{holding.Name} is now valued at {Money.Format(form.Value, holding.Currency)}.";

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveValuation(int id, int valuationId)
        {
            var valuations = await _context.HoldingValuations.Where(v => v.HoldingId == id).ToListAsync();
            var valuation = valuations.FirstOrDefault(v => v.Id == valuationId);

            if (valuation == null)
                return NotFound();

            if (valuations.Count == 1)
            {
                TempData["FlashError"] = "Keep at least one valuation, or delete the item.";
                return RedirectToAction(nameof(Details), new { id });
            }

            _context.HoldingValuations.Remove(valuation);
            await _context.SaveChangesAsync();

            TempData["Flash"] = "The valuation was removed.";

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var holding = await _context.Holdings.FirstOrDefaultAsync(h => h.Id == id);

            if (holding == null)
                return NotFound();

            _context.Holdings.Remove(holding);
            await _context.SaveChangesAsync();

            TempData["Flash"] = $"{holding.Name} was deleted.";

            return RedirectToAction(nameof(Index));
        }

        private static void Copy(HoldingFormViewModel form, Holding holding)
        {
            holding.Kind = form.Kind;
            holding.Name = form.Name.Trim();
            holding.Category = form.Category;
            holding.Currency = form.Currency;
            holding.AcquiredOn = form.AcquiredOn;
            holding.IsClosed = form.IsClosed;
            holding.Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim();
        }
    }
}
