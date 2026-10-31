using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;

namespace MyFinances.Services
{
    public static class DateFormats
    {
        public static readonly IReadOnlyList<string> All =
        [
            "dd MMM yyyy",
            "dd/MM/yyyy",
            "MM/dd/yyyy",
            "yyyy-MM-dd"
        ];
    }

    /// <summary>
    /// The signed-in person's settings, read once per request, with the
    /// helpers views use to write dates and amounts their way.
    /// </summary>
    public class UserPreferences
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUser _currentUser;

        private UserSettings? _settings;

        public UserPreferences(ApplicationDbContext context, CurrentUser currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        public async Task<UserSettings> GetAsync()
        {
            if (_settings != null)
                return _settings;

            if (_currentUser.UserId == null)
                return _settings = new UserSettings();

            _settings = await _context.UserSettings
                .AsNoTracking()
                .FirstOrDefaultAsync();

            return _settings ??= new UserSettings { UserId = _currentUser.UserId.Value };
        }

        public UserSettings Current =>
            _settings ?? throw new InvalidOperationException(
                "Preferences are read with GetAsync before they are used.");

        public string Date(DateOnly date) =>
            date.ToString((_settings ?? new UserSettings()).DateFormat, CultureInfo.InvariantCulture);

        public string Date(DateOnly? date, string none = "—") =>
            date == null ? none : Date(date.Value);

        public string Date(DateTime utc) =>
            Date(DateOnly.FromDateTime(utc.ToLocalTime()));

        public static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    }
}
