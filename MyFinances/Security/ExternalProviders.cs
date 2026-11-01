using AspNet.Security.OAuth.Twitter;
using Microsoft.AspNetCore.Authentication;

namespace MyFinances.Security
{
    /// <summary>
    /// Signing in with Google, GitHub, Facebook or X.
    ///
    /// Each one is switched on by its keys alone: set
    /// Authentication:{Name}:ClientId and ClientSecret (on Render,
    /// Authentication__Google__ClientId and so on) and its button
    /// appears; leave them out and it is not offered at all, so a half
    /// set-up provider never shows a button that fails.
    /// </summary>
    public static class ExternalProviders
    {
        /// <summary>
        /// The sign-in pages of the providers in use. The sign-in form
        /// posts to the app, which sends the browser on to one of
        /// these, and the content security policy has to allow it.
        /// </summary>
        public static IReadOnlyList<string> FormTargets { get; private set; } = [];

        public static void Add(AuthenticationBuilder authentication, IConfiguration configuration)
        {
            var targets = new List<string>();

            if (Keys(configuration, "Google") is { } google)
            {
                authentication.AddGoogle(options =>
                {
                    options.ClientId = google.Id;
                    options.ClientSecret = google.Secret;
                });

                targets.Add("https://accounts.google.com");
            }

            if (Keys(configuration, "GitHub") is { } gitHub)
            {
                authentication.AddGitHub(options =>
                {
                    options.ClientId = gitHub.Id;
                    options.ClientSecret = gitHub.Secret;

                    // The profile only carries a public address, if any;
                    // with this the provider looks up the verified primary.
                    options.Scope.Add("user:email");
                });

                targets.Add("https://github.com");
            }

            if (Keys(configuration, "Facebook") is { } facebook)
            {
                authentication.AddFacebook(options =>
                {
                    options.AppId = facebook.Id;
                    options.AppSecret = facebook.Secret;
                });

                targets.Add("https://www.facebook.com");
                targets.Add("https://m.facebook.com");
            }

            // X does not hand over an email address without extra
            // approval on its side, so people coming from X type theirs.
            if (Keys(configuration, "X") is { } x)
            {
                authentication.AddTwitter(
                    TwitterAuthenticationDefaults.AuthenticationScheme,
                    "X",
                    options =>
                    {
                        options.ClientId = x.Id;
                        options.ClientSecret = x.Secret;
                    });

                targets.Add("https://x.com");
                targets.Add("https://twitter.com");
            }

            FormTargets = targets;
        }

        /// <summary>The Bootstrap icon for a provider's button.</summary>
        public static string Icon(string scheme) => scheme switch
        {
            "Google" => "google",
            "GitHub" => "github",
            "Facebook" => "facebook",
            TwitterAuthenticationDefaults.AuthenticationScheme => "twitter-x",
            _ => "box-arrow-in-right"
        };

        private static (string Id, string Secret)? Keys(IConfiguration configuration, string name)
        {
            var section = configuration.GetSection($"Authentication:{name}");

            var id = section["ClientId"];
            var secret = section["ClientSecret"];

            return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret)
                ? null
                : (id.Trim(), secret.Trim());
        }
    }
}
