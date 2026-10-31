using System.Security.Claims;

namespace MyFinances.Data
{
    /// <summary>
    /// Whose records this request may see. Normally the signed-in
    /// person; sign-up, which runs before anyone is signed in, and the
    /// tests stand in for a user with <see cref="ActAs"/>.
    /// </summary>
    public class CurrentUser
    {
        private readonly IHttpContextAccessor? _httpContextAccessor;

        private int? _actingAs;

        public CurrentUser(IHttpContextAccessor? httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public int? UserId
        {
            get
            {
                if (_actingAs != null)
                    return _actingAs;

                var value = _httpContextAccessor?
                    .HttpContext?
                    .User
                    .FindFirstValue(ClaimTypes.NameIdentifier);

                return int.TryParse(value, out var id) ? id : null;
            }
        }

        public int RequiredUserId =>
            UserId ?? throw new InvalidOperationException(
                "No one is signed in, so there are no records to work with.");

        public IDisposable ActAs(int userId)
        {
            var previous = _actingAs;

            _actingAs = userId;

            return new Restore(() => _actingAs = previous);
        }

        private sealed class Restore(Action undo) : IDisposable
        {
            public void Dispose() => undo();
        }
    }
}
