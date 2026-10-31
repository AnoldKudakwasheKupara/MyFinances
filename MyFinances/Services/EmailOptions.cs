namespace MyFinances.Services
{
    public enum EmailDelivery
    {
        Smtp,

        File,

        /// <summary>
        /// Microsoft 365 through Microsoft Graph, signed in as an
        /// app rather than with a mailbox password, which Microsoft
        /// is switching off for SMTP from the end of 2026.
        /// </summary>
        Graph
    }

    public class EmailOptions
    {
        public const string Section = "Email";

        public bool Enabled { get; set; }

        public EmailDelivery Delivery { get; set; } = EmailDelivery.Smtp;

        public string PickupDirectory { get; set; } = "App_Data/mail";

        public string Host { get; set; } = string.Empty;

        public int Port { get; set; } = 587;

        public bool UseStartTls { get; set; } = true;

        public string? UserName { get; set; }

        public string? Password { get; set; }

        public string FromAddress { get; set; } = string.Empty;

        public string FromName { get; set; } = "MyFinances";

        public string? PortalBaseUrl { get; set; }

        /// <summary>
        /// The Microsoft 365 directory (tenant) the app is registered
        /// in. Graph delivery only.
        /// </summary>
        public string? TenantId { get; set; }

        /// <summary>The app registration's application (client) ID.</summary>
        public string? ClientId { get; set; }

        /// <summary>
        /// The app registration's client secret. Kept in the host's
        /// settings, never in the repository.
        /// </summary>
        public string? ClientSecret { get; set; }

        /// <summary>
        /// Where tokens and Graph are reached. Microsoft's public
        /// cloud unless the organisation is in a national one.
        /// </summary>
        public string GraphLoginBase { get; set; } =
            "https://login.microsoftonline.com";

        public string GraphBase { get; set; } =
            "https://graph.microsoft.com";
    }
}
