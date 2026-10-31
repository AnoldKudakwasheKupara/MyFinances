using Microsoft.AspNetCore.Identity.UI.Services;

namespace MyFinances.Services
{
    /// <summary>
    /// Lets the built-in account pages (changing email, resending a
    /// confirmation) send through the same mail setup as everything else.
    /// </summary>
    public class IdentityEmailSender : IEmailSender
    {
        private readonly EmailService _email;

        public IdentityEmailSender(EmailService email)
        {
            _email = email;
        }

        public Task SendEmailAsync(string email, string subject, string htmlMessage) =>
            _email.SendAsync(new OutgoingEmail(
                email,
                null,
                subject,
                subject,
                HtmlMessage: htmlMessage));
    }
}
