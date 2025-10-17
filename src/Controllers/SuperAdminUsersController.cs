using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT.Data;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MT.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    public class SuperAdminUsersController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        private static readonly string[] AllowedRoles = new[]
        {
            "Admin","DocumentVerifier","FinalApprover","MinistryOfficer","Owner","VehicleOwner"
        };

        private async Task EnsureRolesExistAsync()
        {
            foreach (var r in AllowedRoles)
            {
                if (await _roleManager.FindByNameAsync(r) == null)
                {
                    await _roleManager.CreateAsync(new IdentityRole(r));
                }
            }
        }

        public SuperAdminUsersController(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            RoleManager<IdentityRole> roleManager)
        {
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        private static string DigitsOnly(string? s) => new string((s ?? string.Empty).Where(char.IsDigit).ToArray());
        private static string NormalizePhone(string? s)
        {
            var d = DigitsOnly(s);
            if (d.StartsWith("974") && d.Length > 3)
            {
                var core = d.Substring(3);
                if (core.Length > 8) core = core.Substring(0, 8);
                return "974" + core; // store as 974 + 8 digits
            }
            return d; // fallback: just digits
        }
        private static string NormalizeQid(string? s)
        {
            // Keep digits only; enforce max length 11 (exact length validated by annotations)
            var d = DigitsOnly(s);
            if (d.Length > 11) d = d.Substring(0, 11);
            return d;
        }

        // GET: /SuperAdminUsers
        public async Task<IActionResult> Index()
        {
            await EnsureRolesExistAsync();
            var profiles = await _db.UserProfiles
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            // prefetch roles
            var roleMap = await _roleManager.Roles.ToDictionaryAsync(r => r.Id, r => r.Name ?? r.NormalizedName);

            var vm = profiles.Select(p => new SuperUserListVm
            {
                Id = p.Id,
                Name = p.Name,
                Email = p.Email,
                Phone = p.Phone,
                QID = p.QID,
                RoleName = roleMap.ContainsKey(p.RoleId) ? (roleMap[p.RoleId] ?? "") : "",
                Username = p.Username,
                IsActive = p.IsActive,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            }).ToList();

            ViewBag.AllowedRoles = AllowedRoles;
            return View(vm);
        }

        // GET: /SuperAdminUsers/Create
        public IActionResult Create()
        {
            ViewBag.Roles = new SelectList(AllowedRoles);
            return View(new SuperUserCreateVm());
        }

        // POST: /SuperAdminUsers/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SuperUserCreateVm vm)
        {
            ViewBag.Roles = new SelectList(AllowedRoles);
            if (!AllowedRoles.Contains(vm.Role))
                ModelState.AddModelError(nameof(vm.Role), "Invalid role selected.");

            // First, honor required/format validations
            if (!ModelState.IsValid)
                return View(vm);

            // Server-side uniqueness validation (guard against null/empty)
            if (!string.IsNullOrWhiteSpace(vm.Email))
            {
                var normEmail = vm.Email.ToUpper();
                if (await _userManager.Users.AnyAsync(u => u.NormalizedEmail == normEmail))
                    ModelState.AddModelError(nameof(vm.Email), "Email already exists.");
            }
            if (!string.IsNullOrWhiteSpace(vm.Username))
            {
                var normUser = vm.Username.ToUpper();
                if (await _userManager.Users.AnyAsync(u => u.NormalizedUserName == normUser))
                    ModelState.AddModelError(nameof(vm.Username), "Username already exists.");
            }

            if (!ModelState.IsValid)
                return View(vm);

            // Ensure role exists
            if (!await _roleManager.RoleExistsAsync(vm.Role))
                await _roleManager.CreateAsync(new IdentityRole(vm.Role));

            // Normalize and validate phone uniqueness
            var normalizedPhone = NormalizePhone(vm.Phone);
            var normalizedQid = NormalizeQid(vm.QID);
            if (!string.IsNullOrWhiteSpace(normalizedPhone))
            {
                var phoneInUsers = await _userManager.Users.AnyAsync(u => u.PhoneNumber == normalizedPhone);
                var phoneInProfiles = await _db.UserProfiles.AnyAsync(p => p.Phone == normalizedPhone);
                if (phoneInUsers || phoneInProfiles)
                {
                    ModelState.AddModelError(nameof(vm.Phone), "Phone number already exists.");
                }
            }
            if (!string.IsNullOrWhiteSpace(normalizedQid))
            {
                var qidExists = await _db.UserProfiles.AnyAsync(p => p.QID == normalizedQid);
                if (qidExists)
                {
                    ModelState.AddModelError(nameof(vm.QID), "QID already exists.");
                }
            }
            if (!ModelState.IsValid)
                return View(vm);

            // Create Identity user
            
            var user = new IdentityUser { UserName = vm.Username, Email = vm.Email, EmailConfirmed = true, PhoneNumber = normalizedPhone };
            var createRes = await _userManager.CreateAsync(user, vm.Password);
            if (!createRes.Succeeded)
            {
                foreach (var e in createRes.Errors) ModelState.AddModelError(string.Empty, e.Description);
                return View(vm);
            }

            // Assign role
            await _userManager.AddToRoleAsync(user, vm.Role);

            // Create profile
            var roleId = await _roleManager.Roles.Where(r => r.Name == vm.Role).Select(r => r.Id).FirstAsync();
            var profile = new UserProfile
            {
                Name = vm.Name,
                Email = vm.Email,
                Phone = normalizedPhone,
                QID = string.IsNullOrWhiteSpace(normalizedQid) ? null : normalizedQid,
                UserId = user.Id,
                RoleId = roleId,
                Username = vm.Username,
                IsActive = vm.IsActive,
                CreatedBy = User?.Identity?.Name,
                CreatedAt = DateTime.UtcNow
            };
            _db.UserProfiles.Add(profile);
            await _db.SaveChangesAsync();

            TempData["ok"] = "User created.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /SuperAdminUsers/Edit/{id}
        public async Task<IActionResult> Edit(long id)
        {
            await EnsureRolesExistAsync();
            var profile = await _db.UserProfiles.FindAsync(id);
            if (profile == null) return NotFound();
            var role = await _roleManager.FindByIdAsync(profile.RoleId);
            var vm = new SuperUserEditVm
            {
                Id = profile.Id,
                Name = profile.Name,
                Email = profile.Email,
                Phone = profile.Phone,
                QID = profile.QID,
                Username = profile.Username,
                Role = role?.Name ?? string.Empty,
                IsActive = profile.IsActive
            };
            ViewBag.Roles = new SelectList(AllowedRoles);
            return View(vm);
        }

        // POST: /SuperAdminUsers/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(SuperUserEditVm vm)
        {
            ViewBag.Roles = new SelectList(AllowedRoles);
            if (!AllowedRoles.Contains(vm.Role))
                ModelState.AddModelError(nameof(vm.Role), "Invalid role selected.");
            
            var profile = await _db.UserProfiles.FindAsync(vm.Id);
            if (profile == null) return NotFound();

            // First, honor required/format validations
            if (!ModelState.IsValid) return View(vm);

            // Uniqueness checks excluding current user (guard against null/empty)
            if (!string.IsNullOrWhiteSpace(vm.Email))
            {
                var normEmail = vm.Email.ToUpper();
                if (await _userManager.Users.AnyAsync(u => u.NormalizedEmail == normEmail && u.Id != profile.UserId))
                    ModelState.AddModelError(nameof(vm.Email), "Email already exists.");
            }
            if (!string.IsNullOrWhiteSpace(vm.Username))
            {
                var normUser = vm.Username.ToUpper();
                if (await _userManager.Users.AnyAsync(u => u.NormalizedUserName == normUser && u.Id != profile.UserId))
                    ModelState.AddModelError(nameof(vm.Username), "Username already exists.");
            }

            if (!ModelState.IsValid) return View(vm);

            // Normalize and validate phone uniqueness (excluding current user)
            var normalizedPhone = NormalizePhone(vm.Phone);
            var normalizedQid = NormalizeQid(vm.QID);
            if (!string.IsNullOrWhiteSpace(normalizedPhone))
            {
                var phoneInUsers = await _userManager.Users.AnyAsync(u => u.PhoneNumber == normalizedPhone && u.Id != profile.UserId);
                var phoneInProfiles = await _db.UserProfiles.AnyAsync(p => p.Phone == normalizedPhone && p.Id != profile.Id);
                if (phoneInUsers || phoneInProfiles)
                {
                    ModelState.AddModelError(nameof(vm.Phone), "Phone number already exists.");
                }
            }
            if (!string.IsNullOrWhiteSpace(normalizedQid))
            {
                var qidExists = await _db.UserProfiles.AnyAsync(p => p.QID == normalizedQid && p.Id != profile.Id);
                if (qidExists)
                {
                    ModelState.AddModelError(nameof(vm.QID), "QID already exists.");
                }
            }
            if (!ModelState.IsValid) return View(vm);

            // Update Identity user core fields
            var user = await _userManager.FindByIdAsync(profile.UserId);
            if (user == null) return NotFound();
            user.Email = vm.Email;
            user.UserName = vm.Username;
            user.PhoneNumber = normalizedPhone;
            await _userManager.UpdateAsync(user);

            // Update role if changed
            var currentRole = await _roleManager.FindByIdAsync(profile.RoleId);
            if ((currentRole?.Name ?? string.Empty) != vm.Role)
            {
                if (currentRole != null)
                    await _userManager.RemoveFromRoleAsync(user, currentRole.Name!);
                if (!await _roleManager.RoleExistsAsync(vm.Role))
                    await _roleManager.CreateAsync(new IdentityRole(vm.Role));
                await _userManager.AddToRoleAsync(user, vm.Role);
                profile.RoleId = await _roleManager.Roles.Where(r => r.Name == vm.Role).Select(r => r.Id).FirstAsync();
            }

            // Update profile
            profile.Name = vm.Name;
            profile.Email = vm.Email;
            profile.Phone = normalizedPhone;
            profile.QID = string.IsNullOrWhiteSpace(normalizedQid) ? null : normalizedQid;
            profile.Username = vm.Username;
            profile.IsActive = vm.IsActive;
            profile.UpdatedBy = User?.Identity?.Name;
            profile.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            TempData["ok"] = "User updated.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /SuperAdminUsers/Toggle/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle(long id)
        {
            var profile = await _db.UserProfiles.FindAsync(id);
            if (profile == null) return NotFound();
            profile.IsActive = !profile.IsActive;
            profile.UpdatedBy = User?.Identity?.Name;
            profile.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["ok"] = profile.IsActive ? "User activated." : "User deactivated.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /SuperAdminUsers/ResetPassword/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(long id, string? tempPassword)
        {
            var profile = await _db.UserProfiles.FindAsync(id);
            if (profile == null) return NotFound();

            var user = await _userManager.FindByIdAsync(profile.UserId);
            if (user == null) return NotFound();

            // Use provided temp password or fallback
            var newPass = string.IsNullOrWhiteSpace(tempPassword) ? "Pass@1234" : tempPassword.Trim();

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var res = await _userManager.ResetPasswordAsync(user, token, newPass);
            if (!res.Succeeded)
            {
                foreach (var e in res.Errors) ModelState.AddModelError(string.Empty, e.Description);
                TempData["err"] = "Failed to reset password: " + string.Join("; ", res.Errors.Select(e => e.Description));
            }
            else
            {
                TempData["ok"] = $"Password reset for {user.Email}. Temporary password: {newPass}";
            }
            return RedirectToAction(nameof(Index));
        }

        // No Delete action as per requirement

        // GET: /SuperAdminUsers/CheckQid?qid=...&excludeId=...
        [HttpGet]
        public async Task<IActionResult> CheckQid(string? qid, long? excludeId)
        {
            var n = NormalizeQid(qid);
            if (string.IsNullOrWhiteSpace(n))
                return Json(new { ok = true, normalized = "" });
            var exists = await _db.UserProfiles.AnyAsync(p => p.QID == n && (!excludeId.HasValue || p.Id != excludeId.Value));
            return Json(new { ok = !exists, normalized = n });
        }

        // GET: /SuperAdminUsers/CheckEmail?email=...&excludeId=...
        [HttpGet]
        public async Task<IActionResult> CheckEmail(string? email, long? excludeId)
        {
            if (string.IsNullOrWhiteSpace(email))
                return Json(new { ok = true });
            var norm = email.Trim().ToUpperInvariant();
            // Find UserId for excludeId if provided
            string? excludeUserId = null;
            if (excludeId.HasValue)
            {
                excludeUserId = await _db.UserProfiles.Where(p => p.Id == excludeId.Value).Select(p => p.UserId).FirstOrDefaultAsync();
            }

            var existsInUsers = await _userManager.Users.AnyAsync(u => u.NormalizedEmail == norm && (excludeUserId == null || u.Id != excludeUserId));
            var existsInProfiles = await _db.UserProfiles.AnyAsync(p => p.Email.ToUpper() == norm && (!excludeId.HasValue || p.Id != excludeId.Value));
            var exists = existsInUsers || existsInProfiles;
            return Json(new { ok = !exists });
        }

        // GET: /SuperAdminUsers/CheckPhone?phone=974########&excludeId=...
        [HttpGet]
        public async Task<IActionResult> CheckPhone(string? phone, long? excludeId)
        {
            var normalized = NormalizePhone(phone);
            if (string.IsNullOrWhiteSpace(normalized))
                return Json(new { ok = true });
            string? excludeUserId = null;
            if (excludeId.HasValue)
            {
                excludeUserId = await _db.UserProfiles.Where(p => p.Id == excludeId.Value).Select(p => p.UserId).FirstOrDefaultAsync();
            }
            var existsInUsers = await _userManager.Users.AnyAsync(u => u.PhoneNumber == normalized && (excludeUserId == null || u.Id != excludeUserId));
            var existsInProfiles = await _db.UserProfiles.AnyAsync(p => p.Phone == normalized && (!excludeId.HasValue || p.Id != excludeId.Value));
            return Json(new { ok = !(existsInUsers || existsInProfiles) });
        }
    }

    public class SuperUserListVm
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? QID { get; set; }
        public string RoleName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class SuperUserCreateVm
    {
        [Required, StringLength(150)]
        public string Name { get; set; } = string.Empty;
        [Required, EmailAddress, StringLength(256)]
        public string Email { get; set; } = string.Empty;
        [StringLength(20)]
        [RegularExpression(@"^974\d{8}$", ErrorMessage = "Phone must be digits only: 974########.")]
        public string? Phone { get; set; }
        [StringLength(50)]
        [RegularExpression(@"^[0-9]{11}$", ErrorMessage = "QID must be exactly 11 digits.")]
        public string? QID { get; set; }
        [Required, StringLength(256)]
        [RegularExpression(@"^[A-Za-z0-9._@+\-]{3,256}$", ErrorMessage = "Username may contain letters, numbers and . _ @ + - (min 3).")]
        public string Username { get; set; } = string.Empty;
        [Required]
        [RegularExpression(@"^(Admin|DocumentVerifier|FinalApprover|MinistryOfficer|Owner|VehicleOwner)$", ErrorMessage = "Invalid role.")]
        public string Role { get; set; } = "";
        [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 6)]
        public string Password { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }

    public class SuperUserEditVm
    {
        public long Id { get; set; }
        [Required, StringLength(150)]
        public string Name { get; set; } = string.Empty;
        [Required, EmailAddress, StringLength(256)]
        public string Email { get; set; } = string.Empty;
        [StringLength(20)]
        [RegularExpression(@"^974\d{8}$", ErrorMessage = "Phone must be digits only: 974########.")]
        public string? Phone { get; set; }
        [StringLength(50)]
        [RegularExpression(@"^[0-9]{11}$", ErrorMessage = "QID must be exactly 11 digits.")]
        public string? QID { get; set; }
        [Required, StringLength(256)]
        [RegularExpression(@"^[A-Za-z0-9._@+\-]{3,256}$", ErrorMessage = "Username may contain letters, numbers and . _ @ + - (min 3).")]
        public string Username { get; set; } = string.Empty;
        [Required]
        [RegularExpression(@"^(Admin|DocumentVerifier|FinalApprover|MinistryOfficer|Owner|VehicleOwner)$", ErrorMessage = "Invalid role.")]
        public string Role { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}
