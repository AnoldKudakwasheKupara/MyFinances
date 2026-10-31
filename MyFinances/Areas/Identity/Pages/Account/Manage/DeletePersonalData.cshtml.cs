using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MyFinances.Data;
using MyFinances.Services;

namespace MyFinances.Areas.Identity.Pages.Account.Manage;

/// <summary>
/// Closes the account. The ledger is cleared first; deleting the
/// sign-in then removes every other financial record (the database
/// cascades from the user). Receipt files are on disk, so they are
/// removed here as well.
/// </summary>
public class DeletePersonalDataModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ApplicationDbContext _context;
    private readonly UserSetupService _setup;
    private readonly ReceiptStorage _receipts;
    private readonly ILogger<DeletePersonalDataModel> _logger;

    public DeletePersonalDataModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ApplicationDbContext context,
        UserSetupService setup,
        ReceiptStorage receipts,
        ILogger<DeletePersonalDataModel> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _context = context;
        _setup = setup;
        _receipts = receipts;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool RequirePassword { get; set; }

    public class InputModel
    {
        [DataType(DataType.Password)]
        public string? Password { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);

        if (user == null)
            return NotFound();

        RequirePassword = await _userManager.HasPasswordAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.GetUserAsync(User);

        if (user == null)
            return NotFound();

        RequirePassword = await _userManager.HasPasswordAsync(user);

        if (RequirePassword && !await _userManager.CheckPasswordAsync(user, Input.Password ?? string.Empty))
        {
            ModelState.AddModelError(string.Empty, "That password is not right.");
            return Page();
        }

        var userId = user.Id;

        await _context.InTransactionAsync(async () =>
        {
            await _setup.ClearLedgerAsync(userId);

            var result = await _userManager.DeleteAsync(user);

            if (!result.Succeeded)
                throw new InvalidOperationException("The account could not be closed.");

            return result;
        });

        _receipts.DeleteAll(userId);

        await _signInManager.SignOutAsync();

        _logger.LogInformation("User {UserId} closed their account.", userId);

        return Redirect("~/");
    }
}
