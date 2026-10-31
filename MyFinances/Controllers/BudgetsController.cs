using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;
using MyFinances.Services;
using MyFinances.ViewModels;

namespace MyFinances.Controllers
{
    public class BudgetsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly BudgetService _budgets;
        private readonly CategoryService _categories;
        private readonly UserPreferences _preferences;

        public BudgetsController(
            ApplicationDbContext context,
            BudgetService budgets,
            CategoryService categories,
            UserPreferences preferences)
        {
            _context = context;
            _budgets = budgets;
            _categories = categories;
            _preferences = preferences;
        }

        /// <summary>The budgets running in a month; any past month can be looked back on.</summary>
        public async Task<IActionResult> Index(string? month = null)
        {
            var day = DateOnly.TryParseExact(month, "yyyy-MM", out var parsed)
                ? parsed
                : Periods.MonthStart(UserPreferences.Today);

            return View(new BudgetsIndexViewModel
            {
                Month = day,
                Budgets = await _budgets.StatusAsync(day),
                Settings = await _preferences.GetAsync(),
                AnyBudgets = await _context.Budgets.AnyAsync()
            });
        }

        public async Task<IActionResult> Details(int id)
        {
            var budget = await _context.Budgets.AsNoTracking().Include(b => b.Category).FirstOrDefaultAsync(b => b.Id == id);

            if (budget == null)
                return NotFound();

            return View(new BudgetDetailsViewModel
            {
                Budget = budget,
                History = await _budgets.HistoryAsync(budget, 12),
                Settings = await _preferences.GetAsync()
            });
        }

        public async Task<IActionResult> Create(int? categoryId = null)
        {
            var settings = await _preferences.GetAsync();

            return View("Form", await FillAsync(new BudgetFormViewModel
            {
                Currency = settings.ReportingCurrency,
                CategoryId = categoryId ?? 0
            }));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(BudgetFormViewModel form) => await SaveAsync(null, form);

        public async Task<IActionResult> Edit(int id)
        {
            var b = await _context.Budgets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

            if (b == null)
                return NotFound();

            return View("Form", await FillAsync(new BudgetFormViewModel
            {
                Id = b.Id,
                Name = b.Name,
                CategoryId = b.CategoryId,
                Period = b.Period,
                Amount = b.Amount,
                Currency = b.Currency,
                StartDate = b.StartDate,
                EndDate = b.EndDate,
                Notes = b.Notes
            }));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, BudgetFormViewModel form) => await SaveAsync(id, form);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _budgets.DeleteAsync(id);

            if (!result.Found)
                return NotFound();

            TempData["Flash"] = result.Message;

            return RedirectToAction(nameof(Index));
        }

        private async Task<IActionResult> SaveAsync(int? id, BudgetFormViewModel form)
        {
            form.Id = id;

            if (!ModelState.IsValid)
                return View("Form", await FillAsync(form));

            var result = await _budgets.SaveAsync(id, form);

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

        private async Task<BudgetFormViewModel> FillAsync(BudgetFormViewModel form)
        {
            form.Categories = await _categories.ActiveAsync(CategoryKind.Expense, form.CategoryId);
            return form;
        }
    }
}
