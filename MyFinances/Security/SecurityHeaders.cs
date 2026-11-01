namespace MyFinances.Security
{
    /// <summary>
    /// Headers that tell the browser to keep the app to itself: no
    /// framing by other sites, no guessing file types, no referrer
    /// leaking financial page addresses, and scripts, styles and fonts
    /// only from the app.
    /// </summary>
    public static class SecurityHeaders
    {
        // Inline script is limited to the theme switch in the layout and
        // a few onclick handlers; everything else is served from /js.
        // Forms may also lead on to the sign-in pages of the outside
        // providers in use, which the browser checks on the redirect.
        private static string? _policy;

        private static string Policy => _policy ??=
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; " +
            "font-src 'self'; " +
            "connect-src 'self'; " +
            "object-src 'none'; " +
            "base-uri 'self'; " +
            "form-action " + string.Join(" ", ["'self'", .. ExternalProviders.FormTargets]) + "; " +
            "frame-ancestors 'none'";

        public static Task AddAsync(HttpContext context, Func<Task> next)
        {
            var headers = context.Response.Headers;

            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            headers["Content-Security-Policy"] = Policy;

            return next();
        }
    }
}
