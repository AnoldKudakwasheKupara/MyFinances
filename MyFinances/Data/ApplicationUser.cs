using Microsoft.AspNetCore.Identity;

namespace MyFinances.Data
{
    public class ApplicationUser : IdentityUser<int>
    {
        [PersonalData]
        public string FullName { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
    }
}
