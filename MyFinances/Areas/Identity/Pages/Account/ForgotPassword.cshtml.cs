using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using MyFinances.Data;
using MyFinances.Services;

namespace MyFinances.Areas.Identity.Pages.Account;

public class ForgotPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly EmailService _email;
    private readonly ILogger<ForgotPasswordModel> _logger;

    public ForgotPasswordModel(
        UserManager<ApplicationUser> userManager,
        EmailService email,
        ILogger<ForgotPasswordModel> logger)
    {
        _userManager = userManager;
        _email = email;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = default!;

    public bool EmailEnabled => _email.IsEnabled;

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = default!;
    }

    /// <summary>
    /// The page says the same thing whether or not the address belongs
    /// to anybody, so it cannot be used to find out who has an account.
    /// </summary>
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var user = await _userManager.FindByEmailAsync(Input.Email);

        if (user == null ||
            (_userManager.Options.SignIn.RequireConfirmedAccount &&
             !await _userManager.IsEmailConfirmedAsync(user)))
        {
            return RedirectToPage("./ForgotPasswordConfirmation");
        }

        var code = await _userManager.GeneratePasswordResetTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

        var link = Url.Page(
            "/Account/ResetPassword",
            pageHandler: null,
            values: new { area = "Identity", code })!;

        link = string.IsNullOrWhiteSpace(_email.BaseUrl)
            ? $"{Request.Scheme}://{Request.Host}{link}"
            : $"{_email.BaseUrl.TrimEnd('/')}{link}";

        var sent = await _email.SendAsync(new OutgoingEmail(
            user.Email!,
            user.FullName,
            "Reset your MyFinances password",
            "Somebody asked to reset the password for your MyFinances " +
            "account. If it was you, choose a new password using the link " +
            "below. The link works once. If you did not ask, ignore this " +
            "email and your password stays as it is.",
            link,
            "Choose a new password"));

        if (!sent)
        {
            _logger.LogWarning(
                "A password reset was asked for user {UserId} but the " +
                "email could not be sent.",
                user.Id);
        }

        return RedirectToPage("./ForgotPasswordConfirmation");
    }
}
