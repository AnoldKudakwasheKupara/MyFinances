using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Services;
using MyFinances.ViewModels;

namespace MyFinances.Controllers
{
    public class GoalsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly GoalService _goals;
        private readonly UserPreferences _preferences;

        public GoalsController(ApplicationDbContext context, GoalService goals, UserPreferences preferences)
        {
            _context = context;
            _goals = goals;
            _preferences = preferences;
        }

        public async Task<IActionResult> Index() => View(await _goals.ListAsync());

        public async Task<IActionResult> Details(int id)
        {
            var progress = (await _goals.ListAsync()).FirstOrDefault(g => g.Goal.Id == id);

            if (progress == null)
                return NotFound();

            return View(await DetailsModelAsync(progress));
        }

        public async Task<IActionResult> Create(string? idea = null)
        {
            var settings = await _preferences.GetAsync();
            var pick = GoalFormViewModel.Ideas.FirstOrDefault(i => i.Example == idea);

            return View("Form", await FillAsync(new GoalFormViewModel
            {
                Currency = settings.ReportingCurrency,
                Name = pick.Example is null or "Something else" ? string.Empty : pick.Example,
                Icon = pick.Icon ?? "piggy-bank"
            }));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Create(GoalFormViewModel form) => SaveAsync(null, form);

        public async Task<IActionResult> Edit(int id)
        {
            var g = await _context.SavingsGoals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

            if (g == null)
                return NotFound();

            return View("Form", await FillAsync(new GoalFormViewModel
            {
                Id = g.Id,
                Name = g.Name,
                Description = g.Description,
                TargetAmount = g.TargetAmount,
                Currency = g.Currency,
                TargetDate = g.TargetDate,
                AccountId = g.AccountId,
                Status = g.Status,
                Icon = g.Icon
            }));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Edit(int id, GoalFormViewModel form) => SaveAsync(id, form);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Contribute(int id, [Bind(Prefix = "Form")] ContributionFormViewModel form)
        {
            if (!ModelState.IsValid)
            {
                var progress = (await _goals.ListAsync()).FirstOrDefault(g => g.Goal.Id == id);
                return progress == null ? NotFound() : View(nameof(Details), await DetailsModelAsync(progress, form));
            }

            var result = await _goals.ContributeAsync(id, form);

            if (!result.Found)
                return NotFound();

            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Message!);
                var progress = (await _goals.ListAsync()).First(g => g.Goal.Id == id);
                return View(nameof(Details), await DetailsModelAsync(progress, form));
            }

            TempData["Flash"] = result.Message;

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveContribution(int id, int contributionId)
        {
            var result = await _goals.RemoveContributionAsync(id, contributionId);

            if (!result.Found)
                return NotFound();

            TempData["Flash"] = result.Message;

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _goals.DeleteAsync(id);

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

        private async Task<IActionResult> SaveAsync(int? id, GoalFormViewModel form)
        {
            form.Id = id;

            if (!ModelState.IsValid)
                return View("Form", await FillAsync(form));

            var result = await _goals.SaveAsync(id, form);

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

        private async Task<GoalFormViewModel> FillAsync(GoalFormViewModel form)
        {
            form.Accounts = await _context.Accounts.AsNoTracking()
                .Where(a => !a.IsArchived || a.Id == form.AccountId)
                .OrderBy(a => a.Name)
                .ToListAsync();

            return form;
        }

        private async Task<GoalDetailsViewModel> DetailsModelAsync(GoalProgress progress, ContributionFormViewModel? form = null) => new()
        {
            Progress = progress,
            Contributions = await _context.GoalContributions.AsNoTracking()
                .Include(c => c.Transactions).ThenInclude(t => t.Account)
                .Include(c => c.Transactions).ThenInclude(t => t.ToAccount)
                .Where(c => c.GoalId == progress.Goal.Id)
                .OrderByDescending(c => c.Date)
                .ThenByDescending(c => c.Id)
                .ToListAsync(),
            Form = form ?? new ContributionFormViewModel(),
            Accounts = await _context.Accounts.AsNoTracking()
                .Where(a => !a.IsArchived && a.Currency == progress.Goal.Currency && a.Id != progress.Goal.AccountId)
                .OrderBy(a => a.Name)
                .ToListAsync()
        };
    }
}
