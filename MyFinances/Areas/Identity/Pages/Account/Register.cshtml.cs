using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using MyFinances.Data;
using MyFinances.Entities;
using MyFinances.Services;

namespace MyFinances.Areas.Identity.Pages.Account;

/// <summary>
/// Anyone may open their own set of books. Each account sees only
/// its own records, and starts with the default categories in the
/// currency chosen here.
/// </summary>
public class RegisterModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserSetupService _setup;
    private readonly EmailService _email;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RegisterModel> _logger;

    public RegisterModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        UserSetupService setup,
        EmailService email,
        IConfiguration configuration,
        ILogger<RegisterModel> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _setup = setup;
        _email = email;
        _configuration = configuration;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public IList<Microsoft.AspNetCore.Authentication.AuthenticationScheme> ExternalLogins { get; set; } = [];

    public bool IsOpen => _configuration.GetValue("Registration:Enabled", true);

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

        [Required]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Use at least 8 characters.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "The two passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return LocalRedirect("~/");

        ReturnUrl = returnUrl;
        ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        ReturnUrl = returnUrl;
        ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

        if (!IsOpen)
            return NotFound();

        if (!ModelState.IsValid)
            return Page();

        var user = new ApplicationUser
        {
            UserName = Input.Email.Trim(),
            Email = Input.Email.Trim(),
            FullName = Input.FullName.Trim()
        };

        var result = await _userManager.CreateAsync(user, Input.Password);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            return Page();
        }

        _logger.LogInformation("User {UserId} opened an account.", user.Id);

        await _setup.EnsureSetUpAsync(user.Id, Input.ReportingCurrency);

        if (_userManager.Options.SignIn.RequireConfirmedAccount)
        {
            await SendConfirmationAsync(this, _userManager, _email, _logger, user);

            return RedirectToPage("RegisterConfirmation", new { email = user.Email });
        }

        await _signInManager.SignInAsync(user, isPersistent: false);

        return LocalRedirect(returnUrl);
    }

    /// <summary>
    /// Emails a new person the link that confirms their address. Also
    /// used when an account is opened through Google and the like.
    /// </summary>
    internal static async Task SendConfirmationAsync(
        PageModel page,
        UserManager<ApplicationUser> userManager,
        EmailService email,
        ILogger logger,
        ApplicationUser user)
    {
        var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

        var link = page.Url.Page(
            "/Account/ConfirmEmail",
            pageHandler: null,
            values: new { area = "Identity", userId = user.Id, code },
            protocol: page.Request.Scheme)!;

        var sent = await email.SendAsync(new OutgoingEmail(
            user.Email!,
            user.FullName,
            "Confirm your MyFinances account",
            "Thanks for signing up. Confirm your email address to start " +
            "using MyFinances. If you did not sign up, you can ignore this email.",
            link,
            "Confirm my email"));

        if (!sent)
        {
            logger.LogWarning(
                "User {UserId} signed up but the confirmation email could not be sent.",
                user.Id);
        }
    }
}
