using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MT.Controllers
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class UsersController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public UsersController(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        // GET: /Users
        public async Task<IActionResult> Index()
        {
            var users = _userManager.Users.ToList();
            var model = new List<UserWithRolesVm>();
            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                model.Add(new UserWithRolesVm
                {
                    Id = u.Id,
                    Email = u.Email ?? u.UserName ?? string.Empty,
                    Roles = roles.ToList()
                });
            }
            return View(model);
        }

        // GET: /Users/EditRoles/{id}
        public async Task<IActionResult> EditRoles(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var allRoles = _roleManager.Roles.Select(r => r.Name!).ToList();
            var userRoles = await _userManager.GetRolesAsync(user);
            var vm = new EditUserRolesVm
            {
                UserId = user.Id,
                Email = user.Email ?? user.UserName ?? string.Empty,
                AllRoles = allRoles,
                SelectedRoles = userRoles.ToList()
            };
            return View(vm);
        }

        // POST: /Users/EditRoles
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditRoles(EditUserRolesVm vm)
        {
            var user = await _userManager.FindByIdAsync(vm.UserId);
            if (user == null) return NotFound();

            var current = await _userManager.GetRolesAsync(user);
            var toRemove = current.Where(r => !vm.SelectedRoles.Contains(r)).ToList();
            var toAdd = vm.SelectedRoles.Where(r => !current.Contains(r)).ToList();

            if (toRemove.Any())
                await _userManager.RemoveFromRolesAsync(user, toRemove);
            if (toAdd.Any())
                await _userManager.AddToRolesAsync(user, toAdd);

            TempData["ok"] = "Roles updated.";
            return RedirectToAction(nameof(Index));
        }
    }

    public class UserWithRolesVm
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
    }

    public class EditUserRolesVm
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public List<string> AllRoles { get; set; } = new();
        public List<string> SelectedRoles { get; set; } = new();
    }
}
