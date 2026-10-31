using Microsoft.AspNetCore.Mvc;

namespace MyFinances.Controllers
{
    /// <summary>
    /// Lets a page that is being read or filled in, but not
    /// submitted, keep its session open.
    /// </summary>
    public class SessionController : Controller
    {
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult KeepAlive() => NoContent();
    }
}
