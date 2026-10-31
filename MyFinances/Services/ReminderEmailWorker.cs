using Microsoft.EntityFrameworkCore;
using MyFinances.Data;

namespace MyFinances.Services
{
    /// <summary>
    /// Once a day, emails each person who asked for it a short list of
    /// what needs their attention. Does nothing until email is set up.
    /// Each person is handled in a scope of their own, acting as them,
    /// so the same ownership rules apply as in the app.
    /// </summary>
    public class ReminderEmailWorker : BackgroundService
    {
        private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(1);

        /// <summary>Not before this hour, local time, so the email arrives in the morning.</summary>
        private const int SendFromHour = 7;

        private readonly IServiceScopeFactory _scopes;
        private readonly EmailService _email;
        private readonly ILogger<ReminderEmailWorker> _logger;

        public ReminderEmailWorker(IServiceScopeFactory scopes, EmailService email, ILogger<ReminderEmailWorker> logger)
        {
            _scopes = scopes;
            _email = email;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_email.IsEnabled)
                return;

            using var timer = new PeriodicTimer(CheckEvery);

            do
            {
                try
                {
                    if (DateTime.Now.Hour >= SendFromHour)
                        await SendDueAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Reminder emails could not be sent this time; they will be tried again.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task SendDueAsync(CancellationToken cancellationToken)
        {
            var today = UserPreferences.Today;

            List<int> userIds;

            using (var scope = _scopes.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                userIds = await context.UserSettings
                    .IgnoreQueryFilters()
                    .Where(s => s.EmailReminders && (s.LastReminderEmailOn == null || s.LastReminderEmailOn < today))
                    .Select(s => s.UserId)
                    .ToListAsync(cancellationToken);
            }

            foreach (var userId in userIds)
            {
                using var scope = _scopes.CreateScope();

                var services = scope.ServiceProvider;
                using var acting = services.GetRequiredService<CurrentUser>().ActAs(userId);

                var context = services.GetRequiredService<ApplicationDbContext>();
                var reminders = (await services.GetRequiredService<ReminderService>().ListAsync())
                    .Where(r => r.Level != ReminderLevel.Info)
                    .ToList();

                var user = await context.Users.AsNoTracking().FirstAsync(u => u.Id == userId, cancellationToken);
                var settings = await context.UserSettings.FirstAsync(cancellationToken);

                // Email only goes to an address its owner has confirmed.
                if (reminders.Any() && user.EmailConfirmed)
                {
                    var lines = string.Join(" ", reminders.Select(r => $"• {r.Title}: {r.Detail}"));
                    var html = "<ul>" + string.Join("", reminders.Select(r =>
                        $"<li><strong>{System.Net.WebUtility.HtmlEncode(r.Title)}</strong> — {System.Net.WebUtility.HtmlEncode(r.Detail)}</li>")) + "</ul>";

                    var sent = await _email.SendAsync(new OutgoingEmail(
                        user.Email!,
                        user.FullName,
                        reminders.Count == 1 ? "1 thing needs your attention" : $"{reminders.Count} things need your attention",
                        lines,
                        string.IsNullOrWhiteSpace(_email.BaseUrl) ? null : _email.BaseUrl.TrimEnd('/') + "/Notifications",
                        "Open MyFinances",
                        html), cancellationToken);

                    if (!sent)
                        continue;
                }

                settings.LastReminderEmailOn = today;
                await context.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
