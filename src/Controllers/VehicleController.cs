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

namespace MT.Controllers
{
    public class VehicleController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
       
        public VehicleController(ApplicationDbContext db, IWebHostEnvironment env)
        {
            _db = db; // <— this should NOT be null
            _env = env;
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Register(string lang = "en")
        {
            ViewBag.Lang = (lang == "ar" ? "ar" : "en");
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
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
                var entity = new VehicleRegistration
                {
                    VehicleType = model.VehicleType,
                    OwnerPhone = model.OwnerPhone,
                    VehicleOwnerName = model.VehicleOwnerName,
                    DriverPhone = model.DriverPhone,
                    DriverName = model.DriverName,
                    ClientIP = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    Status = "Pending",
                    SubmittedDate = DateTime.UtcNow
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

                _db.VehicleRegistrations.Add(entity);
                await _db.SaveChangesAsync();

                TempData["ok"] = lang == "ar" ? "تم الإرسال بنجاح" : "Submitted successfully";
                return RedirectToAction(nameof(Register), new { lang });
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


        [Authorize(Roles = "Admin,SuperAdmin,DocumentVerifier,FinalApprover,MinistryOfficer,Owner")]
        public async Task<IActionResult> List(string type = "all", int page = 1, int pageSize = 50)
        {
            var query = _db.VehicleRegistrations.AsQueryable();
            // Owner: only their own data (matched by OwnerPhone from their profile)
            if (User?.IsInRole("Owner") == true)
            {
                var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var ownerPhone = await _db.UserProfiles
                    .Where(p => p.UserId == uid)
                    .Select(p => p.Phone)
                    .FirstOrDefaultAsync();
                if (string.IsNullOrWhiteSpace(ownerPhone))
                {
                    // fall back to Identity PhoneNumber if profile missing phone
                    ownerPhone = await _db.Users
                        .Where(u => u.Id == uid)
                        .Select(u => u.PhoneNumber)
                        .FirstOrDefaultAsync();
                }
                if (!string.IsNullOrWhiteSpace(ownerPhone))
                    query = query.Where(x => x.OwnerPhone == ownerPhone);
                else
                    query = query.Where(x => false); // no profile phone -> show none
            }
            // MinistryOfficer can only see verified (Approved) records
            else if (User?.IsInRole("MinistryOfficer") == true)
            {
                query = query.Where(x => x.Status == "Approved");
            }
            
            // Filter by vehicle type if specified
            if (!string.IsNullOrEmpty(type) && type != "all")
            {
                query = query.Where(x => x.VehicleType == type);
            }
            
            var items = await query
                .OrderByDescending(x => x.SubmittedDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
                
            // Pass counts for each type to the view (respect role filtering)
            var baseCountQuery = _db.VehicleRegistrations.AsQueryable();
            if (User?.IsInRole("Owner") == true)
            {
                var uid2 = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var ownerPhone2 = await _db.UserProfiles
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
                    baseCountQuery = baseCountQuery.Where(x => x.OwnerPhone == ownerPhone2);
                else
                    baseCountQuery = baseCountQuery.Where(x => false);
            }
            else if (User?.IsInRole("MinistryOfficer") == true)
                baseCountQuery = baseCountQuery.Where(x => x.Status == "Approved");

            ViewBag.TruckCount = await baseCountQuery.CountAsync(x => x.VehicleType == "truck");
            ViewBag.TankCount = await baseCountQuery.CountAsync(x => x.VehicleType == "tank");
            ViewBag.CurrentType = type;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;

            return View(items);
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Details(int id, string lang = "en")
        {
            ViewBag.Lang = (lang == "ar" ? "ar" : "en");
            var items = _db.VehicleRegistrations.Where(x => x.Id == id).SingleOrDefault();
            if (items == null) return NotFound();
            // Owners can only view their own
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
                if (string.IsNullOrWhiteSpace(ownerPhone) || !string.Equals(ownerPhone, items.OwnerPhone, StringComparison.Ordinal))
                    return Forbid();
            }
            return View(items);
        }


        [HttpPost]
        [Authorize(Roles = "Admin,SuperAdmin,DocumentVerifier,FinalApprover")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(long id)
        {
            var record = await _db.VehicleRegistrations.FindAsync(id);
            if (record == null)
                return NotFound();

            // If verifier approves -> move to Under Review
            if (User?.IsInRole("DocumentVerifier") == true)
            {
                if (record.Status == "Approved")
                {
                    TempData["ok"] = "This registration is already approved.";
                    return RedirectToAction(nameof(List));
                }
                record.Status = "Under Review";
                _db.VehicleRegistrations.Update(record);
                await _db.SaveChangesAsync();
                TempData["ok"] = "Registration moved to Under Review.";
                return RedirectToAction(nameof(List));
            }

            // Final approver or admin path -> Approved + token
            if (User?.IsInRole("Admin") == true || User?.IsInRole("SuperAdmin") == true || User?.IsInRole("FinalApprover") == true)
            {
                if (record.Status == "Approved")
                {
                    TempData["ok"] = "This registration is already approved.";
                    return RedirectToAction(nameof(List));
                }

                // Generate next unique token
                var lastToken = await _db.VehicleRegistrations
                    .Where(v => v.UniqueToken != null)
                    .OrderByDescending(v => v.UniqueToken)
                    .Select(v => v.UniqueToken)
                    .FirstOrDefaultAsync();

                int nextNum = 1;
                if (!string.IsNullOrEmpty(lastToken) && lastToken.StartsWith("REF"))
                {
                    if (int.TryParse(lastToken.Substring(3), out int num))
                        nextNum = num + 1;
                }

                record.UniqueToken = $"REF{nextNum:D6}";
                record.Status = "Approved";

                _db.VehicleRegistrations.Update(record);
                await _db.SaveChangesAsync();
                TempData["ok"] = $"Registration approved. Token: {record.UniqueToken}";
                return RedirectToAction(nameof(List));
            }

            return Forbid();
        }


        [HttpPost]
        [Authorize(Roles = "Admin,SuperAdmin,DocumentVerifier,FinalApprover")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(long id, string? reason)
        {
            var record = await _db.VehicleRegistrations.FindAsync(id);
            if (record == null)
                return NotFound();
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

            var sb = new StringBuilder();
            sb.AppendLine("<html><head><meta charset='utf-8'><style>");
            sb.AppendLine("body{font-family:Arial,Helvetica,sans-serif;font-size:12pt;color:#222}");
            sb.AppendLine("h1{font-size:18pt;margin:0 0 10px 0} h2{font-size:14pt;margin:16px 0 8px 0}");
            sb.AppendLine("table{width:100%;border-collapse:collapse} th,td{border:1px solid #ddd;padding:8px}");
            sb.AppendLine("th{background:#f8f8f8;text-align:left}");
            sb.AppendLine("</style></head><body>");

            sb.AppendLine("<h1>Vehicle Registration Details</h1>");
            sb.AppendLine("<h2>Owner & Driver</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine($"<tr><th>Owner Phone</th><td>{System.Net.WebUtility.HtmlEncode(v.OwnerPhone ?? "—")}</td></tr>");
            sb.AppendLine($"<tr><th>Owner Name</th><td>{System.Net.WebUtility.HtmlEncode(v.VehicleOwnerName ?? "—")}</td></tr>");
            sb.AppendLine($"<tr><th>Driver Phone</th><td>{System.Net.WebUtility.HtmlEncode(v.DriverPhone ?? "—")}</td></tr>");
            sb.AppendLine($"<tr><th>Driver Name</th><td>{System.Net.WebUtility.HtmlEncode(v.DriverName ?? "—")}</td></tr>");
            sb.AppendLine($"<tr><th>Vehicle Type</th><td>{System.Net.WebUtility.HtmlEncode(v.VehicleType ?? "—")}</td></tr>");
            sb.AppendLine($"<tr><th>Status</th><td>{System.Net.WebUtility.HtmlEncode(v.Status ?? "—")}</td></tr>");
            sb.AppendLine($"<tr><th>Submitted</th><td>{v.SubmittedDate:yyyy-MM-dd HH:mm} UTC</td></tr>");
            sb.AppendLine("</table>");

            sb.AppendLine("<h2>Documents</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr><th>#</th><th>Name</th><th>Status</th></tr></thead><tbody>");

            int idx = 1;
            foreach (var (display, key, webPath) in GetDocumentList(v))
            {
                bool uploaded = !string.IsNullOrWhiteSpace(webPath);
                sb.AppendLine($"<tr><td>{idx++}</td><td>{System.Net.WebUtility.HtmlEncode(display)}</td><td>{(uploaded ? "Uploaded" : "Not uploaded")}</td></tr>");
            }

            sb.AppendLine("</tbody></table>");
            sb.AppendLine("</body></html>");

            // OPTION A: return HTML (for debugging, or if you use a wkhtmltopdf/DinkToPdf instance elsewhere)
            var htmlBytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(htmlBytes, "text/html; charset=utf-8", "VehicleDetails.html");

            // OPTION B (replace above with your PDF tool):
            // var pdfBytes = _yourPdfLib.ConvertHtmlToPdf(sb.ToString());
            // return File(pdfBytes, "application/pdf", "VehicleDetails.pdf");
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
