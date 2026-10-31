using Microsoft.AspNetCore.Mvc;
using MyFinances.Services;
using MyFinances.ViewModels;

namespace MyFinances.Controllers
{
    public class AccountsController : Controller
    {
        private readonly AccountService _accounts;

        public AccountsController(AccountService accounts)
        {
            _accounts = accounts;
        }

        public async Task<IActionResult> Index(bool archived = false) =>
            View(await _accounts.IndexAsync(archived));

        public async Task<IActionResult> Details(int id)
        {
            var model = await _accounts.DetailsAsync(id);

            return model == null ? NotFound() : View(model);
        }

        public IActionResult Create() => View("Form", new AccountFormViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AccountFormViewModel form)
        {
            if (!ModelState.IsValid)
                return View("Form", form);

            var id = await _accounts.CreateAsync(form);

            TempData["Flash"] = $"{form.Name.Trim()} was added.";

            return RedirectToAction(nameof(Details), new { id });
        }

        public async Task<IActionResult> Edit(int id)
        {
            var form = await _accounts.FormAsync(id);

            return form == null ? NotFound() : View("Form", form);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, AccountFormViewModel form)
        {
            form.Id = id;

            if (!ModelState.IsValid)
            {
                form.CurrencyLocked = await _accounts.HasLedgerAsync(id);
                return View("Form", form);
            }

            var result = await _accounts.UpdateAsync(id, form);

            if (!result.Found)
                return NotFound();

            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Message!);
                form.CurrencyLocked = await _accounts.HasLedgerAsync(id);
                return View("Form", form);
            }

            TempData["Flash"] = result.Message;

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Archive(int id, bool archived = true)
        {
            var result = await _accounts.SetArchivedAsync(id, archived);

            if (!result.Found)
                return NotFound();

            TempData["Flash"] = result.Message;

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _accounts.DeleteAsync(id);

            if (!result.Found)
                return NotFound();

            if (!result.Succeeded)
            {
                TempData["FlashError"] = result.Message;
                return RedirectToAction(nameof(Details), new { id });
            }

            TempData["Flash"] = result.Message;

            return RedirectToAction(nameof(Index));
        }
    }
}
