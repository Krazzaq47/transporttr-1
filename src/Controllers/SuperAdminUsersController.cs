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
            "Admin","DocumentVerifier","FinalApprover","MinistryOfficer","Owner"
        };

        public SuperAdminUsersController(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            RoleManager<IdentityRole> roleManager)
        {
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        // GET: /SuperAdminUsers
        public async Task<IActionResult> Index()
        {
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

            // Create Identity user
            var user = new IdentityUser { UserName = vm.Username, Email = vm.Email, EmailConfirmed = true };
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
                Phone = vm.Phone,
                QID = vm.QID,
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

            // Update Identity user core fields
            var user = await _userManager.FindByIdAsync(profile.UserId);
            if (user == null) return NotFound();
            user.Email = vm.Email;
            user.UserName = vm.Username;
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
            profile.Phone = vm.Phone;
            profile.QID = vm.QID;
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

        // No Delete action as per requirement
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
        [Phone, StringLength(50)]
        [RegularExpression(@"^[0-9+\-\s]{7,20}$", ErrorMessage = "Phone must be 7-20 digits and may include +, - and spaces.")]
        public string? Phone { get; set; }
        [StringLength(50)]
        [RegularExpression(@"^[0-9]{6,20}$", ErrorMessage = "QID must be 6-20 digits.")]
        public string? QID { get; set; }
        [Required, StringLength(256)]
        [RegularExpression(@"^[A-Za-z0-9._@+\-]{3,256}$", ErrorMessage = "Username may contain letters, numbers and . _ @ + - (min 3).")]
        public string Username { get; set; } = string.Empty;
        [Required]
        [RegularExpression(@"^(Admin|DocumentVerifier|FinalApprover|MinistryOfficer|Owner)$", ErrorMessage = "Invalid role.")]
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
        [Phone, StringLength(50)]
        [RegularExpression(@"^[0-9+\-\s]{7,20}$", ErrorMessage = "Phone must be 7-20 digits and may include +, - and spaces.")]
        public string? Phone { get; set; }
        [StringLength(50)]
        [RegularExpression(@"^[0-9]{6,20}$", ErrorMessage = "QID must be 6-20 digits.")]
        public string? QID { get; set; }
        [Required, StringLength(256)]
        [RegularExpression(@"^[A-Za-z0-9._@+\-]{3,256}$", ErrorMessage = "Username may contain letters, numbers and . _ @ + - (min 3).")]
        public string Username { get; set; } = string.Empty;
        [Required]
        [RegularExpression(@"^(Admin|DocumentVerifier|FinalApprover|MinistryOfficer|Owner)$", ErrorMessage = "Invalid role.")]
        public string Role { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}
