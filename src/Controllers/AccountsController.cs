using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

[AllowAnonymous]
[Route("accounts")]
public class AccountsController : Controller
{
    private readonly SignInManager<IdentityUser> _signInManager;

    public AccountsController(SignInManager<IdentityUser> signInManager)
    {
        _signInManager = signInManager;
    }

    // POST /accounts/logout
    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        // Decide target BEFORE sign-out while user info is available
        string target = Url.Content("~/");

        if (User?.Identity?.IsAuthenticated == true)
        {
            if (User.IsInRole("SuperAdmin") ||
                User.IsInRole("Admin") ||
                User.IsInRole("FinalApprover") ||
                User.IsInRole("DocumentVerifier"))
            {
                target = Url.Content("~/identity/account/login");
            }
            else if (User.IsInRole("MinistryOfficer"))
            {
                target = Url.Content("~/mto/login");
            }
            else if (User.IsInRole("Owner"))
            {
                target = Url.Content("~/owner/login");
            }
            else if (User.IsInRole("VehicleOwner"))
            {
                target = Url.Content("~/user/login");
            }
        }

        await _signInManager.SignOutAsync();
        return LocalRedirect(target);
    }
}
