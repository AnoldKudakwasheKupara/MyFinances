using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MyFinances.Data;
using MyFinances.Entities;
using MyFinances.Services;

namespace MyFinances.Areas.Identity.Pages.Account;

/// <summary>
/// Signing in with Google, GitHub, Facebook or X.
///
/// Someone who has linked the provider to their account is signed
/// straight in (two-factor still applies). Someone new is asked for
/// their name, email and main currency, the same as on sign-up, and
/// gets an account with no password.
///
/// An address that already has an account is never joined to the
/// provider here: whoever opened that account might not be the person
/// now signing in. Its owner signs in with their password and links
/// the provider under My profile.
/// </summary>
public class ExternalLoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly UserSetupService _setup;
    private readonly EmailService _email;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ExternalLoginModel> _logger;

    public ExternalLoginModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        UserSetupService setup,
        EmailService email,
        IConfiguration configuration,
        ILogger<ExternalLoginModel> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _setup = setup;
        _email = email;
        _configuration = configuration;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string ProviderDisplayName { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    private bool IsOpen => _configuration.GetValue("Registration:Enabled", true);

    public class InputModel
    {
        [Required]
        [StringLength(100)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Main currency")]
        public Currency ReportingCurrency { get; set; } = Currency.USD;
    }

    public IActionResult OnGet() => RedirectToPage("./Login");

    public async Task<IActionResult> OnPostAsync(string provider, string? returnUrl = null)
    {
        var schemes = await _signInManager.GetExternalAuthenticationSchemesAsync();

        if (!schemes.Any(s => s.Name == provider))
            return RedirectToPage("./Login");

        var redirectUrl = Url.Page("./ExternalLogin", pageHandler: "Callback", values: new { returnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);

        return new ChallengeResult(provider, properties);
    }

    public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl = null, string? remoteError = null)
    {
        returnUrl = SafeReturnUrl(returnUrl);

        if (remoteError != null)
            return BackToLogin("The sign-in was cancelled or refused. Try again, or use your email and password.", returnUrl);

        var info = await _signInManager.GetExternalLoginInfoAsync();

        if (info == null)
            return BackToLogin("The sign-in took too long or was interrupted. Try again.", returnUrl);

        var result = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: false);

        if (result.Succeeded)
        {
            _logger.LogInformation("User signed in with {Provider}.", info.LoginProvider);
            return LocalRedirect(returnUrl);
        }

        if (result.RequiresTwoFactor)
            return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl });

        if (result.IsLockedOut)
            return RedirectToPage("./Lockout");

        if (result.IsNotAllowed)
            return BackToLogin("Confirm your email address first. The link is in the email we sent when you signed up.", returnUrl);

        if (!IsOpen)
            return BackToLogin($"No account is linked to that {info.ProviderDisplayName} login, and new accounts are not being opened.", returnUrl);

        ReturnUrl = returnUrl;
        ProviderDisplayName = info.ProviderDisplayName ?? info.LoginProvider;

        Input = new InputModel
        {
            FullName = FullName(info.Principal),
            Email = info.Principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty
        };

        return Page();
    }

    public async Task<IActionResult> OnPostConfirmationAsync(string? returnUrl = null)
    {
        returnUrl = SafeReturnUrl(returnUrl);
        ReturnUrl = returnUrl;

        if (!IsOpen)
            return NotFound();

        var info = await _signInManager.GetExternalLoginInfoAsync();

        if (info == null)
            return BackToLogin("The sign-in took too long or was interrupted. Try again.", returnUrl);

        ProviderDisplayName = info.ProviderDisplayName ?? info.LoginProvider;

        if (!ModelState.IsValid)
            return Page();

        var email = Input.Email.Trim();

        if (await _userManager.FindByEmailAsync(email) != null)
        {
            ModelState.AddModelError(string.Empty,
                $"An account already uses {email}. Sign in with its password, then link " +
                $"{ProviderDisplayName} under My profile → External logins.");

            return Page();
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = Input.FullName.Trim()
        };

        var created = await _userManager.CreateAsync(user);

        if (created.Succeeded)
            created = await _userManager.AddLoginAsync(user, info);

        if (!created.Succeeded)
        {
            foreach (var error in created.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            if (user.Id != 0)
                await _userManager.DeleteAsync(user);

            return Page();
        }

        _logger.LogInformation("User {UserId} opened an account with {Provider}.", user.Id, info.LoginProvider);

        await _setup.EnsureSetUpAsync(user.Id, Input.ReportingCurrency);

        if (_userManager.Options.SignIn.RequireConfirmedAccount)
        {
            await RegisterModel.SendConfirmationAsync(this, _userManager, _email, _logger, user);

            return RedirectToPage("./RegisterConfirmation", new { email = user.Email });
        }

        await _signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);

        return LocalRedirect(returnUrl);
    }

    private string SafeReturnUrl(string? returnUrl) =>
        returnUrl != null && Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Content("~/");

    private IActionResult BackToLogin(string message, string returnUrl)
    {
        ErrorMessage = message;
        return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
    }

    /// <summary>
    /// The person's own name where the provider gives one: GitHub and X
    /// put the handle in the usual name claim and the real name apart.
    /// </summary>
    private static string FullName(ClaimsPrincipal principal) =>
        principal.FindFirstValue("urn:github:name")
        ?? principal.FindFirstValue("urn:twitter:name")
        ?? principal.FindFirstValue(ClaimTypes.Name)
        ?? string.Empty;
}
