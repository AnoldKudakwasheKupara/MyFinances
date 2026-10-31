using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MyFinances.Services
{
    public static class EnumText
    {
        /// <summary>The [Display] name if there is one, otherwise the name itself.</summary>
        public static string Text(this Enum value)
        {
            var member = value.GetType().GetMember(value.ToString()).FirstOrDefault();

            return member?.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? value.ToString();
        }

        public static IEnumerable<SelectListItem> Options<T>(T? selected = null, Func<T, bool>? include = null)
            where T : struct, Enum =>
            Enum.GetValues<T>()
                .Where(v => include == null || include(v))
                .Select(v => new SelectListItem(v.Text(), v.ToString(), selected != null && selected.Value.Equals(v)));
    }
}
