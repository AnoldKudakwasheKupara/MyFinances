using Microsoft.AspNetCore.Mvc;
using MyFinances.Services;
using MyFinances.Services.Reporting;

namespace MyFinances.Controllers
{
    public class ReportsController : Controller
    {
        private readonly ReportService _reports;
        private readonly UserPreferences _preferences;

        public ReportsController(ReportService reports, UserPreferences preferences)
        {
            _reports = reports;
            _preferences = preferences;
        }

        public IActionResult Index() => View(ReportService.All);

        public async Task<IActionResult> Show(string id, DateOnly? from = null, DateOnly? to = null)
        {
            var definition = ReportService.Find(id);

            if (definition == null)
                return NotFound();

            return View(await _reports.BuildAsync(definition, await RangeAsync(from, to)));
        }

        public async Task<IActionResult> Export(string id, string format = "csv", DateOnly? from = null, DateOnly? to = null)
        {
            var definition = ReportService.Find(id);

            if (definition == null)
                return NotFound();

            var range = await RangeAsync(from, to);
            var report = await _reports.BuildAsync(definition, range);
            var name = $"{definition.Key}-{(definition.UsesRange ? range.Start.ToString("yyyy-MM-dd") + "-to-" : "")}{range.End:yyyy-MM-dd}";

            if (format == "pdf")
            {
                var owner = User.FindFirst(MyFinances.Data.ApplicationUserClaimsPrincipalFactory.FullNameClaim)?.Value ?? User.Identity?.Name ?? "";
                return File(ReportExport.Pdf(report, owner), "application/pdf", name + ".pdf");
            }

            return File(ReportExport.Csv(report), "text/csv", name + ".csv");
        }

        /// <summary>The dates asked for, or the current financial year to date.</summary>
        private async Task<DateRange> RangeAsync(DateOnly? from, DateOnly? to)
        {
            var settings = await _preferences.GetAsync();
            var today = UserPreferences.Today;
            var year = Periods.FinancialYear(today, settings.FinancialYearStartMonth);

            var start = from ?? year.Start;
            var end = to ?? today;

            if (end < start)
                (start, end) = (end, start);

            // Keep reports to ten years so a mistyped year cannot run away.
            if (end.DayNumber - start.DayNumber > 3660)
                start = end.AddYears(-10);

            return new DateRange(start, end);
        }
    }
}
