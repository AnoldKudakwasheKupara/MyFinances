using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MyFinances.Areas.Identity.Pages.Account;

/// <summary>
/// Signing in through another provider is not set up, and this
/// page would make a new account for anyone who came through one.
///
/// The page is kept, answering not found, because removing it would
/// bring back the built-in one in its place.
/// </summary>
public class ExternalLoginModel : PageModel
{
    public IActionResult OnGet() => NotFound();

    public IActionResult OnPost() => NotFound();
}
