using System.Net;

namespace MyFinances.Services
{
    /// <summary>
    /// One plain email: a greeting, a few sentences and, when there is
    /// something to open, a single button. Kept deliberately simple so
    /// it reads the same in every mail client.
    /// </summary>
    public record OutgoingEmail(
        string EmailAddress,
        string? DisplayName,
        string Title,
        string Message,
        string? Link = null,
        string LinkText = "Open MyFinances",
        string? HtmlMessage = null)
    {
        public string ToText()
        {
            var greeting = string.IsNullOrWhiteSpace(DisplayName)
                ? "Hello,"
                : $"Hello {DisplayName},";

            var text = $"{greeting}\n\n{Message}\n";

            if (Link != null)
                text += $"\n{LinkText}: {Link}\n";

            return text + "\nMyFinances\n";
        }

        public string ToHtml()
        {
            var greeting = string.IsNullOrWhiteSpace(DisplayName)
                ? "Hello,"
                : $"Hello {WebUtility.HtmlEncode(DisplayName)},";

            var button = Link == null
                ? string.Empty
                : $"""
                   <p style="margin:28px 0">
                     <a href="{WebUtility.HtmlEncode(Link)}"
                        style="background:#0f766e;color:#ffffff;padding:12px 22px;border-radius:8px;text-decoration:none;font-weight:600;display:inline-block">
                       {WebUtility.HtmlEncode(LinkText)}
                     </a>
                   </p>
                   <p style="font-size:12px;color:#64748b">If the button does not work, paste this link into your browser:<br>{WebUtility.HtmlEncode(Link)}</p>
                   """;

            return $"""
                <!DOCTYPE html>
                <html><body style="margin:0;background:#f4f6f8;font-family:Segoe UI,Arial,sans-serif;color:#0f1f3d">
                  <div style="max-width:560px;margin:32px auto;background:#ffffff;border:1px solid #e2e8f0;border-radius:12px;padding:32px">
                    <p style="font-weight:700;color:#0f766e;margin:0 0 24px">MyFinances</p>
                    <h1 style="font-size:20px;margin:0 0 16px">{WebUtility.HtmlEncode(Title)}</h1>
                    <p>{greeting}</p>
                    <p style="line-height:1.6">{HtmlMessage ?? WebUtility.HtmlEncode(Message)}</p>
                    {button}
                  </div>
                </body></html>
                """;
        }
    }
}
