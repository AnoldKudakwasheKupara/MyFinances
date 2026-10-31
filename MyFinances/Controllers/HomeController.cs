using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyFinances.Entities;
using MyFinances.Services;
using MyFinances.Services.Reporting;

namespace MyFinances.Controllers
{
    public class HomeController : Controller
    {
        private readonly DashboardService _dashboard;

        public HomeController(DashboardService dashboard)
        {
            _dashboard = dashboard;
        }

        public async Task<IActionResult> Index(DashboardPeriod? period = null, SeriesGrain view = SeriesGrain.Monthly) =>
            View(await _dashboard.BuildAsync(period, view));

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() => View();
    }
}
