using Microsoft.AspNetCore.Mvc;
using MyFinances.Services;

namespace MyFinances.Controllers
{
    public class NotificationsController : Controller
    {
        private readonly ReminderService _reminders;

        public NotificationsController(ReminderService reminders)
        {
            _reminders = reminders;
        }

        public async Task<IActionResult> Index() => View(await _reminders.ListAsync());
    }
}
