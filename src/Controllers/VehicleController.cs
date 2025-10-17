using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using MT.Data;
using System.IO.Compression;        // ZipFile, ZipArchive
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuestPDF.Helpers;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using QRCoder;
using ClosedXML.Excel;
using System.Globalization;

namespace MT.Controllers
{
    [Authorize]
    public class VehicleController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
       
        public VehicleController(ApplicationDbContext db, IWebHostEnvironment env)
        {
            _db = db; // <— this should NOT be null
            _env = env;
        }

        private static string DigitsOnly(string? s) => new string((s ?? string.Empty).Where(char.IsDigit).ToArray());
        private static string NormalizePhone11(string? s)
        {
            var d = DigitsOnly(s);
            if (string.IsNullOrEmpty(d)) return string.Empty;
            if (d.StartsWith("974")) d = d.Length > 11 ? d.Substring(0, 11) : d;
            else d = ("974" + d);
            if (d.Length > 11) d = d.Substring(0, 11);
            return d;
        }

        // Export current filtered list to Excel
        [HttpGet]
        [Authorize(Roles = "Admin,SuperAdmin,DocumentVerifier,FinalApprover,MinistryOfficer,Owner,VehicleOwner")]
        public async Task<IActionResult> ExportExcel(
            string type = "all",
            string? q = null,
            string sort = "date",
            string dir = "desc",
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? owner = null,
            string? driver = null)
        {
            var query = _db.VehicleRegistrations.AsQueryable();

    // If VehicleOwner, restrict to own records by phone
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
            query = query.Where(v => v.OwnerPhone == ownerPhone);
        }
        else
        {
            // If we cannot resolve phone, show nothing to be safe
            query = query.Where(v => false);
        }
    }

            // All roles (including Owner and MinistryOfficer) can export all records
            // Except: hide records for non-SuperAdmin
            if (!(User?.IsInRole("SuperAdmin") ?? false))
            {
                query = query.Where(v => v.Status != "Hidden");
            }

            // Type filter
            if (string.Equals(type, "truck", StringComparison.OrdinalIgnoreCase))
                query = query.Where(v => v.VehicleType == "truck");
            else if (string.Equals(type, "tank", StringComparison.OrdinalIgnoreCase))
                query = query.Where(v => v.VehicleType == "tank");

            // Date filters (SubmittedDate assumed UTC in DB)
            if (fromDate.HasValue)
            {
                var f = DateTime.SpecifyKind(fromDate.Value.Date, DateTimeKind.Utc);
                query = query.Where(v => v.SubmittedDate >= f);
            }
            if (toDate.HasValue)
            {
                var t = DateTime.SpecifyKind(toDate.Value.Date.AddDays(1), DateTimeKind.Utc);
                query = query.Where(v => v.SubmittedDate < t);
            }

            // Owner/Driver filters
            if (!string.IsNullOrWhiteSpace(owner))
            {
                var ow = owner.Trim();
                query = query.Where(v => v.VehicleOwnerName.Contains(ow) || v.OwnerPhone.Contains(ow));
            }
            if (!string.IsNullOrWhiteSpace(driver))
            {
                var dr = driver.Trim();
                query = query.Where(v => v.DriverName.Contains(dr) || v.DriverPhone.Contains(dr));
            }

            // Quick search
            if (!string.IsNullOrWhiteSpace(q))
            {
                var s = q.Trim();
                query = query.Where(v =>
                    v.VehicleOwnerName.Contains(s) || v.DriverName.Contains(s) ||
                    v.OwnerPhone.Contains(s) || v.DriverPhone.Contains(s) ||
                    v.Status.Contains(s));
            }

            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            query = (sort switch
            {
                "owner" => (desc ? query.OrderByDescending(v => v.VehicleOwnerName) : query.OrderBy(v => v.VehicleOwnerName)),
                "driver" => (desc ? query.OrderByDescending(v => v.DriverName) : query.OrderBy(v => v.DriverName)),
                "phone" => (desc ? query.OrderByDescending(v => v.OwnerPhone) : query.OrderBy(v => v.OwnerPhone)),
                "status" => (desc ? query.OrderByDescending(v => v.Status) : query.OrderBy(v => v.Status)),
                _ => (desc ? query.OrderByDescending(v => v.SubmittedDate) : query.OrderBy(v => v.SubmittedDate))
            });

            var data = await query.ToListAsync();

            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Registrations");

            // Header
            var headers = new[] { "#", "Date", "Type", "Owner", "Driver", "Phone", "Status", "Approved By", "Approved At", "Reference", "Token" };
            for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
            ws.Row(1).Style.Font.Bold = true;

            // Rows
            int r = 2; int idx = 1;
            foreach (var v in data)
            {
                ws.Cell(r, 1).Value = idx++;
                ws.Cell(r, 2).Value = v.SubmittedDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                ws.Cell(r, 3).Value = v.VehicleType;
                ws.Cell(r, 4).Value = v.VehicleOwnerName;
                ws.Cell(r, 5).Value = v.DriverName;
                ws.Cell(r, 6).Value = v.OwnerPhone;
                ws.Cell(r, 7).Value = v.Status;
                ws.Cell(r, 8).Value = v.ApprovedByName;
                ws.Cell(r, 9).Value = v.ApprovedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                ws.Cell(r, 10).Value = $"APP{v.Id:D6}";
                ws.Cell(r, 11).Value = (v.Status == "Approved" ? v.UniqueToken : "");
                r++;
            }

            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            var bytes = ms.ToArray();
            var fname = $"Registrations_{type}_{DateTime.Now:yyyyMMdd-HHmm}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fname);
        }

        // Generate a URL-safe, short unique token
        private string GenerateUniqueToken(int length = 12)
        {
            // Base32 alphabet (no confusing chars), URL-safe
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            Span<byte> buffer = stackalloc byte[16];
            using var rng = RandomNumberGenerator.Create();
            string token;
            do
            {
                rng.GetBytes(buffer);
                var chars = new char[length];
                for (int i = 0; i < length; i++)
                {
                    chars[i] = alphabet[buffer[i % buffer.Length] % alphabet.Length];
                }
                token = new string(chars);
            }
            while (_db.VehicleRegistrations.Any(v => v.UniqueToken == token));
            return token.ToLowerInvariant();
        }

        [HttpGet]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> ExportExcelAll(
            string type = "all",
            string? q = null,
            string sort = "date",
            string dir = "desc",
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? owner = null,
            string? driver = null, string? hiddenList="All")
        {
            var query = _db.VehicleRegistrations.AsQueryable();

            // If VehicleOwner, restrict to own records by phone
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
                    query = query.Where(v => v.OwnerPhone == ownerPhone);
                }
                else
                {
                    // If we cannot resolve phone, show nothing to be safe
                    query = query.Where(v => false);
                }
            }

            // All roles (including Owner and MinistryOfficer) can export all records
            // Except: hide records for non-SuperAdmin
            if (!(User?.IsInRole("SuperAdmin") ?? false))
            {
                query = query.Where(v => v.Status != "Hidden");
            }

            // Type filter
            if (string.Equals(type, "truck", StringComparison.OrdinalIgnoreCase))
                query = query.Where(v => v.VehicleType == "truck");
            else if (string.Equals(type, "tank", StringComparison.OrdinalIgnoreCase))
                query = query.Where(v => v.VehicleType == "tank");

            // Date filters (SubmittedDate assumed UTC in DB)
            if (fromDate.HasValue)
            {
                var f = DateTime.SpecifyKind(fromDate.Value.Date, DateTimeKind.Utc);
                query = query.Where(v => v.SubmittedDate >= f);
            }
            if (toDate.HasValue)
            {
                var t = DateTime.SpecifyKind(toDate.Value.Date.AddDays(1), DateTimeKind.Utc);
                query = query.Where(v => v.SubmittedDate < t);
            }

            // Owner/Driver filters
            if (!string.IsNullOrWhiteSpace(owner))
            {
                var ow = owner.Trim();
                query = query.Where(v => v.VehicleOwnerName.Contains(ow) || v.OwnerPhone.Contains(ow));
            }
            if (!string.IsNullOrWhiteSpace(driver))
            {
                var dr = driver.Trim();
                query = query.Where(v => v.DriverName.Contains(dr) || v.DriverPhone.Contains(dr));
            }

            // Quick search
            if (!string.IsNullOrWhiteSpace(q))
            {
                var s = q.Trim();
                query = query.Where(v =>
                    v.VehicleOwnerName.Contains(s) || v.DriverName.Contains(s) ||
                    v.OwnerPhone.Contains(s) || v.DriverPhone.Contains(s) ||
                    v.Status.Contains(s));
            }

            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            query = (sort switch
            {
                "owner" => (desc ? query.OrderByDescending(v => v.VehicleOwnerName) : query.OrderBy(v => v.VehicleOwnerName)),
                "driver" => (desc ? query.OrderByDescending(v => v.DriverName) : query.OrderBy(v => v.DriverName)),
                "phone" => (desc ? query.OrderByDescending(v => v.OwnerPhone) : query.OrderBy(v => v.OwnerPhone)),
                "status" => (desc ? query.OrderByDescending(v => v.Status) : query.OrderBy(v => v.Status)),
                _ => (desc ? query.OrderByDescending(v => v.SubmittedDate) : query.OrderBy(v => v.SubmittedDate))
            });

            var data = await query.ToListAsync();

            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Registrations");

            // Header
            var headers = new[] { "#", "Date", "Type", "Owner", "Driver", "Phone", "Status", "Approved By", "Approved At", "Reference", "Token" };
            for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
            ws.Row(1).Style.Font.Bold = true;

            // Rows
            int r = 2; int idx = 1;
            foreach (var v in data)
            {
                ws.Cell(r, 1).Value = idx++;
                ws.Cell(r, 2).Value = v.SubmittedDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                ws.Cell(r, 3).Value = v.VehicleType;
                ws.Cell(r, 4).Value = v.VehicleOwnerName;
                ws.Cell(r, 5).Value = v.DriverName;
                ws.Cell(r, 6).Value = v.OwnerPhone;
                ws.Cell(r, 7).Value = v.Status;
                ws.Cell(r, 8).Value = v.ApprovedByName;
                ws.Cell(r, 9).Value = v.ApprovedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                ws.Cell(r, 10).Value = $"APP{v.Id:D6}";
                ws.Cell(r, 11).Value = (v.Status == "Approved" ? v.UniqueToken : "");
                r++;
            }

            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            var bytes = ms.ToArray();
            var fname = $"Registrations_{type}_{DateTime.Now:yyyyMMdd-HHmm}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fname);
        }

        //// Generate a URL-safe, short unique token
        //private string GenerateUniqueToken(int length = 12)
        //{
        //    // Base32 alphabet (no confusing chars), URL-safe
        //    const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        //    Span<byte> buffer = stackalloc byte[16];
        //    using var rng = RandomNumberGenerator.Create();
        //    string token;
        //    do
        //    {
        //        rng.GetBytes(buffer);
        //        var chars = new char[length];
        //        for (int i = 0; i < length; i++)
        //        {
        //            chars[i] = alphabet[buffer[i % buffer.Length] % alphabet.Length];
        //        }
        //        token = new string(chars);
        //    }
        //    while (_db.VehicleRegistrations.Any(v => v.UniqueToken == token));
        //    return token.ToLowerInvariant();
        //}

        //[AllowAnonymous]
        [HttpGet]
        public IActionResult Register(string lang = "en")
        {
            ViewBag.Lang = (lang == "ar" ? "ar" : "en");
            // Fallback flags if TempData is not available (e.g., CDN/caching edge case)
            var submitted = HttpContext.Request.Query["submitted"].ToString();
            var app = HttpContext.Request.Query["app"].ToString();
            ViewBag.Submitted = submitted == "1";
            if (!string.IsNullOrWhiteSpace(app)) ViewBag.AppNum = app;
            return View();
        }

        [HttpPost]
        //[AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(VehicleRegisterVm model, string lang = "en")
        {
            ViewBag.Lang = lang == "ar" ? "ar" : "en";

            //if (!ModelState.IsValid)
            //    return View(model);

            // Choose subfolder by vehicle type
            var typeFolder = (model.VehicleType?.Equals("truck", StringComparison.OrdinalIgnoreCase) == true)
                ? "truck" : "tank";

            // ensure upload dir
            var root = _env.WebRootPath ?? throw new InvalidOperationException("WebRootPath not configured.");
            var uploadDir = Path.Combine(root, "uploads", typeFolder);
            Directory.CreateDirectory(uploadDir);

            // validation config
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };
            const long maxBytes = 5 * 1024 * 1024; // 5MB

            string? Save(IFormFile? file)
            {
                if (file == null || file.Length == 0) return null;

                // basic validation
                if (file.Length > maxBytes)
                    throw new InvalidOperationException("File too large. Max 5 MB.");
                var ext = Path.GetExtension(file.FileName);
                if (string.IsNullOrWhiteSpace(ext) || !allowed.Contains(ext))
                    throw new InvalidOperationException("Invalid file type. Allowed: PNG, JPG, JPEG.");

                var safeName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{ext}";
                var full = Path.Combine(uploadDir, safeName);
                using var fs = new FileStream(full, FileMode.Create);
                file.CopyTo(fs);
                return $"/uploads/{typeFolder}/{safeName}"; // web-relative path
            }

            try
            {

                UserProfile userProfile=_db.UserProfiles.Where(x=>x.Username == User.FindFirstValue(ClaimTypes.Name)).FirstOrDefault();

                var entity = new MT.Data.VehicleRegistration
                {
                    VehicleType = model.VehicleType?.ToLowerInvariant() == "truck" ? "truck" : "tank",
                    OwnerPhone = NormalizePhone11(userProfile.Phone),
                    VehicleOwnerName = userProfile.Name,
                    DriverPhone = NormalizePhone11(model.DriverPhone),
                    DriverName = model.DriverName,
                    Status = "Pending",
                    SubmittedDate = DateTime.UtcNow,
                    ClientIP = HttpContext.Connection.RemoteIpAddress?.ToString(),   
                    VehicleNumber=  model.VehicleNumber
                };

                // If logged-in Owner, enforce OwnerPhone from their profile; if profile missing phone, persist submitted phone into profile
                if (User?.IsInRole("Owner") == true)
                {
                    var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == uid);
                    if (profile != null)
                    {
                        if (!string.IsNullOrWhiteSpace(profile.Phone))
                        {
                            entity.OwnerPhone = profile.Phone;
                        }
                        else if (!string.IsNullOrWhiteSpace(model.OwnerPhone))
                        {
                            // save submitted phone into profile for future scoping
                            profile.Phone = model.OwnerPhone;
                            _db.UserProfiles.Update(profile);
                            await _db.SaveChangesAsync();
                            entity.OwnerPhone = profile.Phone;
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(model.OwnerPhone))
                    {
                        // No profile found: seed Identity user's PhoneNumber so scoping can use it
                        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
                        if (user != null)
                        {
                            if (string.IsNullOrWhiteSpace(user.PhoneNumber))
                            {
                                user.PhoneNumber = model.OwnerPhone;
                                _db.Users.Update(user);
                                await _db.SaveChangesAsync();
                            }
                            entity.OwnerPhone = user.PhoneNumber ?? model.OwnerPhone;
                        }
                    }
                }

                if (typeFolder == "tank")
                {
                    entity.IdCardBothSidesPath = Save(model.IdCardBothSides);
                    entity.TankerFormBothSidesPath = Save(model.TankerFormBothSides);
                    entity.IbanCertificatePath = Save(model.IbanCertificate);
                    entity.TankCapacityCertPath = Save(model.TankCapacityCert);
                    entity.LandfillWorksPath = Save(model.LandfillWorks);
                    entity.SignedRegistrationFormPath = Save(model.SignedRegistrationForm);
                    entity.ReleaseFormPath = Save(model.ReleaseForm);
                }
                else // truck
                {
                    entity.Truck_IdCardPath = Save(model.Truck_IdCard);
                    entity.Truck_TrailerRegistrationPath = Save(model.Truck_TrailerRegistration);
                    entity.Truck_TrafficCertificatePath = Save(model.Truck_TrafficCertificate);
                    entity.Truck_IbanCertificatePath = Save(model.Truck_IbanCertificate);
                    entity.Truck_VehicleRegFormPath = Save(model.Truck_VehicleRegForm);
                    entity.Truck_ReleaseFormPath = Save(model.Truck_ReleaseForm);
                }

                // Ensure public-friendly token
                if (string.IsNullOrWhiteSpace(entity.UniqueToken))
                {
                    entity.UniqueToken = GenerateUniqueToken();
                }

                _db.VehicleRegistrations.Add(entity);
                await _db.SaveChangesAsync();

                // Use DB Id as the Application Number (formatted)
                var appNum = $"APP{entity.Id:D6}";
                TempData["ok"] = lang == "ar" ? "تم الإرسال بنجاح" : "Submitted successfully";
                TempData["appNum"] = appNum;
                // Also pass via query as a fallback so the modal can still show if TempData gets lost
                return RedirectToAction(nameof(Register), new { lang, submitted = 1, app = appNum });
            }
            catch (Exception)
            {
                // show a friendly message
                var msg = ViewBag.Lang == "ar"
                    ? "حدث خطأ أثناء رفع الملفات. الرجاء المحاولة مرة أخرى."
                    : "There was a problem uploading the files. Please try again.";
                ModelState.AddModelError(string.Empty, msg);

                // (optional) log ex
                return View(model);
            }
        }


        [Authorize(Roles = "Admin,SuperAdmin,DocumentVerifier,FinalApprover,MinistryOfficer,Owner,VehicleOwner")]
        public async Task<IActionResult> List(
            string type = "all",
            int page = 1,
            int pageSize = 10,
            string? q = null,
            string sort = "date",
            string dir = "desc",
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? owner = null,
            string? driver = null)
        {
            // If no 'type' was explicitly provided in the querystring, redirect to default type=truck
            if (!Request.Query.ContainsKey("type"))
            {
                return RedirectToAction(nameof(List), new
                {
                    type = "truck",
                    page,
                    pageSize,
                    q,
                    sort,
                    dir,
                    fromDate,
                    toDate,
                    owner,
                    driver
                });
            }

            var query = _db.VehicleRegistrations.AsQueryable();
            // If VehicleOwner, restrict to their own records by phone
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
                    var d = DigitsOnly(ownerPhone);
                    var norm = NormalizePhone11(d);
                    var core = norm.Length >= 11 ? norm.Substring(3) : d; // 8-digit local
                    query = query.Where(x => x.OwnerPhone == norm || x.OwnerPhone == core || x.OwnerPhone == d);
                }
                else
                {
                    query = query.Where(x => false); // no phone resolved -> no data
                }
            }
            // All roles (including Owner and MinistryOfficer) can see all records
            // Except: hide records with Status == "Hidden" for everyone except SuperAdmin
            if ((User?.IsInRole("VehicleOwner") ?? true))
            {
               // query = query.Where(x => x.Status != "Hidden");
            }
            else
            {
                query = query.Where(x => x.Status != "Hidden");
            }

            // Filter by vehicle type if specified
            if (!string.IsNullOrEmpty(type) && type != "all")
            {
                query = query.Where(x => x.VehicleType == type);
            }

            // Date range filters
            if (fromDate.HasValue)
            {
                var from = DateTime.SpecifyKind(fromDate.Value.Date, DateTimeKind.Utc);
                query = query.Where(x => x.SubmittedDate >= from);
            }
            if (toDate.HasValue)
            {
                // make 'to' inclusive by moving to next day exclusive
                var toExclusive = DateTime.SpecifyKind(toDate.Value.Date.AddDays(1), DateTimeKind.Utc);
                query = query.Where(x => x.SubmittedDate < toExclusive);
            }

            // Owner filter (name or phone)
            if (!string.IsNullOrWhiteSpace(owner))
            {
                var term = owner.Trim();
                query = query.Where(x =>
                    (x.VehicleOwnerName != null && x.VehicleOwnerName.Contains(term)) ||
                    (x.OwnerPhone != null && x.OwnerPhone.Contains(term))
                );
            }

            // Driver filter (name or phone)
            if (!string.IsNullOrWhiteSpace(driver))
            {
                var term = driver.Trim();
                query = query.Where(x =>
                    (x.DriverName != null && x.DriverName.Contains(term)) ||
                    (x.DriverPhone != null && x.DriverPhone.Contains(term))
                );
            }

            // Quick search filter (owner name, driver name, phone, token)
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(x =>
                    (x.VehicleOwnerName != null && x.VehicleOwnerName.Contains(term)) ||
                    (x.DriverName != null && x.DriverName.Contains(term)) ||
                    (x.OwnerPhone != null && x.OwnerPhone.Contains(term)) ||
                    (x.UniqueToken != null && x.UniqueToken.Contains(term))
                );
            }
            
            var totalCount = await query.CountAsync();

            // Sorting
            bool desc = !string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
            IOrderedQueryable<VehicleRegistration> ordered;
            switch ((sort ?? "date").ToLowerInvariant())
            {
                case "owner":
                    ordered = desc ? query.OrderByDescending(x => x.VehicleOwnerName) : query.OrderBy(x => x.VehicleOwnerName);
                    break;
                case "driver":
                    ordered = desc ? query.OrderByDescending(x => x.DriverName) : query.OrderBy(x => x.DriverName);
                    break;
                case "phone":
                    ordered = desc ? query.OrderByDescending(x => x.OwnerPhone) : query.OrderBy(x => x.OwnerPhone);
                    break;
                case "status":
                    ordered = desc ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status);
                    break;
                case "token":
                    ordered = desc ? query.OrderByDescending(x => x.UniqueToken) : query.OrderBy(x => x.UniqueToken);
                    break;
                case "date":
                default:
                    ordered = desc ? query.OrderByDescending(x => x.SubmittedDate) : query.OrderBy(x => x.SubmittedDate);
                    break;
            }

            var items = await ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
                
            // Pass counts for each type to the view (respect role filtering)
            var baseCountQuery = _db.VehicleRegistrations.AsQueryable();
    if (User?.IsInRole("VehicleOwner") == true)
    {
        var uid2 = User.FindFirstValue(ClaimTypes.NameIdentifier);
        string? ownerPhone2 = await _db.UserProfiles
            .Where(p => p.UserId == uid2)
            .Select(p => p.Phone)
            .FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(ownerPhone2))
        {
            ownerPhone2 = await _db.Users
                .Where(u => u.Id == uid2)
                .Select(u => u.PhoneNumber)
                .FirstOrDefaultAsync();
        }
        if (!string.IsNullOrWhiteSpace(ownerPhone2))
        {
            var d2 = DigitsOnly(ownerPhone2);
            var norm2 = NormalizePhone11(d2);
            var core2 = norm2.Length >= 11 ? norm2.Substring(3) : d2;
            baseCountQuery = baseCountQuery.Where(v => v.OwnerPhone == norm2 || v.OwnerPhone == core2 || v.OwnerPhone == d2);
        }
        else
        {
            baseCountQuery = baseCountQuery.Where(v => false);
        }
    }
            if (User?.IsInRole("MinistryOfficer") == true)
                baseCountQuery = baseCountQuery.Where(x => x.Status == "Approved");

            ViewBag.TruckCount = await baseCountQuery.CountAsync(x => x.VehicleType == "truck");
            ViewBag.TankCount = await baseCountQuery.CountAsync(x => x.VehicleType == "tank");
            ViewBag.CurrentType = type;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Query = q ?? string.Empty;
            ViewBag.OwnerFilter = owner ?? string.Empty;
            ViewBag.DriverFilter = driver ?? string.Empty;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd") ?? string.Empty;
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd") ?? string.Empty;
            ViewBag.TotalCount = totalCount;
            ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.Sort = sort;
            ViewBag.Dir = desc ? "desc" : "asc";
            var startIdx = totalCount == 0 ? 0 : ((page - 1) * pageSize) + 1;
            var endIdx = totalCount == 0 ? 0 : Math.Min(page * pageSize, totalCount);
            ViewBag.StartIndex = startIdx;
            ViewBag.EndIndex = endIdx;

            return View(items);
        }


        [Authorize(Roles = "Admin,SuperAdmin,DocumentVerifier,FinalApprover,MinistryOfficer,Owner,VehicleOwner")]
        public async Task<IActionResult> ListAll(
            string type = "all",
            int page = 1,
            int pageSize = 10,
            string? q = null,
            string sort = "date",
            string dir = "desc",
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? owner = null,
            string? driver = null, string? hiddenList="All")
        {
            // If no 'type' was explicitly provided in the querystring, redirect to default type=truck
            if (!Request.Query.ContainsKey("type"))
            {
                return RedirectToAction(nameof(List), new
                {
                    type = "truck",
                    page,
                    pageSize,
                    q,
                    sort,
                    dir,
                    fromDate,
                    toDate,
                    owner,
                    driver
                });
            }

            var query = _db.VehicleRegistrations.AsQueryable();
            // If VehicleOwner, restrict to their own records by phone
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
                    var d = DigitsOnly(ownerPhone);
                    var norm = NormalizePhone11(d);
                    var core = norm.Length >= 11 ? norm.Substring(3) : d; // 8-digit local
                    query = query.Where(x => x.OwnerPhone == norm || x.OwnerPhone == core || x.OwnerPhone == d);
                }
                else
                {
                    query = query.Where(x => false); // no phone resolved -> no data
                }
            }
            // All roles (including Owner and MinistryOfficer) can see all records
            // Except: hide records with Status == "Hidden" for everyone except SuperAdmin
            if (hiddenList=="Hidden")
            {
                query = query.Where(x => x.Status == "Hidden");
            }

            // Filter by vehicle type if specified
            if (!string.IsNullOrEmpty(type) && type != "all")
            {
                query = query.Where(x => x.VehicleType == type);
            }

            // Date range filters
            if (fromDate.HasValue)
            {
                var from = DateTime.SpecifyKind(fromDate.Value.Date, DateTimeKind.Utc);
                query = query.Where(x => x.SubmittedDate >= from);
            }
            if (toDate.HasValue)
            {
                // make 'to' inclusive by moving to next day exclusive
                var toExclusive = DateTime.SpecifyKind(toDate.Value.Date.AddDays(1), DateTimeKind.Utc);
                query = query.Where(x => x.SubmittedDate < toExclusive);
            }

            // Owner filter (name or phone)
            if (!string.IsNullOrWhiteSpace(owner))
            {
                var term = owner.Trim();
                query = query.Where(x =>
                    (x.VehicleOwnerName != null && x.VehicleOwnerName.Contains(term)) ||
                    (x.OwnerPhone != null && x.OwnerPhone.Contains(term))
                );
            }

            // Driver filter (name or phone)
            if (!string.IsNullOrWhiteSpace(driver))
            {
                var term = driver.Trim();
                query = query.Where(x =>
                    (x.DriverName != null && x.DriverName.Contains(term)) ||
                    (x.DriverPhone != null && x.DriverPhone.Contains(term))
                );
            }

            // Quick search filter (owner name, driver name, phone, token)
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(x =>
                    (x.VehicleOwnerName != null && x.VehicleOwnerName.Contains(term)) ||
                    (x.DriverName != null && x.DriverName.Contains(term)) ||
                    (x.OwnerPhone != null && x.OwnerPhone.Contains(term)) ||
                    (x.UniqueToken != null && x.UniqueToken.Contains(term))
                );
            }

            var totalCount = await query.CountAsync();

            // Sorting
            bool desc = !string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
            IOrderedQueryable<VehicleRegistration> ordered;
            switch ((sort ?? "date").ToLowerInvariant())
            {
                case "owner":
                    ordered = desc ? query.OrderByDescending(x => x.VehicleOwnerName) : query.OrderBy(x => x.VehicleOwnerName);
                    break;
                case "driver":
                    ordered = desc ? query.OrderByDescending(x => x.DriverName) : query.OrderBy(x => x.DriverName);
                    break;
                case "phone":
                    ordered = desc ? query.OrderByDescending(x => x.OwnerPhone) : query.OrderBy(x => x.OwnerPhone);
                    break;
                case "status":
                    ordered = desc ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status);
                    break;
                case "token":
                    ordered = desc ? query.OrderByDescending(x => x.UniqueToken) : query.OrderBy(x => x.UniqueToken);
                    break;
                case "date":
                default:
                    ordered = desc ? query.OrderByDescending(x => x.SubmittedDate) : query.OrderBy(x => x.SubmittedDate);
                    break;
            }

            var items = await ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Pass counts for each type to the view (respect role filtering)
            var baseCountQuery = _db.VehicleRegistrations.AsQueryable();
            if (User?.IsInRole("VehicleOwner") == true)
            {
                var uid2 = User.FindFirstValue(ClaimTypes.NameIdentifier);
                string? ownerPhone2 = await _db.UserProfiles
                    .Where(p => p.UserId == uid2)
                    .Select(p => p.Phone)
                    .FirstOrDefaultAsync();
                if (string.IsNullOrWhiteSpace(ownerPhone2))
                {
                    ownerPhone2 = await _db.Users
                        .Where(u => u.Id == uid2)
                        .Select(u => u.PhoneNumber)
                        .FirstOrDefaultAsync();
                }
                if (!string.IsNullOrWhiteSpace(ownerPhone2))
                {
                    var d2 = DigitsOnly(ownerPhone2);
                    var norm2 = NormalizePhone11(d2);
                    var core2 = norm2.Length >= 11 ? norm2.Substring(3) : d2;
                    baseCountQuery = baseCountQuery.Where(v => v.OwnerPhone == norm2 || v.OwnerPhone == core2 || v.OwnerPhone == d2);
                }
                else
                {
                    baseCountQuery = baseCountQuery.Where(v => false);
                }
            }
            if (User?.IsInRole("MinistryOfficer") == true)
                baseCountQuery = baseCountQuery.Where(x => x.Status == "Approved");

            ViewBag.TruckCount = await baseCountQuery.CountAsync(x => x.VehicleType == "truck");
            ViewBag.TankCount = await baseCountQuery.CountAsync(x => x.VehicleType == "tank");
            ViewBag.CurrentType = type;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Query = q ?? string.Empty;
            ViewBag.OwnerFilter = owner ?? string.Empty;
            ViewBag.DriverFilter = driver ?? string.Empty;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd") ?? string.Empty;
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd") ?? string.Empty;
            ViewBag.TotalCount = totalCount;
            ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.Sort = sort;
            ViewBag.Dir = desc ? "desc" : "asc";
            var startIdx = totalCount == 0 ? 0 : ((page - 1) * pageSize) + 1;
            var endIdx = totalCount == 0 ? 0 : Math.Min(page * pageSize, totalCount);
            ViewBag.StartIndex = startIdx;
            ViewBag.EndIndex = endIdx;
            ViewBag.HiddenList = hiddenList;

            return View(items);
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Details(int id, string lang = "en")
        {
            // Backward-compatible: redirect to token URL
            var rec = _db.VehicleRegistrations.SingleOrDefault(x => x.Id == id);
            if (rec == null) return NotFound();
            if (string.Equals(rec.Status, "Hidden", StringComparison.OrdinalIgnoreCase) && !(User?.IsInRole("SuperAdmin") ?? false))
                return NotFound();
            if (string.IsNullOrWhiteSpace(rec.UniqueToken))
            {
                rec.UniqueToken = GenerateUniqueToken();
                _db.VehicleRegistrations.Update(rec);
                _db.SaveChanges();
            }
            return RedirectToAction(nameof(DetailsByToken), new { token = rec.UniqueToken, lang });
        }

        // Pretty URL: /Vehicle/Details/{token}
        [HttpGet("Vehicle/Details/{token}")]
        [AllowAnonymous]
        public IActionResult DetailsByToken(string token, string lang = "en")
        {
            ViewBag.Lang = (lang == "ar" ? "ar" : "en");
            if (string.IsNullOrWhiteSpace(token)) return NotFound();
            var items = _db.VehicleRegistrations.FirstOrDefault(x => x.UniqueToken == token);
            if (items == null) return NotFound();
            if (string.Equals(items.Status, "Hidden", StringComparison.OrdinalIgnoreCase) && !(User?.IsInRole("SuperAdmin") ?? false))
                return NotFound();
            // Owners can view any record now; but restrict download actions to their own record
            if (User?.IsInRole("Owner") == true)
            {
                var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var ownerPhone = _db.UserProfiles
                    .Where(p => p.UserId == uid)
                    .Select(p => p.Phone)
                    .FirstOrDefault();
                if (string.IsNullOrWhiteSpace(ownerPhone))
                {
                    ownerPhone = _db.Users
                        .Where(u => u.Id == uid)
                        .Select(u => u.PhoneNumber)
                        .FirstOrDefault();
                }
                ViewBag.CanDownload = !string.IsNullOrWhiteSpace(ownerPhone) && string.Equals(ownerPhone, items.OwnerPhone, StringComparison.Ordinal);
            }
            else
            {
                ViewBag.CanDownload = true; // all non-Owner roles can download as per role-based UI
            }
            return View("Details", items);
        }

        // ===== Token-based wrappers to avoid exposing numeric ID =====
        [HttpGet("Vehicle/DownloadAllByToken/{token}")]
        [AllowAnonymous]
        public async Task<IActionResult> DownloadAllByToken(string token)
        {
            var v = await _db.VehicleRegistrations.FirstOrDefaultAsync(x => x.UniqueToken == token);
            if (v == null) return NotFound();
            if (string.Equals(v.Status, "Hidden", StringComparison.OrdinalIgnoreCase) && !(User?.IsInRole("SuperAdmin") ?? false))
                return NotFound();
            return await DownloadAll(v.Id);
        }

        [HttpGet("Vehicle/DownloadFileByToken/{token}")]
        [AllowAnonymous]
        public async Task<IActionResult> DownloadFileByToken(string token, string file)
        {
            var v = await _db.VehicleRegistrations.FirstOrDefaultAsync(x => x.UniqueToken == token);
            if (v == null) return NotFound();
            if (string.Equals(v.Status, "Hidden", StringComparison.OrdinalIgnoreCase) && !(User?.IsInRole("SuperAdmin") ?? false))
                return NotFound();
            return await DownloadFile(v.Id, file);
        }

        [HttpGet("Vehicle/ExportPdfByToken/{token}")]
        [AllowAnonymous]
        public async Task<IActionResult> ExportPdfByToken(string token)
        {
            var v = await _db.VehicleRegistrations.FirstOrDefaultAsync(x => x.UniqueToken == token);
            if (v == null) return NotFound();
            if (string.Equals(v.Status, "Hidden", StringComparison.OrdinalIgnoreCase) && !(User?.IsInRole("SuperAdmin") ?? false))
                return NotFound();
            return await ExportPdf(v.Id);
        }


        // Admin utility: generate tokens for all records missing UniqueToken
        [HttpPost]
        [Authorize(Roles = "Admin,SuperAdmin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BackfillTokens(int length = 12)
        {
            var list = await _db.VehicleRegistrations
                .Where(v => string.IsNullOrEmpty(v.UniqueToken))
                .ToListAsync();
            foreach (var v in list)
            {
                // temporarily use the provided length
                string token;
                do
                {
                    token = GenerateUniqueToken(length);
                } while (await _db.VehicleRegistrations.AnyAsync(x => x.UniqueToken == token));
                v.UniqueToken = token;
                _db.VehicleRegistrations.Update(v);
            }
            var count = await _db.SaveChangesAsync();
            TempData["ok"] = $"Backfilled {list.Count} records with tokens.";
            return RedirectToAction(nameof(List));
        }

        [HttpPost]
        [Authorize(Roles = "SuperAdmin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unhide(long id)
        {
            var rec = await _db.VehicleRegistrations.FindAsync(id);
            if (rec == null)
                return NotFound();
            if (rec.Status != "Hidden")
            {
                TempData["ok"] = "Record is not hidden.";
                return RedirectToAction(nameof(ListAll));
            }
            // Restore to previous status if available; fallback to Pending
            rec.Status = string.IsNullOrWhiteSpace(rec.PreviousStatus) ? "Pending" : rec.PreviousStatus;
            rec.PreviousStatus = null;
            _db.VehicleRegistrations.Update(rec);
            await _db.SaveChangesAsync();
            TempData["ok"] = "Record unhidden.";
            return RedirectToAction(nameof(ListAll));
        }

[HttpPost]
[Authorize(Roles = "Admin,SuperAdmin,DocumentVerifier,FinalApprover")]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Approve(long id, string? comment)
{
    var record = await _db.VehicleRegistrations.FindAsync(id);
    if (record == null)
        return NotFound();

    if (string.Equals(record.Status, "Hidden", StringComparison.OrdinalIgnoreCase))
    {
        TempData["err"] = "This record is hidden and cannot be modified.";
        return RedirectToAction(nameof(List));
    }

    // 1) Document Verifier can only move record to Under Review
    if (User?.IsInRole("DocumentVerifier") == true)
    {
        if (record.Status == "Approved")
        {
            TempData["ok"] = "This registration is already approved.";
            return RedirectToAction(nameof(List));
        }
        if (record.Status != "Under Review")
        {
            record.Status = "Under Review";
            _db.VehicleRegistrations.Update(record);
            await _db.SaveChangesAsync();
            TempData["ok"] = "Registration moved to Under Review.";
            return RedirectToAction(nameof(List));
        }
        TempData["ok"] = "Registration is already under review.";
        return RedirectToAction(nameof(List));
    }

    // 2) Final approver / Admin can approve
    //    - FinalApprover: only when not Pending (must be Under Review)
    //    - Admin/SuperAdmin: can approve directly (even if Pending)
    if (User?.IsInRole("Admin") == true || User?.IsInRole("SuperAdmin") == true || User?.IsInRole("FinalApprover") == true)
    {
        if (User?.IsInRole("FinalApprover") == true && string.Equals(record.Status, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            TempData["err"] = "This registration must be verified by Document Verifier before final approval.";
            return RedirectToAction(nameof(List));
        }
        if (record.Status == "Approved")
        {
            TempData["ok"] = "This registration is already approved.";
            return RedirectToAction(nameof(List));
        }

        // Generate next token in REF series
        var lastToken = await _db.VehicleRegistrations
            .Where(v => v.UniqueToken != null && v.UniqueToken.StartsWith("REF"))
            .OrderByDescending(v => v.UniqueToken)
            .Select(v => v.UniqueToken)
            .FirstOrDefaultAsync();

        int nextNum = 1;
        if (!string.IsNullOrEmpty(lastToken) && int.TryParse(lastToken.Substring(3), out int num))
            nextNum = num + 1;

        record.UniqueToken = $"REF{nextNum:D6}";
        record.Status = "Approved";
        record.ApproveComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        record.ApprovedAt = DateTime.UtcNow;
        record.ApprovedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        record.ApprovedByName = User.Identity?.Name;
        record.ApprovedByRole = User?.IsInRole("SuperAdmin") == true ? "SuperAdmin"
            : User?.IsInRole("Admin") == true ? "Admin"
            : "FinalApprover";

        _db.VehicleRegistrations.Update(record);
        await _db.SaveChangesAsync();
        TempData["ok"] = $"Registration approved. Token: {record.UniqueToken}";
        return RedirectToAction(nameof(List));
    }

    return Forbid();
}


        [HttpPost]
        [Authorize(Roles = "SuperAdmin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Hide(long id)
        {
            var rec = await _db.VehicleRegistrations.FindAsync(id);
            if (rec == null)
                return NotFound();
            if (rec.Status == "Hidden")
            {
                TempData["ok"] = "Record is already hidden.";
                return RedirectToAction(nameof(ListAll));
            }
            rec.PreviousStatus = rec.Status;
            rec.Status = "Hidden";
            _db.VehicleRegistrations.Update(rec);
            await _db.SaveChangesAsync();
            TempData["ok"] = "Record hidden. Only SuperAdmin can view it.";
            return RedirectToAction(nameof(ListAll));
        }

        [HttpPost]
        [Authorize(Roles = "Admin,SuperAdmin,DocumentVerifier,FinalApprover")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(long id, string? reason)
        {
            var record = await _db.VehicleRegistrations.FindAsync(id);
            if (record == null)
                return NotFound();
            if (string.Equals(record.Status, "Hidden", StringComparison.OrdinalIgnoreCase))
            {
                TempData["err"] = "This record is hidden and cannot be modified.";
                return RedirectToAction(nameof(List));
            }
            if (string.IsNullOrWhiteSpace(reason))
            {
                TempData["err"] = "Rejection reason is required.";
                return RedirectToAction(nameof(List));
            }
            record.Status = "Rejected";
            record.RejectReason = reason.Trim();
            record.RejectedAt = DateTime.UtcNow;
            record.RejectedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            record.RejectedByName = User.Identity?.Name;
            // Derive primary role label for audit
            record.RejectedByRole = User?.IsInRole("SuperAdmin") == true ? "SuperAdmin"
                : User?.IsInRole("Admin") == true ? "Admin"
                : User?.IsInRole("FinalApprover") == true ? "FinalApprover"
                : User?.IsInRole("DocumentVerifier") == true ? "DocumentVerifier"
                : User?.IsInRole("MinistryOfficer") == true ? "MinistryOfficer"
                : User?.IsInRole("Owner") == true ? "Owner"
                : null;
            _db.VehicleRegistrations.Update(record);
            await _db.SaveChangesAsync();

            TempData["ok"] = "Vehicle registration rejected.";
            return RedirectToAction(nameof(List));
        }

        private string? MapWebPathToPhysical(string? webPath)
        {
            if (string.IsNullOrWhiteSpace(webPath)) return null;
            // webPath like: "/uploads/tank/abc.jpg"
            var relative = webPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(_env.WebRootPath ?? string.Empty, relative);
        }

        private IEnumerable<(string Display, string Key, string? WebPath)> GetDocumentList(MT.Data.VehicleRegistration v)
        {
            if (v.VehicleType?.Equals("tank", StringComparison.OrdinalIgnoreCase) == true)
            {
                yield return ("Double-sided ID card", "IdCardBothSides", v.IdCardBothSidesPath);
                yield return ("Tanker application form (both sides)", "TankerFormBothSides", v.TankerFormBothSidesPath);
                yield return ("IBAN certificate from bank", "IbanCertificate", v.IbanCertificatePath);
                yield return ("Tank capacity certificate", "TankCapacityCert", v.TankCapacityCertPath);
                yield return ("Dumping landfill (works)", "LandfillWorks", v.LandfillWorksPath);
                yield return ("Signed vehicle registration form", "SignedRegistrationForm", v.SignedRegistrationFormPath);
                yield return ("Release form", "ReleaseForm", v.ReleaseFormPath);
            }
            else // truck
            {
                yield return ("Double-sided ID card", "Truck_IdCard", v.Truck_IdCardPath);
                yield return ("Locomotive & trailer registration (valid)", "Truck_TrailerRegistration", v.Truck_TrailerRegistrationPath);
                yield return ("Traffic department certificate", "Truck_TrafficCertificate", v.Truck_TrafficCertificatePath);
                yield return ("IBAN certificate from bank", "Truck_IbanCertificate", v.Truck_IbanCertificatePath);
                yield return ("Signed vehicle registration application form", "Truck_VehicleRegForm", v.Truck_VehicleRegFormPath);
                yield return ("Release form", "Truck_ReleaseForm", v.Truck_ReleaseFormPath);
            }
        }
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> DownloadAll(long id)
        {
            var v = await _db.VehicleRegistrations.SingleOrDefaultAsync(x => x.Id == id);
            if (v == null) return NotFound();
            if (string.Equals(v.Status, "Hidden", StringComparison.OrdinalIgnoreCase) && !(User?.IsInRole("SuperAdmin") ?? false))
                return NotFound();
            if (User?.IsInRole("Owner") == true)
            {
                var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var ownerPhone = await _db.UserProfiles.Where(p => p.UserId == uid).Select(p => p.Phone).FirstOrDefaultAsync();
                if (string.IsNullOrWhiteSpace(ownerPhone))
                {
                    ownerPhone = await _db.Users.Where(u => u.Id == uid).Select(u => u.PhoneNumber).FirstOrDefaultAsync();
                }
                if (string.IsNullOrWhiteSpace(ownerPhone) || !string.Equals(ownerPhone, v.OwnerPhone, StringComparison.Ordinal))
                    return Forbid();
            }

            // Build a temp zip in %TEMP%
            var zipFile = Path.Combine(Path.GetTempPath(), $"VehicleDocs_{id}_{DateTime.UtcNow:yyyyMMddHHmmss}.zip");
            if (System.IO.File.Exists(zipFile)) System.IO.File.Delete(zipFile);

            using (var zip = ZipFile.Open(zipFile, ZipArchiveMode.Create))
            {
                foreach (var (display, key, webPath) in GetDocumentList(v))
                {
                    var phys = MapWebPathToPhysical(webPath);
                    if (!string.IsNullOrWhiteSpace(phys) && System.IO.File.Exists(phys))
                    {
                        // Use a nice file name inside the zip
                        var ext = Path.GetExtension(phys);
                        var entryName = $"{key}{ext}";
                        zip.CreateEntryFromFile(phys, entryName, CompressionLevel.Optimal);
                    }
                }
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(zipFile);
            return File(bytes, "application/zip", "VehicleDocuments.zip");
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ExportPdf(long id)
        {
            var v = await _db.VehicleRegistrations.SingleOrDefaultAsync(x => x.Id == id);
            if (v == null) return NotFound();
            if (string.Equals(v.Status, "Hidden", StringComparison.OrdinalIgnoreCase) && !(User?.IsInRole("SuperAdmin") ?? false))
                return NotFound();
            if (User?.IsInRole("Owner") == true)
            {
                var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var ownerPhone = await _db.UserProfiles.Where(p => p.UserId == uid).Select(p => p.Phone).FirstOrDefaultAsync();
                if (string.IsNullOrWhiteSpace(ownerPhone))
                {
                    ownerPhone = await _db.Users.Where(u => u.Id == uid).Select(u => u.PhoneNumber).FirstOrDefaultAsync();
                }
                if (string.IsNullOrWhiteSpace(ownerPhone) || !string.Equals(ownerPhone, v.OwnerPhone, StringComparison.Ordinal))
                    return Forbid();
            }

            var docList = GetDocumentList(v).ToList();
            QuestPDF.Settings.License = LicenseType.Community;

            // Localization helpers
            var ui = System.Globalization.CultureInfo.CurrentUICulture;
            var qCulture = (Request?.Query["culture"].ToString() ?? string.Empty).ToLowerInvariant();
            var qUi = (Request?.Query["ui_culture"].ToString() ?? string.Empty).ToLowerInvariant();
            var qLang = (Request?.Query["lang"].ToString() ?? string.Empty).ToLowerInvariant();
            bool isAr = qCulture == "ar" || qUi == "ar" || qLang == "ar" || ui.TextInfo.IsRightToLeft || ui.TwoLetterISOLanguageName == "ar";
            string T(string en, string ar) => isAr ? ar : en;

            var lblTitle = T("Vehicle Registration Details", "تفاصيل تسجيل المركبة");
            var lblOwnerPhone = T("Owner's Phone", "رقم هاتف المالك");
            var lblOwnerName = T("Owner Name:", "اسم المالك:");
            var lblDriverPhone = T("Driver's Phone", "رقم هاتف السائق");
            var lblDriverName = T("Driver Name:", "اسم السائق:");
            var lblVehicleType = T("Vehicle Type", "نوع المركبة");
            var lblStatus = T("Status:", "الحالة:");
            var lblSubmitted = T("Submitted:", "تاريخ التقديم:");
            var lblDocuments = T("Documents required for trucks", "المستندات المطلوبة للشاحنات");
            if (string.Equals(v.VehicleType, "tank", StringComparison.OrdinalIgnoreCase))
                lblDocuments = T("Documents required for tanks", "المستندات المطلوبة للصهاريج");
            var lblNo = T("#", "#");
            var lblName = T("Name", "الاسم");
            var lblDocStatus = T("Status", "الحالة");
            var lblUploaded = T("Uploaded", "تم الرفع");
            var lblNotUploaded = T("Not uploaded", "غير مرفوع");
            var lblReference = T("Reference:", "المرجع:");
            var lblToken = T("Token:", "الرمز:");
            var lblSubtitle = T(
                "Below are all details and uploaded documents for this vehicle registration.",
                "هنا جميع التفاصيل والوثائق المرفوعة لهذا التسجيل.");
            var lblOwnerDriverTitle = T("Owner and Driver Information", "معلومات المالك والسائق");
            var lblApprovedBadge = T("Approved", "تمت الموافقة");
            var lblRejectedBadge = T("Rejected", "مرفوض");
            var lblUnderReviewBadge = T("Under Review", "قيد المراجعة");

            // Logo (optional)
            byte[]? logoBytes = null;
            try
            {
                var logoPath = Path.Combine(_env.WebRootPath ?? string.Empty, "img", "logo.png");
                if (System.IO.File.Exists(logoPath))
                    logoBytes = await System.IO.File.ReadAllBytesAsync(logoPath);
            }
            catch { /* ignore logo errors */ }

            var pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2f, Unit.Centimetre);
                    page.DefaultTextStyle(t => t.FontSize(12));

                    // Watermark based on status
                    var watermarkText = v.Status == "Approved" ? lblApprovedBadge : v.Status == "Rejected" ? lblRejectedBadge : lblUnderReviewBadge;
                    // Use light color to simulate translucency
                    page.Background().AlignCenter().AlignMiddle().Text(watermarkText).SemiBold().FontSize(120).FontColor(Colors.Grey.Lighten2);
                    page.Header().Row(row =>
                    {
                        if (logoBytes != null)
                            row.ConstantItem(60).Image(logoBytes);
                        row.RelativeItem().AlignRight().Text(lblTitle).SemiBold().FontSize(22);
                    });

                    page.Content().Column(col =>
                    {
                        // Subtitle and Reference
                        col.Item().Text(lblSubtitle).FontColor(Colors.Grey.Darken2);
                        col.Item().Row(rr =>
                        {
                            rr.AutoItem().Text(lblReference + " ").SemiBold();
                            rr.AutoItem().Container()
                                .Background(Colors.Grey.Lighten3)
                                .Padding(4)
                                .Text($"APP{v.Id:D6}")
                                .FontColor(Colors.Grey.Darken3);
                        });

                        // Owner & Driver card
                        col.Item().PaddingTop(10);
                        col.Item().Text(lblOwnerDriverTitle).AlignCenter().SemiBold();
                        col.Item().Padding(6).Border(1).BorderColor(Colors.Grey.Lighten2).Column(inner =>
                        {
                            inner.Item().Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.RelativeColumn(3);
                                    c.RelativeColumn(4);
                                    c.RelativeColumn(3);
                                    c.RelativeColumn(4);
                                });
                                t.Cell().Text(lblOwnerPhone).FontColor(Colors.Grey.Darken2);
                                t.Cell().Text(v.OwnerPhone ?? "-").SemiBold();
                                t.Cell().Text(lblOwnerName).FontColor(Colors.Grey.Darken2);
                                t.Cell().Text(v.VehicleOwnerName ?? "-").SemiBold();

                                t.Cell().Text(lblDriverPhone).FontColor(Colors.Grey.Darken2);
                                t.Cell().Text(v.DriverPhone ?? "-").SemiBold();
                                t.Cell().Text(lblDriverName).FontColor(Colors.Grey.Darken2);
                                t.Cell().Text(v.DriverName ?? "-").SemiBold();

                                t.Cell().Text(lblVehicleType).FontColor(Colors.Grey.Darken2);
                                t.Cell().Text(v.VehicleType ?? "-").SemiBold();
                                t.Cell().Text("");
                                t.Cell().Text("");
                            });
                        });

                        // Status card
                        col.Item().PaddingTop(10);
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Text(T("Status", "الحالة")).SemiBold();
                            r.ConstantItem(140).AlignRight().Container().AlignRight().Background(
                                v.Status == "Approved" ? Colors.Green.Darken2 : v.Status == "Rejected" ? Colors.Red.Darken2 : Colors.Orange.Darken2
                            ).PaddingVertical(4).PaddingHorizontal(8).Text(
                                v.Status == "Approved" ? lblApprovedBadge : v.Status == "Rejected" ? lblRejectedBadge : lblUnderReviewBadge
                            ).FontColor(Colors.White);
                        });
                        col.Item().Padding(6).Border(1).BorderColor(Colors.Grey.Lighten2).Column(inner =>
                        {
                            inner.Item().Text(txt => { txt.Span(lblSubmitted + " ").SemiBold(); txt.Span(v.SubmittedDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm")); });
                            if (!string.IsNullOrWhiteSpace(v.ApprovedByName))
                                inner.Item().Text(txt => { txt.Span(T("Approved By:", "تمت الموافقة بواسطة:") + " ").SemiBold(); txt.Span($"{v.ApprovedByName}{(string.IsNullOrWhiteSpace(v.ApprovedByRole)?"":$" ({v.ApprovedByRole})")}"); });
                            if (v.ApprovedAt.HasValue)
                                inner.Item().Text(txt => { txt.Span(T("When:", "التاريخ:") + " ").SemiBold(); txt.Span(v.ApprovedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")); });
                            if (!string.IsNullOrWhiteSpace(v.ApproveComment))
                                inner.Item().Text(txt => { txt.Span(T("Comment:", "ملاحظة:") + " ").SemiBold(); txt.Span(v.ApproveComment); });

                            // If rejected, show rejection info
                            if (string.Equals(v.Status, "Rejected", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!string.IsNullOrWhiteSpace(v.RejectReason))
                                    inner.Item().Text(txt => { txt.Span(T("Reason:", "السبب:") + " ").SemiBold(); txt.Span(v.RejectReason); });
                                if (v.RejectedAt.HasValue)
                                    inner.Item().Text(txt => { txt.Span(T("Rejected At:", "تاريخ الرفض:") + " ").SemiBold(); txt.Span(v.RejectedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")); });
                            }
                        });

                        // Documents header
                        col.Item().PaddingTop(15);
                        col.Item().Text(lblDocuments).SemiBold().FontSize(14);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(7);
                                columns.RelativeColumn(3);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Text(lblNo).SemiBold();
                                header.Cell().Text(lblName).SemiBold();
                                header.Cell().Text(lblDocStatus).SemiBold();
                            });

                            for (int i = 0; i < docList.Count; i++)
                            {
                                var d = docList[i];
                                var isUploaded = !string.IsNullOrWhiteSpace(d.WebPath);
                                table.Cell().Text((i + 1).ToString());
                                table.Cell().Text(d.Display);
                                table.Cell().Text(isUploaded ? lblUploaded : lblNotUploaded)
                                      .FontColor(isUploaded ? Colors.Green.Darken2 : Colors.Grey.Darken2);
                            }
                        });
                    });

                    // Footer with QR (left) and ref/token (right)
                    bool showToken = string.Equals(v.Status, "Approved", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(v.UniqueToken);
                    string? baseUrl = null;
                    byte[]? qrBytes = null;
                    if (showToken)
                    {
                        baseUrl = Url.Action("DetailsByToken", "Vehicle", new { token = v.UniqueToken, culture = (isAr?"ar":"en"), ui_culture = (isAr?"ar":"en") }, Request.Scheme) ?? ("/Vehicle/Details/" + v.UniqueToken);
                        try
                        {
                            var qrGen = new QRCodeGenerator();
                            using var qrData = qrGen.CreateQrCode(baseUrl, QRCodeGenerator.ECCLevel.Q);
                            var qrPng = new PngByteQRCode(qrData);
                            qrBytes = qrPng.GetGraphic(5);
                        }
                        catch { }
                    }
                    page.Footer().Row(fr =>
                    {
                        fr.ConstantItem(80).Column(cc =>
                        {
                            if (showToken && qrBytes != null)
                            {
                                cc.Item().Text(T("Scan", "امسح")).FontSize(9).FontColor(Colors.Grey.Darken2);
                                cc.Item().Image(qrBytes);
                            }
                        });
                        fr.RelativeItem().AlignRight().Text(txt =>
                        {
                            txt.Span(lblReference + " ").SemiBold();
                            txt.Span($"APP{v.Id:D6}   ");
                            if (showToken)
                            {
                                txt.Span(lblToken + " ").SemiBold();
                                txt.Span(v.UniqueToken!);
                            }
                        });
                    });
                });
            }).GeneratePdf();

            var safeStatus = (v.Status ?? "").Replace(' ', '_');
            var fileName = $"Application_APP{v.Id:D6}_{safeStatus}_{DateTime.Now:yyyyMMdd-HHmm}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }

        [HttpGet]
        [AllowAnonymous] // or restrict as needed
        public async Task<IActionResult> DownloadFile(long id, string file)
        {
            if (string.IsNullOrWhiteSpace(file))
                return BadRequest("Missing file key.");

            var reg = await _db.VehicleRegistrations.SingleOrDefaultAsync(x => x.Id == id);
            if (reg == null)
                return NotFound("Registration not found.");
            if (string.Equals(reg.Status, "Hidden", StringComparison.OrdinalIgnoreCase) && !(User?.IsInRole("SuperAdmin") ?? false))
                return NotFound();
            if (User?.IsInRole("Owner") == true)
            {
                var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var ownerPhone = await _db.UserProfiles.Where(p => p.UserId == uid).Select(p => p.Phone).FirstOrDefaultAsync();
                if (string.IsNullOrWhiteSpace(ownerPhone))
                {
                    ownerPhone = await _db.Users.Where(u => u.Id == uid).Select(u => u.PhoneNumber).FirstOrDefaultAsync();
                }
                if (string.IsNullOrWhiteSpace(ownerPhone) || !string.Equals(ownerPhone, reg.OwnerPhone, StringComparison.Ordinal))
                    return Forbid();
            }

            var map = GetKeyMap(reg);
            if (!map.TryGetValue(file, out var meta))
                return NotFound("Invalid document key for this vehicle type.");

            var webPath = meta.GetPath(reg);
            if (string.IsNullOrWhiteSpace(webPath))
                return NotFound("This document was not uploaded.");

            var phys = MapWebPathToPhysical(webPath);
            if (string.IsNullOrWhiteSpace(phys) || !System.IO.File.Exists(phys))
                return NotFound("File not found on server.");

            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(phys, out var contentType))
                contentType = "application/octet-stream";

            var downloadName = GetDownloadName(meta.Display, phys);
            var fileBytes = await System.IO.File.ReadAllBytesAsync(phys);
            return File(fileBytes, contentType, downloadName);
        }

        

        // Returns a dictionary of allowed keys -> (displayName, webPathGetter)
        private Dictionary<string, (string Display, Func<MT.Data.VehicleRegistration, string?> GetPath)> GetKeyMap(MT.Data.VehicleRegistration v)
        {
            if (v.VehicleType?.Equals("tank", StringComparison.OrdinalIgnoreCase) == true)
            {
                return new(StringComparer.OrdinalIgnoreCase)
                {
                    ["IdCardBothSides"] = ("Double-sided ID card", x => x.IdCardBothSidesPath),
                    ["TankerFormBothSides"] = ("Tanker application form (both sides)", x => x.TankerFormBothSidesPath),
                    ["IbanCertificate"] = ("IBAN certificate from bank", x => x.IbanCertificatePath),
                    ["TankCapacityCert"] = ("Tank capacity certificate", x => x.TankCapacityCertPath),
                    ["LandfillWorks"] = ("Dumping landfill (works)", x => x.LandfillWorksPath),
                    ["SignedRegistrationForm"] = ("Signed vehicle registration form", x => x.SignedRegistrationFormPath),
                    ["ReleaseForm"] = ("Release form", x => x.ReleaseFormPath),
                };
            }

            // truck
            return new(StringComparer.OrdinalIgnoreCase)
            {
                ["Truck_IdCard"] = ("Double-sided ID card", x => x.Truck_IdCardPath),
                ["Truck_TrailerRegistration"] = ("Locomotive & trailer registration (valid)", x => x.Truck_TrailerRegistrationPath),
                ["Truck_TrafficCertificate"] = ("Traffic department certificate", x => x.Truck_TrafficCertificatePath),
                ["Truck_IbanCertificate"] = ("IBAN certificate from bank", x => x.Truck_IbanCertificatePath),
                ["Truck_VehicleRegForm"] = ("Signed vehicle registration application form", x => x.Truck_VehicleRegFormPath),
                ["Truck_ReleaseForm"] = ("Release form", x => x.Truck_ReleaseFormPath),
            };
        }

        private static string GetDownloadName(string display, string physicalPath)
        {
            var ext = Path.GetExtension(physicalPath);
            // filename like: "Double-sided ID card.jpg"
            return $"{display}{ext}";
        }

    }


}
    public class VehicleRegisterVm
    {
        [Required]
        public string VehicleType { get; set; } = null!;        // "truck" | "tank"
        [Required]
        public string OwnerPhone { get; set; } = null!;
        [Required]
        public string VehicleOwnerName { get; set; } = null!;
        [Required]
        public string DriverPhone { get; set; } = null!;
        [Required]
        public string DriverName { get; set; } = null!;
    
        public string? VehicleNumber { get; set; }
    // uploads (nullable to support alternative vehicle types and optional fields)
        public IFormFile? IdCardBothSides { get; set; }          // #1
        public IFormFile? TankerFormBothSides { get; set; }      // #2
        public IFormFile? IbanCertificate { get; set; }          // #3
        public IFormFile? TankCapacityCert { get; set; }         // #4
        public IFormFile? LandfillWorks { get; set; }            // #5
        public IFormFile? SignedRegistrationForm { get; set; }   // #6
        public IFormFile? ReleaseForm { get; set; }              // #7

        // ===== Truck documents =====
        public IFormFile? Truck_IdCard { get; set; }
        public IFormFile? Truck_TrailerRegistration { get; set; }
        public IFormFile? Truck_TrafficCertificate { get; set; }
        public IFormFile? Truck_IbanCertificate { get; set; }
        public IFormFile? Truck_VehicleRegForm { get; set; }
        public IFormFile? Truck_ReleaseForm { get; set; }

}
