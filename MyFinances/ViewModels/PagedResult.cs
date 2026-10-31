using Microsoft.EntityFrameworkCore;

namespace MyFinances.ViewModels
{
    public class PagedResult<T>
    {
        public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

        public int Page { get; init; } = 1;

        public int PageSize { get; init; }

        public int TotalCount { get; init; }

        public int TotalPages { get; init; }

        public bool HasPrevious => Page > 1;

        public bool HasNext => Page < TotalPages;

        public bool IsPaged => TotalPages > 1;
    }

    public static class PagedQueryExtensions
    {
        public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
            this IQueryable<T> query,
            int page,
            int pageSize)
        {
            if (pageSize < 1)
                pageSize = 1;

            var totalCount = await query.CountAsync();

            var totalPages =
                (int)Math.Ceiling(totalCount / (double)pageSize);

            if (page < 1)
                page = 1;

            if (totalPages > 0 && page > totalPages)
                page = totalPages;

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<T>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages
            };
        }
    }
}
