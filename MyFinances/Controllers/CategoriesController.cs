using Microsoft.AspNetCore.Mvc;
using MyFinances.Entities;
using MyFinances.Services;
using MyFinances.ViewModels;

namespace MyFinances.Controllers
{
    public class CategoriesController : Controller
    {
        private readonly CategoryService _categories;

        public CategoriesController(CategoryService categories)
        {
            _categories = categories;
        }

        public async Task<IActionResult> Index(int? edit = null) =>
            View(await BuildAsync(edit));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save([Bind(Prefix = "Form")] CategoryFormViewModel form)
        {
            if (form.Icon != null && !CategoriesViewModel.Icons.Contains(form.Icon))
                ModelState.AddModelError("Form.Icon", "Pick an icon from the list.");

            if (!ModelState.IsValid)
                return View(nameof(Index), await BuildAsync(null, form));

            var result = await _categories.SaveAsync(form.Id, form);

            if (!result.Found)
                return NotFound();

            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Message!);
                return View(nameof(Index), await BuildAsync(null, form));
            }

            TempData["Flash"] = result.Message;

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Active(int id, bool active)
        {
            var result = await _categories.SetActiveAsync(id, active);

            if (!result.Found)
                return NotFound();

            TempData["Flash"] = result.Message;

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _categories.DeleteAsync(id);

            if (!result.Found)
                return NotFound();

            TempData[result.Succeeded ? "Flash" : "FlashError"] = result.Message;

            return RedirectToAction(nameof(Index));
        }

        private async Task<CategoriesViewModel> BuildAsync(int? edit, CategoryFormViewModel? form = null)
        {
            var rows = await _categories.ListAsync();

            if (form == null && edit != null)
            {
                var row = rows.FirstOrDefault(r => r.Category.Id == edit);

                if (row != null)
                {
                    form = new CategoryFormViewModel
                    {
                        Id = row.Category.Id,
                        Name = row.Category.Name,
                        Kind = row.Category.Kind,
                        Icon = row.Category.Icon
                    };
                }
            }

            return new CategoriesViewModel
            {
                Income = rows.Where(r => r.Category.Kind == CategoryKind.Income).ToList(),
                Expense = rows.Where(r => r.Category.Kind == CategoryKind.Expense).ToList(),
                Form = form ?? new CategoryFormViewModel()
            };
        }
    }
}
