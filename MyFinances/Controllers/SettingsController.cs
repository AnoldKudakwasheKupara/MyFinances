using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;
using MyFinances.Services;
using MyFinances.ViewModels;

namespace MyFinances.Controllers
{
    public class SettingsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly CurrencyService _currency;
        private readonly UserSetupService _setup;
        private readonly CurrentUser _currentUser;
        private readonly EmailService _email;

        public SettingsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            CurrencyService currency,
            UserSetupService setup,
            CurrentUser currentUser,
            EmailService email)
        {
            _context = context;
            _userManager = userManager;
            _signInManager = signInManager;
            _currency = currency;
            _setup = setup;
            _currentUser = currentUser;
            _email = email;
        }

        public async Task<IActionResult> Index()
        {
            var settings = await LoadAsync();
            var user = await _userManager.GetUserAsync(User);

            var model = new SettingsViewModel
            {
                FullName = user?.FullName ?? string.Empty,
                ReportingCurrency = settings.ReportingCurrency,
                DateFormat = settings.DateFormat,
                FinancialYearStartMonth = settings.FinancialYearStartMonth,
                DefaultDashboardPeriod = settings.DefaultDashboardPeriod,
                BudgetWarningPercent = settings.BudgetWarningPercent,
                BudgetLimitPercent = settings.BudgetLimitPercent,
                Theme = settings.Theme,
                BillReminderDays = settings.BillReminderDays,
                EmailReminders = settings.EmailReminders,
                ShowArchivedAccounts = settings.ShowArchivedAccounts,
                DefaultAccountId = settings.DefaultAccountId
            };

            return View(await FillAsync(model));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(SettingsViewModel model)
        {
            if (!DateFormats.All.Contains(model.DateFormat))
                ModelState.AddModelError(nameof(model.DateFormat), "Pick one of the date formats.");

            if (model.BudgetWarningPercent >= model.BudgetLimitPercent)
                ModelState.AddModelError(nameof(model.BudgetWarningPercent), "The warning must come before the limit.");

            if (model.DefaultAccountId != null &&
                !await _context.Accounts.AnyAsync(a => a.Id == model.DefaultAccountId && !a.IsArchived))
            {
                ModelState.AddModelError(nameof(model.DefaultAccountId), "Pick one of your active accounts.");
            }

            if (!ModelState.IsValid)
                return View(await FillAsync(model));

            var settings = await LoadAsync();
            var currencyChanged = settings.ReportingCurrency != model.ReportingCurrency;

            settings.ReportingCurrency = model.ReportingCurrency;
            settings.DateFormat = model.DateFormat;
            settings.FinancialYearStartMonth = model.FinancialYearStartMonth;
            settings.DefaultDashboardPeriod = model.DefaultDashboardPeriod;
            settings.BudgetWarningPercent = model.BudgetWarningPercent;
            settings.BudgetLimitPercent = model.BudgetLimitPercent;
            settings.Theme = model.Theme;
            settings.BillReminderDays = model.BillReminderDays;
            settings.EmailReminders = model.EmailReminders && _email.IsEnabled;
            settings.ShowArchivedAccounts = model.ShowArchivedAccounts;
            settings.DefaultAccountId = model.DefaultAccountId;

            await _context.SaveChangesAsync();

            var user = await _userManager.GetUserAsync(User);

            if (user != null && user.FullName != model.FullName.Trim())
            {
                user.FullName = model.FullName.Trim();
                await _userManager.UpdateAsync(user);
                await _signInManager.RefreshSignInAsync(user);
            }

            var message = "Your settings were saved.";

            if (currencyChanged)
            {
                await _currency.RefreshSnapshotsAsync(model.ReportingCurrency, onlyMissing: false);

                message += $" Every transaction was converted again to {model.ReportingCurrency}, " +
                           "each at the rate recorded for its own date.";
            }

            TempData["Flash"] = message;

            return RedirectToAction(nameof(Index));
        }

        private async Task<UserSettings> LoadAsync()
        {
            var settings = await _context.UserSettings.FirstOrDefaultAsync();

            if (settings != null)
                return settings;

            await _setup.EnsureSetUpAsync(_currentUser.RequiredUserId);

            return await _context.UserSettings.FirstAsync();
        }

        private async Task<SettingsViewModel> FillAsync(SettingsViewModel model)
        {
            model.Accounts = await _context.Accounts
                .AsNoTracking()
                .Where(a => !a.IsArchived)
                .OrderBy(a => a.Name)
                .ToListAsync();

            model.EmailAvailable = _email.IsEnabled;

            return model;
        }
    }
}
