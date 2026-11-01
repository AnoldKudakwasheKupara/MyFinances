using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MyFinances.Security
{
    /// <summary>
    /// Limits on the pages anyone can reach without signing in.
    ///
    /// A household may share one internet address, so the limit per
    /// address on signing in is generous: it is there to stop a flood
    /// of guesses, while the lockout after five wrong passwords
    /// protects each account. Sign-up is limited too, so nobody can
    /// mass-create accounts.
    ///
    /// The reset and confirmation emails are also counted per email
    /// address, so nobody can fill somebody else's inbox with them.
    /// </summary>
    public static class SignInLimits
    {
        public const string LoginPath = "/Identity/Account/Login";
        public const string ForgotPasswordPath = "/Identity/Account/ForgotPassword";

        private const string EmailItem = "SignInLimits.Email";

        private sealed record Rule(int Permits, TimeSpan Window);

        private static readonly Dictionary<string, Rule> ByAddress =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [LoginPath] = new(60, TimeSpan.FromMinutes(5)),
                ["/Identity/Account/LoginWith2fa"] = new(60, TimeSpan.FromMinutes(5)),
                ["/Identity/Account/LoginWithRecoveryCode"] = new(60, TimeSpan.FromMinutes(5)),
                [ForgotPasswordPath] = new(20, TimeSpan.FromHours(1)),
                ["/Identity/Account/Register"] = new(10, TimeSpan.FromHours(1)),
                // Starting a sign-in with Google and the like, and opening
                // an account through one.
                ["/Identity/Account/ExternalLogin"] = new(30, TimeSpan.FromHours(1)),
                ["/Identity/Account/ResendEmailConfirmation"] = new(20, TimeSpan.FromHours(1)),
                ["/Identity/Account/ResetPassword"] = new(20, TimeSpan.FromMinutes(15)),
            };

        private static readonly Dictionary<string, Rule> ByEmail =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [ForgotPasswordPath] = new(3, TimeSpan.FromHours(1)),
                ["/Identity/Account/ResendEmailConfirmation"] = new(3, TimeSpan.FromHours(1))
            };

        public static void Configure(RateLimiterOptions options)
        {
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    Partition(context, ByAddress, Address(context))),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    Partition(context, ByEmail, context.Items[EmailItem] as string)));

            options.OnRejected = (rejected, _) =>
            {
                var context = rejected.HttpContext;
                var path = context.Request.Path.Value ?? string.Empty;

                if (path.Equals(LoginPath, StringComparison.OrdinalIgnoreCase) ||
                    path.Equals(ForgotPasswordPath, StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.Redirect($"{path}?limited=1");
                    return ValueTask.CompletedTask;
                }

                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.ContentType = "text/plain; charset=utf-8";

                return new ValueTask(context.Response.WriteAsync(
                    "Too many attempts. Wait a while and try again."));
            };
        }

        /// <summary>
        /// Reads the email address off a reset or confirmation request
        /// before the limits are checked, since the limiter cannot wait
        /// on the form itself. The form is kept, so the page reads the
        /// same one afterwards.
        /// </summary>
        public static async Task ReadEmailAsync(HttpContext context, Func<Task> next)
        {
            var path = context.Request.Path.Value;

            if (HttpMethods.IsPost(context.Request.Method) &&
                path != null &&
                ByEmail.ContainsKey(path) &&
                context.Request.HasFormContentType)
            {
                var form = await context.Request.ReadFormAsync();

                var email = form["Input.Email"].ToString().Trim();

                if (email.Length > 0)
                    context.Items[EmailItem] = email.ToUpperInvariant();
            }

            await next();
        }

        private static RateLimitPartition<string> Partition(
            HttpContext context,
            Dictionary<string, Rule> rules,
            string? who)
        {
            var path = context.Request.Path.Value;

            if (!HttpMethods.IsPost(context.Request.Method) ||
                path == null ||
                who == null ||
                !rules.TryGetValue(path, out var rule))
            {
                return RateLimitPartition.GetNoLimiter(string.Empty);
            }

            return RateLimitPartition.GetFixedWindowLimiter(
                $"{path.ToUpperInvariant()}|{who}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rule.Permits,
                    Window = rule.Window,
                    QueueLimit = 0
                });
        }

        private static string Address(HttpContext context) =>
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
