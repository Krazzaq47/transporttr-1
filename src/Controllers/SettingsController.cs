using Microsoft.AspNetCore.Mvc;
using MT.Data;
using MT.Models;

namespace MT.Controllers
{
    public class SettingsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;

        public IActionResult Index()
        {
            Settings settings = _db.Settings.FirstOrDefault();
            
            if (settings==null)
            {
                settings = new Settings();
            }

            return View(settings);
        }
        public SettingsController(ApplicationDbContext db, IWebHostEnvironment env)
        {
            _db = db; // <— this should NOT be null
            _env = env;
        }

        // POST: Settings/Save
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(Settings model)
        {
            if (!ModelState.IsValid)
            {
                // Redisplay the form if validation fails
                return View("Index", model);
            }

            var settings = _db.Settings.FirstOrDefault();

            if (settings == null)
            {
                // Insert new record if not exists
                model.CreatedAt = DateTime.Now;
                model.UpdatedAt = DateTime.Now;
                _db.Settings.Add(model);
            }
            else
            {
                // Update existing record
                settings.AllowedTruckNumber = model.AllowedTruckNumber;
                settings.AllowedTankerNumber = model.AllowedTankerNumber;
                settings.AutoCancellationDays = model.AutoCancellationDays;
                settings.UpdatedAt = DateTime.Now;

                _db.Settings.Update(settings);
            }

            await _db.SaveChangesAsync();

            TempData["Success"] = "Settings saved successfully!";
            return RedirectToAction(nameof(Index));
        }
    }
}
