namespace MyFinances.Services
{
    /// <summary>
    /// How long a signed-in session may sit untouched before it is
    /// closed.
    ///
    /// The browser does the counting, because only it knows whether
    /// someone is typing or scrolling: after <see cref="Idle"/> with
    /// no use it signs the person out, with a minute's warning first.
    /// The sign-in cookie lasts a little longer than that, so a tab
    /// that was simply closed is dead on the server soon after too.
    /// </summary>
    public static class SessionTimeout
    {
        public static readonly TimeSpan Idle = TimeSpan.FromMinutes(15);

        public static readonly TimeSpan Warning = TimeSpan.FromMinutes(1);

        /// <summary>
        /// How often, at most, activity in the page tells the server
        /// the person is still there.
        /// </summary>
        public static readonly TimeSpan KeepAliveEvery = TimeSpan.FromMinutes(1);

        public static TimeSpan CookieLifetime => Idle + KeepAliveEvery;
    }
}
