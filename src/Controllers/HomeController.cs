using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT.Data;
using MT.Models;
using System.Diagnostics;
using System.Security.Claims;

namespace MT.Controllers
{
[Authorize]
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _db;
        
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger, ApplicationDbContext db)
        {
            _logger = logger;
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            var todayStartUtc = DateTime.UtcNow.Date;
            var todayEndUtc = todayStartUtc.AddDays(1);

            // Base query scoping by role
            var baseQuery = _db.VehicleRegistrations.AsQueryable();
            // If VehicleOwner, show only their own records by phone
            if (User?.IsInRole("VehicleOwner") == true)
            {
                var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
                string? ownerPhone = await _db.UserProfiles
                    .Where(p => p.UserId == uid)
                    .Select(p => p.Phone)
                    .FirstOrDefaultAsync();
                if (string.IsNullOrWhiteSpace(ownerPhone))
                {
                    ownerPhone = await _db.Users
                        .Where(u => u.Id == uid)
                        .Select(u => u.PhoneNumber)
                        .FirstOrDefaultAsync();
                }
                if (!string.IsNullOrWhiteSpace(ownerPhone))
                {
                    baseQuery = baseQuery.Where(x => x.OwnerPhone == ownerPhone);
                }
                else
                {
                    baseQuery = baseQuery.Where(x => false); // safe default
                }
            }
            // Hide hidden records for non-SuperAdmin
            if (!(User?.IsInRole("SuperAdmin") ?? false))
            {
                baseQuery = baseQuery.Where(x => x.Status != "Hidden");
            }
            // MinistryOfficer dashboard counts still scoped to Approved
            if (User?.IsInRole("MinistryOfficer") == true)
            {
                baseQuery = baseQuery.Where(x => x.Status != "Hidden");
            }

            var vm = new DashboardVm
            {
                TodayBooking = await baseQuery
                    .CountAsync(x => x.SubmittedDate >= todayStartUtc && x.SubmittedDate < todayEndUtc),

                TotalApproval = await baseQuery.CountAsync(x => x.Status == "Approved"),

                TodayRejected = await baseQuery
                    .CountAsync(x => x.Status == "Rejected" && x.SubmittedDate >= todayStartUtc && x.SubmittedDate < todayEndUtc),

                TotalBooking = await baseQuery.CountAsync(),

                Recent = await baseQuery
                    .OrderByDescending(x => x.SubmittedDate)
                    .Take(10)
                    .ToListAsync()
            };

            return View(vm);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

       
    }


}
