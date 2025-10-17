using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MT.Controllers
{
    public class SiteController : Controller
    {
        [AllowAnonymous]
        [HttpGet]
        public IActionResult Index(string? lang = "en")
        {
            ViewBag.Lang = lang?.ToLower() == "ar" ? "ar" : "en";
            return View();
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Site2(string? lang = "en")
        {
            ViewBag.Lang = lang?.ToLower() == "ar" ? "ar" : "en";
            return View();
        }

    }
}
