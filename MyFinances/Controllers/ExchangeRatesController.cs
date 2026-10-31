using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;
using MyFinances.Services;
using MyFinances.ViewModels;

namespace MyFinances.Controllers
{
    /// <summary>
    /// Rates are only ever typed in by the user. Adding one fills in
    /// transactions that had no rate on their date; it never rewrites a
    /// conversion that was already made.
    /// </summary>
    public class ExchangeRatesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrencyService _currency;
        private readonly BalanceService _balances;
        private readonly UserPreferences _preferences;

        public ExchangeRatesController(
            ApplicationDbContext context,
            CurrencyService currency,
            BalanceService balances,
            UserPreferences preferences)
        {
            _context = context;
            _currency = currency;
            _balances = balances;
            _preferences = preferences;
        }

        public async Task<IActionResult> Index() => View(await BuildAsync());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind(Prefix = "Form")] ExchangeRateFormViewModel form)
        {
            if (form.FromCurrency == form.ToCurrency)
                ModelState.AddModelError("Form.ToCurrency", "Pick two different currencies.");

            if (!ModelState.IsValid)
                return View(nameof(Index), await BuildAsync(form));

            var existing = await _context.ExchangeRates.FirstOrDefaultAsync(r =>
                r.FromCurrency == form.FromCurrency &&
                r.ToCurrency == form.ToCurrency &&
                r.EffectiveDate == form.EffectiveDate);

            if (existing != null)
            {
                existing.Rate = form.Rate;
            }
            else
            {
                _context.ExchangeRates.Add(new ExchangeRate
                {
                    FromCurrency = form.FromCurrency,
                    ToCurrency = form.ToCurrency,
                    Rate = form.Rate,
                    EffectiveDate = form.EffectiveDate
                });
            }

            await _context.SaveChangesAsync();

            var settings = await _preferences.GetAsync();
            var filled = await _currency.RefreshSnapshotsAsync(settings.ReportingCurrency, onlyMissing: true);

            TempData["Flash"] =
                $"1 {form.FromCurrency} = {form.Rate:0.########} {form.ToCurrency} from {form.EffectiveDate:d MMM yyyy} was saved." +
                (filled > 0 ? $" {filled} transaction(s) that had no rate are now converted." : string.Empty);

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var rate = await _context.ExchangeRates.FirstOrDefaultAsync(r => r.Id == id);

            if (rate == null)
                return NotFound();

            _context.ExchangeRates.Remove(rate);
            await _context.SaveChangesAsync();

            TempData["Flash"] =
                "The rate was removed. Transactions already converted with it keep their conversion.";

            return RedirectToAction(nameof(Index));
        }

        private async Task<ExchangeRatesViewModel> BuildAsync(ExchangeRateFormViewModel? form = null)
        {
            var settings = await _preferences.GetAsync();
            var rates = await _currency.LoadAsync();

            var held = (await _balances.BalancesAsync(includeArchived: false))
                .Select(b => b.Currency)
                .Distinct()
                .Where(c => rates.Find(c, settings.ReportingCurrency, UserPreferences.Today) == null)
                .ToList();

            return new ExchangeRatesViewModel
            {
                Rates = await _context.ExchangeRates
                    .AsNoTracking()
                    .OrderByDescending(r => r.EffectiveDate)
                    .ThenBy(r => r.FromCurrency)
                    .ThenBy(r => r.ToCurrency)
                    .ToListAsync(),
                Form = form ?? new ExchangeRateFormViewModel
                {
                    FromCurrency = settings.ReportingCurrency,
                    ToCurrency = settings.ReportingCurrency == Currency.ZWG ? Currency.USD : Currency.ZWG
                },
                ReportingCurrency = settings.ReportingCurrency,
                Unconverted = await _currency.UnconvertedCountAsync(settings.ReportingCurrency),
                MissingToday = held
            };
        }
    }
}
