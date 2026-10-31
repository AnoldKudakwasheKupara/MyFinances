using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MyFinances.Data;

namespace MyFinances.Areas.Identity.Pages.Account.Manage;

public class IndexModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public IndexModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    public string? Email { get; set; }

    public bool EmailConfirmed { get; set; }

    public bool TwoFactorEnabled { get; set; }

    public DateTime MemberSince { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required]
        [StringLength(100)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Phone]
        [Display(Name = "Phone number")]
        public string? PhoneNumber { get; set; }
    }

    private void Load(ApplicationUser user)
    {
        Email = user.Email;
        EmailConfirmed = user.EmailConfirmed;
        TwoFactorEnabled = user.TwoFactorEnabled;
        MemberSince = user.CreatedAt;

        Input = new InputModel
        {
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber
        };
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);

        if (user == null)
            return NotFound();

        Load(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.GetUserAsync(User);

        if (user == null)
            return NotFound();

        if (!ModelState.IsValid)
        {
            Email = user.Email;
            EmailConfirmed = user.EmailConfirmed;
            TwoFactorEnabled = user.TwoFactorEnabled;
            MemberSince = user.CreatedAt;
            return Page();
        }

        user.FullName = Input.FullName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(Input.PhoneNumber)
            ? null
            : Input.PhoneNumber.Trim();

        var result = await _userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            StatusMessage = "Error: your profile could not be saved.";
            return RedirectToPage();
        }

        // The name shown in the top bar comes from the sign-in cookie.
        await _signInManager.RefreshSignInAsync(user);

        StatusMessage = "Your profile has been updated.";
        return RedirectToPage();
    }
}
