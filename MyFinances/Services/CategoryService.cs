using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;
using MyFinances.ViewModels;

namespace MyFinances.Services
{
    public class CategoryService
    {
        private readonly ApplicationDbContext _context;

        public CategoryService(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>Active categories of one kind, for a picker.</summary>
        public Task<List<Category>> ActiveAsync(CategoryKind kind, int? alsoInclude = null) =>
            _context.Categories
                .AsNoTracking()
                .Where(c => c.Kind == kind && (c.IsActive || c.Id == alsoInclude))
                .OrderBy(c => c.Name)
                .ToListAsync();

        public async Task<List<CategoryRow>> ListAsync()
        {
            var used = await _context.Transactions
                .Where(t => t.CategoryId != null)
                .GroupBy(t => t.CategoryId!.Value)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);

            var categories = await _context.Categories
                .AsNoTracking()
                .OrderBy(c => c.Kind)
                .ThenBy(c => c.Name)
                .ToListAsync();

            return categories
                .Select(c => new CategoryRow(c, used.GetValueOrDefault(c.Id)))
                .ToList();
        }

        public async Task<ServiceResult> SaveAsync(int? id, CategoryFormViewModel form)
        {
            var name = form.Name.Trim();

            var clash = await _context.Categories.AnyAsync(c =>
                c.Kind == form.Kind && c.Name.ToLower() == name.ToLower() && c.Id != id);

            if (clash)
                return ServiceResult.Refused($"There is already an {form.Kind.ToString().ToLower()} category called {name}.");

            Category? category;

            if (id == null)
            {
                category = new Category { Kind = form.Kind };
                _context.Categories.Add(category);
            }
            else
            {
                category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id);

                if (category == null)
                    return ServiceResult.Missing();

                if (category.Kind != form.Kind && await InUseAsync(category.Id))
                {
                    return ServiceResult.Refused(
                        "This category is already in use, so it cannot switch between income and expense.");
                }

                category.Kind = form.Kind;
            }

            category.Name = name;
            category.Icon = string.IsNullOrWhiteSpace(form.Icon) ? "tag" : form.Icon.Trim();

            await _context.SaveChangesAsync();

            return ServiceResult.Ok(id == null ? $"{name} was added." : $"{name} was updated.");
        }

        public async Task<ServiceResult> SetActiveAsync(int id, bool active)
        {
            var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id);

            if (category == null)
                return ServiceResult.Missing();

            category.IsActive = active;
            await _context.SaveChangesAsync();

            return ServiceResult.Ok(active
                ? $"{category.Name} can be used again."
                : $"{category.Name} is deactivated. Past transactions keep it; new ones cannot use it.");
        }

        public async Task<ServiceResult> DeleteAsync(int id)
        {
            var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id);

            if (category == null)
                return ServiceResult.Missing();

            if (await InUseAsync(id))
                return ServiceResult.Refused($"{category.Name} is in use, so it can only be deactivated.");

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            return ServiceResult.Ok($"{category.Name} was deleted.");
        }

        public async Task<bool> InUseAsync(int id) =>
            await _context.Transactions.AnyAsync(t => t.CategoryId == id) ||
            await _context.Budgets.AnyAsync(b => b.CategoryId == id) ||
            await _context.Bills.AnyAsync(b => b.CategoryId == id);
    }
}
