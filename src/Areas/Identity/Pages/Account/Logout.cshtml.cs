// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace MT.Areas.Identity.Pages.Account
{
    public class LogoutModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly ILogger<LogoutModel> _logger;

        public LogoutModel(SignInManager<IdentityUser> signInManager, ILogger<LogoutModel> logger)
        {
            _signInManager = signInManager;
            _logger = logger;
        }

        public async Task<IActionResult> OnPost(string returnUrl = null)
        {
            // Determine redirect based on current roles BEFORE sign-out
            bool isOwner = User?.IsInRole("Owner") == true;
            bool isMinistry = User?.IsInRole("MinistryOfficer") == true;

            await _signInManager.SignOutAsync();
            _logger.LogInformation("User logged out.");

            // Role-based redirects take precedence over any returnUrl to ensure the right login page
            if (isOwner)
            {
                // Custom Owner login
                return Redirect("/owner/login");
            }
            if (isMinistry)
            {
                // Custom Ministry login
                return Redirect("/mto/login");
            }

            if (!string.IsNullOrWhiteSpace(returnUrl))
                return LocalRedirect(returnUrl);

            // Default Identity login page for other users
            return Redirect("/Identity/Account/Login");
        }
    }
}
