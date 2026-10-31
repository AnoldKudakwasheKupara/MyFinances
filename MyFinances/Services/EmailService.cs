using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MyFinances.Services
{
    public class EmailService
    {
        private readonly EmailOptions _options;
        private readonly IHttpClientFactory _http;
        private readonly ILogger<EmailService> _logger;

        private readonly SemaphoreSlim _tokenGate = new(1, 1);

        private string? _graphToken;

        private DateTimeOffset _graphTokenExpires;

        public EmailService(
            IOptions<EmailOptions> options,
            IHttpClientFactory http,
            ILogger<EmailService> logger)
        {
            _options = options.Value;
            _http = http;
            _logger = logger;
        }

        public bool IsEnabled => _options.Enabled;

        /// <summary>
        /// The address links in emails point at, when one is configured.
        /// Otherwise callers use the address the request came in on.
        /// </summary>
        public string? BaseUrl => _options.PortalBaseUrl;

        public async Task<bool> SendAsync(
            OutgoingEmail notification,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled)
                return false;

            if (string.IsNullOrWhiteSpace(notification.EmailAddress))
            {
                _logger.LogDebug(
                    "Skipping notification email with no recipient: {Subject}",
                    notification.Title);

                return false;
            }

            if (string.IsNullOrWhiteSpace(_options.FromAddress))
            {
                _logger.LogWarning(
                    "Email is enabled but Email:FromAddress is not set, " +
                    "so nothing can be sent.");

                return false;
            }

            if (_options.Delivery == EmailDelivery.Graph &&
                (string.IsNullOrWhiteSpace(_options.TenantId) ||
                 string.IsNullOrWhiteSpace(_options.ClientId) ||
                 string.IsNullOrWhiteSpace(_options.ClientSecret)))
            {
                _logger.LogWarning(
                    "Email is set to go through Microsoft Graph but " +
                    "Email:TenantId, Email:ClientId or Email:ClientSecret " +
                    "is not set, so nothing can be sent.");

                return false;
            }

            try
            {
                var message = Build(notification);

                switch (_options.Delivery)
                {
                    case EmailDelivery.File:
                        await WriteToPickupAsync(message, cancellationToken);
                        break;

                    case EmailDelivery.Graph:
                        await SendOverGraphAsync(message, cancellationToken);
                        break;

                    default:
                        await SendOverSmtpAsync(message, cancellationToken);
                        break;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Could not send the notification email to {Recipient}: " +
                    "{Subject}",
                    notification.EmailAddress,
                    notification.Title);

                return false;
            }
        }

        private MimeMessage Build(OutgoingEmail email)
        {
            var message = new MimeMessage();

            message.From.Add(
                new MailboxAddress(_options.FromName, _options.FromAddress));

            message.To.Add(new MailboxAddress(
                email.DisplayName ?? string.Empty,
                email.EmailAddress));

            message.Subject = email.Title;

            var body = new BodyBuilder
            {
                HtmlBody = email.ToHtml(),
                TextBody = email.ToText()
            };

            message.Body = body.ToMessageBody();

            return message;
        }

        private async Task SendOverSmtpAsync(
            MimeMessage message,
            CancellationToken cancellationToken)
        {
            using var client = new SmtpClient();

            await client.ConnectAsync(
                _options.Host,
                _options.Port,
                _options.UseStartTls
                    ? SecureSocketOptions.StartTls
                    : SecureSocketOptions.Auto,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.UserName))
            {
                await client.AuthenticateAsync(
                    _options.UserName,
                    _options.Password ?? string.Empty,
                    cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);

            await client.DisconnectAsync(true, cancellationToken);
        }

        /// <summary>
        /// Hands the finished message to Microsoft Graph as it
        /// stands, so the body arrives
        /// exactly as they were built. Graph takes it base64-encoded,
        /// answers 202 when it has accepted it, and files a copy in
        /// the sending mailbox's Sent Items.
        /// </summary>
        private async Task SendOverGraphAsync(
            MimeMessage message,
            CancellationToken cancellationToken)
        {
            var token = await GraphTokenAsync(cancellationToken);

            using var mime = new MemoryStream();

            await message.WriteToAsync(mime, cancellationToken);

            var content = new StringContent(
                Convert.ToBase64String(mime.ToArray()));

            content.Headers.ContentType =
                new MediaTypeHeaderValue("text/plain");

            var url =
                $"{_options.GraphBase.TrimEnd('/')}/v1.0/users/" +
                $"{Uri.EscapeDataString(_options.FromAddress)}/sendMail";

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content
            };

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.CreateClient()
                .SendAsync(request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Accepted)
            {
                var detail = await response.Content
                    .ReadAsStringAsync(cancellationToken);

                throw new InvalidOperationException(
                    "Microsoft Graph did not accept the message from " +
                    $"{_options.FromAddress} ({(int)response.StatusCode}): " +
                    detail);
            }
        }

        /// <summary>
        /// A token for Graph, signed in as the app. Kept until five
        /// minutes before it runs out, so a morning of reminders
        /// shares one sign-in rather than taking one each.
        /// </summary>
        private async Task<string> GraphTokenAsync(
            CancellationToken cancellationToken)
        {
            if (_graphToken != null &&
                DateTimeOffset.UtcNow < _graphTokenExpires)
            {
                return _graphToken;
            }

            await _tokenGate.WaitAsync(cancellationToken);

            try
            {
                if (_graphToken != null &&
                    DateTimeOffset.UtcNow < _graphTokenExpires)
                {
                    return _graphToken;
                }

                var url =
                    $"{_options.GraphLoginBase.TrimEnd('/')}/" +
                    $"{Uri.EscapeDataString(_options.TenantId!)}" +
                    "/oauth2/v2.0/token";

                var form = new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["client_id"] = _options.ClientId!,
                        ["client_secret"] = _options.ClientSecret!,
                        ["scope"] =
                            $"{_options.GraphBase.TrimEnd('/')}/.default",
                        ["grant_type"] = "client_credentials"
                    });

                using var response = await _http.CreateClient()
                    .PostAsync(url, form, cancellationToken);

                var body = await response.Content
                    .ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        "Microsoft Entra ID did not sign MyFinances in " +
                        $"({(int)response.StatusCode}): {body}");
                }

                using var json = JsonDocument.Parse(body);

                _graphToken = json.RootElement
                    .GetProperty("access_token")
                    .GetString();

                var lifetime = json.RootElement
                    .GetProperty("expires_in")
                    .GetInt32();

                _graphTokenExpires = DateTimeOffset.UtcNow
                    .AddSeconds(Math.Max(60, lifetime - 300));

                return _graphToken!;
            }
            finally
            {
                _tokenGate.Release();
            }
        }

        private async Task WriteToPickupAsync(
            MimeMessage message,
            CancellationToken cancellationToken)
        {
            var directory = Path.IsPathRooted(_options.PickupDirectory)
                ? _options.PickupDirectory
                : Path.Combine(
                    AppContext.BaseDirectory,
                    _options.PickupDirectory);

            Directory.CreateDirectory(directory);

            var name =
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-" +
                $"{Guid.NewGuid():N}.eml";

            await using var stream = File.Create(Path.Combine(directory, name));

            await message.WriteToAsync(stream, cancellationToken);

            _logger.LogInformation(
                "Notification email written to {Path}",
                Path.Combine(directory, name));
        }
    }
}
