namespace MyFinances.Services
{
    /// <summary>
    /// The app's own idea of "now" and "today", in Harare time whatever
    /// zone the server runs in. A shared Windows host cannot be told its
    /// zone, and a day boundary in the wrong zone files late-evening
    /// entries under tomorrow.
    /// </summary>
    public static class Clock
    {
        public static readonly TimeZoneInfo Zone =
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Harare");

        public static DateTime Now =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

        public static DateOnly Today => DateOnly.FromDateTime(Now);

        public static DateTime ToLocal(DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);
    }
}
