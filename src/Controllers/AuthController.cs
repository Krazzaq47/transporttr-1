using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using MT.Data;
using Twilio;
using Twilio.Rest.Verify.V2.Service;
using Microsoft.Extensions.Options;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Runtime.Intrinsics.X86;

namespace MT.Controllers
{
    [AllowAnonymous]
    public class AuthController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly TwilioVerifyOptions _twilio;

        // ADD: Convert any stored/typed value to E.164 +974########
        private static string ToE164FromAny(string phoneRaw)
        {
            var digits = new string((phoneRaw ?? "").Where(char.IsDigit).ToArray());
            if (digits.StartsWith("974")) digits = digits[3..];
            digits = digits.TrimStart('0');
            if (digits.Length != 8) throw new ArgumentException("Phone must be 8 digits for Qatar.");
            return $"+974{digits}";
        }

        // ADD: Start verification (send code)
        private async Task<(bool ok, string? e)> StartVerificationAsync(string phoneRaw)
        {
            var msisdn = ToE164FromAny(phoneRaw);
            var res = await VerificationResource.CreateAsync(
                to: msisdn,
                channel: "sms",
                pathServiceSid: _twilio.VerifyServiceSid
            );
            var ok = string.Equals(res.Status, "pending", StringComparison.OrdinalIgnoreCase);
            return (ok, ok ? null : $"Twilio start failed: {res.Status}");
        }

        // ADD: Check verification (verify code)
        private async Task<(bool ok, string? e)> CheckVerificationAsync(string phoneRaw, string code)
        {
            var msisdn = ToE164FromAny(phoneRaw);
            var res = await VerificationCheckResource.CreateAsync(
                to: msisdn,
                code: code,
                pathServiceSid: _twilio.VerifyServiceSid
            );
            var ok = string.Equals(res.Status, "approved", StringComparison.OrdinalIgnoreCase);
            return (ok, ok ? null : $"Twilio check failed: {res.Status}");
        }





        //public AuthController(ApplicationDbContext db,
        //    UserManager<IdentityUser> userManager,
        //    SignInManager<IdentityUser> signInManager,
        //    RoleManager<IdentityRole> roleManager)
        //{
        //    _db = db;
        //    _userManager = userManager;
        //    _signInManager = signInManager;
        //    _roleManager = roleManager;
        //}

        public AuthController(
    ApplicationDbContext db,
    UserManager<IdentityUser> userManager,
    SignInManager<IdentityUser> signInManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<TwilioVerifyOptions> twilioOptions)   // <-- ADD
        {
            _db = db;
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _twilio = twilioOptions.Value;                 // <-- ADD
        }

        // ===== Shared helpers =====
        private static string DigitsOnly(string s)
        {
            return new string((s ?? string.Empty).Where(char.IsDigit).ToArray());
        }

        private async Task<IdentityUser?> FindUserByPhoneAsync(string phone)
        {
            phone = (phone ?? string.Empty).Trim();
            var d = DigitsOnly(phone);
            // Qatar: country code 974 + 8 digits. Allow both with and without 974.
            var core = d.StartsWith("974") && d.Length > 3 ? d.Substring(3) : d;

            // Try via UserProfiles first
            var userId = await _db.UserProfiles
                .Where(p => p.Phone == phone || p.Phone == d || p.Phone == core)
                .Select(p => p.UserId)
                .FirstOrDefaultAsync();

            IdentityUser? u = null;
            if (!string.IsNullOrEmpty(userId))
            {
                u = await _userManager.FindByIdAsync(userId);
            }
            if (u == null)
            {
                // Fallback to AspNetUsers.PhoneNumber using same variants
                u = await _db.Users.FirstOrDefaultAsync(x => x.PhoneNumber == phone || x.PhoneNumber == d || x.PhoneNumber == core);
            }
            return u;
        }

        private async Task<bool> IsInRoleAsync(IdentityUser user, string role)
        {
            return await _userManager.IsInRoleAsync(user, role);
        }

        // ===== Audit helper =====
        private async Task LogOtpAsync(string? userId, string? phone, string role, string ev, bool success, string? codeMasked = null)
        {
            try
            {
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                var ua = Request.Headers["User-Agent"].ToString();
                _db.OtpAudits.Add(new OtpAudit
                {
                    UserId = userId,
                    Phone = phone,
                    Role = role,
                    Event = ev,
                    AtUtc = DateTime.UtcNow,
                    Ip = ip,
                    UserAgent = ua?.Length > 256 ? ua.Substring(0, 256) : ua,
                    Success = success,
                    CodeMasked = codeMasked
                });
                await _db.SaveChangesAsync();
            }
            catch { /* ignore audit failures */ }
        }

        private void SetOtpSession(string userId, string role, string? phone)
        {
            HttpContext.Session.SetString("OTP_TargetUserId", userId);
            HttpContext.Session.SetString("OTP_TargetRole", role);
            if (!string.IsNullOrWhiteSpace(phone)) HttpContext.Session.SetString("OTP_TargetPhone", phone);
            HttpContext.Session.SetString("OTP_Code", "123456");
            HttpContext.Session.SetString("OTP_ExpiresUtc", DateTime.UtcNow.AddMinutes(5).ToString("o"));
            // rate counters
            var issuedCount = (HttpContext.Session.GetInt32("OTP_IssuedCount") ?? 0) + 1;
            HttpContext.Session.SetInt32("OTP_IssuedCount", issuedCount);
            HttpContext.Session.SetString("OTP_LastIssuedUtc", DateTime.UtcNow.ToString("o"));
        }

        // REPLACE the whole method
        //private void SetOtpSession(string userId, string role, string? phoneE164)
        //{
        //    HttpContext.Session.SetString("OTP_TargetUserId", userId ?? "");
        //    HttpContext.Session.SetString("OTP_TargetRole", role ?? "");
        //    if (!string.IsNullOrWhiteSpace(phoneE164))
        //        HttpContext.Session.SetString("OTP_TargetPhone", phoneE164);

        //    HttpContext.Session.SetString("OTP_ExpiresUtc",
        //        DateTime.UtcNow.AddSeconds(_twilio.DefaultTtlSeconds).ToString("o"));

        //    var issuedCount = (HttpContext.Session.GetInt32("OTP_IssuedCount") ?? 0) + 1;
        //    HttpContext.Session.SetInt32("OTP_IssuedCount", issuedCount);
        //    HttpContext.Session.SetString("OTP_LastIssuedUtc", DateTime.UtcNow.ToString("o"));
        //}


        private bool CheckRateLimit(out string? message, bool enforceCooldown = true)
        {
            message = null;
            var issuedCount = HttpContext.Session.GetInt32("OTP_IssuedCount") ?? 0;
            // Relaxed limits: allow up to 20 OTPs per session
            if (issuedCount >= 20)
            {
                message = "Too many OTP requests. Please try again later.";
                return false;
            }
            var lastIssuedStr = HttpContext.Session.GetString("OTP_LastIssuedUtc");
            if (enforceCooldown && DateTime.TryParse(lastIssuedStr, out var lastIssuedUtc))
            {
                // Relaxed cooldown: 5 seconds instead of 30 seconds
                if ((DateTime.UtcNow - lastIssuedUtc).TotalSeconds < 5)
                {
                    message = "Please wait a moment before requesting another OTP.";
                    return false;
                }
            }
            return true;
        }

        // ===== OWNER OTP LOGIN =====
        [HttpGet]
        [Route("owner/login")]
        public IActionResult OwnerLogin()
        {
            return View("~/Views/Auth/OwnerLogin.cshtml");
        }

        [HttpPost]
        [Route("owner/login")] 
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OwnerLoginPost(string phone)
        {
            var user = await FindUserByPhoneAsync(phone);
            if (user == null || !(await IsInRoleAsync(user, "Owner")))
            {
                TempData["err"] = "Phone not found for an Owner user.";
                return RedirectToAction(nameof(OwnerLogin));
            }

            // Do not enforce cooldown on initial login request; only cap the total per session
            if (!CheckRateLimit(out var msg, enforceCooldown: false))
            {
                TempData["err"] = msg;
                await LogOtpAsync(user.Id, phone, "Owner", "rate_limited", false, null);
                return RedirectToAction(nameof(OwnerLogin));
            }

            SetOtpSession(user.Id, "Owner", phone);
            await LogOtpAsync(user.Id, phone, "Owner", "issued", true, "******");
            var langOwner = Request?.Query["lang"].ToString();
            TempData["ok"] = langOwner == "ar" ? "تم إرسال رمز التحقق إلى هاتفك." : "OTP sent to your phone."; //(use 123456 for dev)
            return RedirectToAction(nameof(OwnerOtp));
        }

        [HttpGet]
        [Route("owner/otp")]
        public IActionResult OwnerOtp()
        {
            return View("~/Views/Auth/OwnerOtp.cshtml");
        }

        [HttpPost]
        [Route("owner/otp")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OwnerOtpPost(string code, string? returnUrl)
        {
            var targetUserId = HttpContext.Session.GetString("OTP_TargetUserId");
            var targetRole = HttpContext.Session.GetString("OTP_TargetRole");
            var expected = HttpContext.Session.GetString("OTP_Code");
            var expStr = HttpContext.Session.GetString("OTP_ExpiresUtc");
            var phone = HttpContext.Session.GetString("OTP_TargetPhone");
            if (string.IsNullOrEmpty(targetUserId) || targetRole != "Owner")
            {
                TempData["err"] = "Session expired. Please try again.";
                return RedirectToAction(nameof(OwnerLogin));
            }
            if (!DateTime.TryParse(expStr, out var expUtc) || DateTime.UtcNow > expUtc)
            {
                TempData["err"] = "OTP expired. Please request a new code.";
                await LogOtpAsync(targetUserId, phone, "Owner", "expired", false, null);
                return RedirectToAction(nameof(OwnerLogin));
            }
            if (string.IsNullOrWhiteSpace(code) || expected != code.Trim())
            {
                TempData["err"] = "Invalid OTP.";
                await LogOtpAsync(targetUserId, phone, "Owner", "failed", false, null);
                return RedirectToAction(nameof(OwnerOtp));
            }

            var user = await _userManager.FindByIdAsync(targetUserId);
            if (user == null)
            {
                TempData["err"] = "Account not found.";
                return RedirectToAction(nameof(OwnerLogin));
            }
            await _signInManager.SignInAsync(user, isPersistent: true);
            await LogOtpAsync(targetUserId, phone, "Owner", "verified", true, null);
            // clear session
            HttpContext.Session.Remove("OTP_TargetUserId");
            HttpContext.Session.Remove("OTP_TargetRole");
            HttpContext.Session.Remove("OTP_Code");
            HttpContext.Session.Remove("OTP_ExpiresUtc");
            HttpContext.Session.Remove("OTP_TargetPhone");

            return Redirect(string.IsNullOrWhiteSpace(returnUrl) ? Url.Action("Index", "Home")! : returnUrl);
        }

        [HttpPost]
        [Route("owner/resend")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OwnerResend()
        {
            var targetUserId = HttpContext.Session.GetString("OTP_TargetUserId");
            var phone = HttpContext.Session.GetString("OTP_TargetPhone");
            if (string.IsNullOrEmpty(targetUserId))
            {
                TempData["err"] = "Session expired. Please start again.";
                return RedirectToAction(nameof(OwnerLogin));
            }
            if (!CheckRateLimit(out var msg))
            {
                TempData["err"] = msg;
                await LogOtpAsync(targetUserId, phone, "Owner", "rate_limited", false, null);
                return RedirectToAction(nameof(OwnerOtp));
            }
            SetOtpSession(targetUserId, "Owner", phone);
            await LogOtpAsync(targetUserId, phone, "Owner", "resend", true, "******");
            var langOwnerR = Request?.Query["lang"].ToString();
            TempData["ok"] = langOwnerR == "ar" ? "تمت إعادة إرسال رمز التحقق." : "OTP resent."; //(use 123456 in dev)
            return RedirectToAction(nameof(OwnerOtp));
        }

        // ===== MINISTRY OFFICER OTP LOGIN =====
        [HttpGet]
        [Route("ministry/login")]
        [Route("mto/login")]
        public IActionResult MinistryLogin()
        {
            return View("~/Views/Auth/MinistryLogin.cshtml");
        }

        [HttpPost]
        [Route("ministry/login")]
        [Route("mto/login")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MinistryLoginPost(string phone)
        {
            var user = await FindUserByPhoneAsync(phone);
            if (user == null || !(await IsInRoleAsync(user, "MinistryOfficer")))
            {
                TempData["err"] = "Phone number not found.";
                return RedirectToAction(nameof(MinistryLogin));
            }
            // Do not enforce cooldown on initial login request; only cap the total per session
            if (!CheckRateLimit(out var msg, enforceCooldown: false))
            {
                TempData["err"] = msg;
                await LogOtpAsync(user.Id, phone, "MinistryOfficer", "rate_limited", false, null);
                return RedirectToAction(nameof(MinistryLogin));
            }
            SetOtpSession(user.Id, "MinistryOfficer", phone);
            await LogOtpAsync(user.Id, phone, "MinistryOfficer", "issued", true, "******");
            var langM = Request?.Query["lang"].ToString();
            TempData["ok"] = langM == "ar" ? "تم إرسال رمز التحقق إلى هاتفك (استخدم 123456 للتطوير)." : "OTP sent to your phone (use 123456 for dev).";
            return Redirect("/mto/otp");
        }

        //[HttpPost]
        //[Route("ministry/login")]
        //[Route("mto/login")]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> MinistryLoginPost(string phone)
        //{
        //    var user = await FindUserByPhoneAsync(phone);
        //    if (user == null || !(await IsInRoleAsync(user, "MinistryOfficer")))
        //    {
        //        TempData["err"] = "Phone not found.";
        //        return RedirectToAction(nameof(MinistryLogin));
        //    }

        //    // Do not enforce cooldown on the very first send; still cap by session total
        //    if (!CheckRateLimit(out var msg, enforceCooldown: false))
        //    {
        //        TempData["err"] = msg;
        //        await LogOtpAsync(user.Id, phone, "MinistryOfficer", "rate_limited", false, null);
        //        return RedirectToAction(nameof(MinistryLogin));
        //    }

        //    // Send OTP via Twilio Verify
        //    try
        //    {
        //        var (sent, err) = await StartVerificationAsync(phone);
        //        await LogOtpAsync(user.Id, phone, "MinistryOfficer", "issued", sent, "******");

        //        if (!sent)
        //        {
        //            TempData["err"] = err ?? "Failed to send OTP. Please try again.";
        //            return RedirectToAction(nameof(MinistryLogin));
        //        }

        //        // Save minimal session (no OTP code) and refresh TTL; store normalized E.164
        //        SetOtpSession(user.Id, "MinistryOfficer", ToE164FromAny(phone));

        //        var lang = Request?.Query["lang"].ToString();
        //        TempData["ok"] = lang == "ar"
        //            ? "تم إرسال رمز التحقق إلى هاتفك."
        //            : "OTP sent to your phone.";

        //        return Redirect("/mto/otp");
        //    }
        //    catch (ArgumentException ex) // bad phone format
        //    {
        //        TempData["err"] = ex.Message; // e.g., "Phone must be 8 digits for Qatar."
        //        return RedirectToAction(nameof(MinistryLogin));
        //    }
        //    catch (Exception ex)
        //    {
        //        TempData["err"] = "Could not start verification. Please try again.";
        //        await LogOtpAsync(user.Id, phone, "MinistryOfficer", "issued_error", false, null);
        //        return RedirectToAction(nameof(MinistryLogin));
        //    }
        //}


        [HttpGet]
        [Route("ministry/otp")]
        [Route("mto/otp")]
        public IActionResult MinistryOtp()
        {
            return View("~/Views/Auth/MinistryOtp.cshtml");
        }

        [HttpPost]
        [Route("ministry/otp")]
        [Route("mto/otp")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MinistryOtpPost(string code, string? returnUrl)
        {
            var targetUserId = HttpContext.Session.GetString("OTP_TargetUserId");
            var targetRole = HttpContext.Session.GetString("OTP_TargetRole");
            var expected =  HttpContext.Session.GetString("OTP_Code");
            var expStr = HttpContext.Session.GetString("OTP_ExpiresUtc");
            var phone = HttpContext.Session.GetString("OTP_TargetPhone");
            if (string.IsNullOrEmpty(targetUserId) || targetRole != "MinistryOfficer")
            {
                TempData["err"] = "Session expired. Please try again.";
                return RedirectToAction(nameof(MinistryLogin));
            }
            if (!DateTime.TryParse(expStr, out var expUtc) || DateTime.UtcNow > expUtc)
            {
                TempData["err"] = "OTP expired. Please request a new code.";
                await LogOtpAsync(targetUserId, phone, "MinistryOfficer", "expired", false, null);
                return RedirectToAction(nameof(MinistryLogin));
            }
            if (string.IsNullOrWhiteSpace(code) || expected != code.Trim())
            {
                TempData["err"] = "Invalid OTP.";
                await LogOtpAsync(targetUserId, phone, "MinistryOfficer", "failed", false, null);
                return Redirect("/mto/otp");
            }

            var user = await _userManager.FindByIdAsync(targetUserId);
            if (user == null)
            {
                TempData["err"] = "Account not found.";
                return RedirectToAction(nameof(MinistryLogin));
            }
            await _signInManager.SignInAsync(user, isPersistent: true);

            await LogOtpAsync(targetUserId, phone, "MinistryOfficer", "verified", true, null);

            HttpContext.Session.Remove("OTP_TargetUserId");
            HttpContext.Session.Remove("OTP_TargetRole");
            HttpContext.Session.Remove("OTP_Code");
            HttpContext.Session.Remove("OTP_ExpiresUtc");
            HttpContext.Session.Remove("OTP_TargetPhone");

            return Redirect(string.IsNullOrWhiteSpace(returnUrl) ? Url.Action("List", "Vehicle", new { type = "truck" })! : returnUrl);
        }

        //[HttpPost] working copy
        //[Route("ministry/otp")]
        //[Route("mto/otp")]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> MinistryOtpPost(string code, string? returnUrl)
        //{
        //    var targetUserId = HttpContext.Session.GetString("OTP_TargetUserId");
        //    var targetRole = HttpContext.Session.GetString("OTP_TargetRole");
        //    var expStr = HttpContext.Session.GetString("OTP_ExpiresUtc");
        //    var phoneRaw = HttpContext.Session.GetString("OTP_TargetPhone");

        //    if (string.IsNullOrEmpty(targetUserId) || targetRole != "MinistryOfficer")
        //    {
        //        TempData["err"] = "Session expired. Please try again.";
        //        return RedirectToAction(nameof(MinistryLogin));
        //    }

        //    if (!DateTime.TryParse(expStr, out var expUtc) || DateTime.UtcNow > expUtc)
        //    {
        //        await LogOtpAsync(targetUserId, phoneRaw, "MinistryOfficer", "expired", false, null);
        //        TempData["err"] = "OTP expired. Please request a new code.";
        //        return RedirectToAction(nameof(MinistryLogin));
        //    }

        //    if (string.IsNullOrWhiteSpace(code))
        //    {
        //        TempData["err"] = "Enter the OTP code.";
        //        return Redirect("/mto/otp");
        //    }

        //    // Verify with Twilio
        //    var (ok, err) = await CheckVerificationAsync(phoneRaw ?? "", code.Trim());
        //    await LogOtpAsync(
        //        targetUserId,
        //        phoneRaw,
        //        "MinistryOfficer",
        //        "verify",
        //        ok,
        //        codeMasked: code.Length >= 2 ? $"{code[0]}***{code[^1]}" : "***"
        //    );

        //    if (!ok)
        //    {
        //        TempData["err"] = "Invalid OTP.";
        //        return Redirect("/mto/otp");
        //    }

        //    var user = await _userManager.FindByIdAsync(targetUserId);
        //    if (user == null)
        //    {
        //        TempData["err"] = "Account not found.";
        //        return RedirectToAction(nameof(MinistryLogin));
        //    }

        //    await _signInManager.SignInAsync(user, isPersistent: true);

        //    await LogOtpAsync(targetUserId, phoneRaw, "MinistryOfficer", "verified", true, null);

        //    // Clear OTP-related session (no OTP_Code anymore)
        //    HttpContext.Session.Remove("OTP_TargetUserId");
        //    HttpContext.Session.Remove("OTP_TargetRole");
        //    HttpContext.Session.Remove("OTP_ExpiresUtc");
        //    HttpContext.Session.Remove("OTP_TargetPhone");

        //    return Redirect(string.IsNullOrWhiteSpace(returnUrl)
        //        ? Url.Action("List", "Vehicle", new { type = "truck" })!
        //        : returnUrl);
        //}


        //[HttpPost]
        //[Route("ministry/resend")]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> MinistryResend()
        //{
        //    var targetUserId = HttpContext.Session.GetString("OTP_TargetUserId");
        //    var phone = HttpContext.Session.GetString("OTP_TargetPhone");
        //    if (string.IsNullOrEmpty(targetUserId))
        //    {
        //        TempData["err"] = "Session expired. Please start again.";
        //        return RedirectToAction(nameof(MinistryLogin));
        //    }
        //    if (!CheckRateLimit(out var msg))
        //    {
        //        TempData["err"] = msg;
        //        await LogOtpAsync(targetUserId, phone, "MinistryOfficer", "rate_limited", false, null);
        //        return Redirect("/mto/otp");
        //    }
        //    SetOtpSession(targetUserId, "MinistryOfficer", phone);
        //    await LogOtpAsync(targetUserId, phone, "MinistryOfficer", "resend", true, "******");
        //    var langMR = Request?.Query["lang"].ToString();
        //    TempData["ok"] = langMR == "ar" ? "تمت إعادة إرسال رمز التحقق (استخدم 123456 في التطوير)." : "OTP resent (use 123456 in dev).";
        //    return Redirect("/mto/otp");
        //}

        [HttpPost]
        [Route("ministry/resend")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MinistryResend()
        {
            var targetUserId = HttpContext.Session.GetString("OTP_TargetUserId");
            var phoneRaw = HttpContext.Session.GetString("OTP_TargetPhone");

            if (string.IsNullOrEmpty(targetUserId) || string.IsNullOrWhiteSpace(phoneRaw))
            {
                TempData["err"] = "Session expired. Please start again.";
                return RedirectToAction(nameof(MinistryLogin));
            }

            if (!CheckRateLimit(out var msg))
            {
                TempData["err"] = msg;
                await LogOtpAsync(targetUserId, phoneRaw, "MinistryOfficer", "rate_limited", false, null);
                return Redirect("/mto/otp");
            }

            // Send via Twilio Verify
            var (sent, err) = await StartVerificationAsync(phoneRaw);
            await LogOtpAsync(targetUserId, phoneRaw, "MinistryOfficer", "resend", sent, "******");

            if (!sent)
            {
                TempData["err"] = err ?? "Failed to resend OTP. Please try again.";
                return Redirect("/mto/otp");
            }

            // Save minimal session (no OTP code) and refresh TTL
            SetOtpSession(targetUserId, "MinistryOfficer", ToE164FromAny(phoneRaw));

            var langMR = Request?.Query["lang"].ToString();
            TempData["ok"] = langMR == "ar"
                ? "تمت إعادة إرسال رمز التحقق."
                : "OTP resent.";

            return Redirect("/mto/otp");
        }


        // ===== Helpers used by signup =====
        private async Task<string> EnsureRoleExistsAsync(string roleName)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role == null)
            {
                await _roleManager.CreateAsync(new IdentityRole(roleName));
                role = await _roleManager.FindByNameAsync(roleName);
            }
            return role!.Id;
        }

        private static string NormalizePhone11(string? s)
        {
            var d = DigitsOnly(s ?? string.Empty);
            if (d.StartsWith("974")) d = d.Substring(0, Math.Min(11, d.Length));
            else d = ("974" + d);
            if (d.Length > 11) d = d.Substring(0, 11);
            return d;
        }

        // ===== VEHICLE OWNER SIGNUP =====
        public class SignupVm
        {
            [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(150)]
            public string Name { get; set; } = string.Empty;
            [System.ComponentModel.DataAnnotations.RegularExpression(@"^974\d{8}$", ErrorMessage = "Phone must be 11 digits (974########).")]
            public string Phone { get; set; } = string.Empty;
            [System.ComponentModel.DataAnnotations.RegularExpression(@"^\d{11}$", ErrorMessage = "QID must be exactly 11 digits.")]
            public string QID { get; set; } = string.Empty;
        }

        [HttpGet]
        [Route("user/signup")]
        public IActionResult UserSignup(string? lang = null)
        {
            return View("~/Views/Auth/Signup.cshtml", new SignupVm());
        }

        [HttpPost]
        [Route("user/signup")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UserSignup(SignupVm vm, string? lang = null)
        {
            // Normalize
            vm.Phone = NormalizePhone11(vm.Phone);
            vm.QID = DigitsOnly(vm.QID ?? string.Empty);

            if (!ModelState.IsValid)
                return View("~/Views/Auth/Signup.cshtml", vm);

            // Uniqueness checks
            var email = ($"{vm.QID}@gmail.com");
            var normEmail = email.ToUpperInvariant();
            var existsEmailUsers = await _db.Users.AnyAsync(u => u.NormalizedEmail == normEmail);
            var existsEmailProfiles = await _db.UserProfiles.AnyAsync(p => p.Email.ToUpper() == normEmail);
            var existsPhoneUsers = await _db.Users.AnyAsync(u => u.PhoneNumber == vm.Phone);
            var existsPhoneProfiles = await _db.UserProfiles.AnyAsync(p => p.Phone == vm.Phone);
            var existsQidProfiles = await _db.UserProfiles.AnyAsync(p => p.QID == vm.QID);

            if (existsQidProfiles) ModelState.AddModelError(nameof(vm.QID), "QID already exists.");
            if (existsPhoneUsers || existsPhoneProfiles) ModelState.AddModelError(nameof(vm.Phone), "Phone already exists.");
            if (existsEmailUsers || existsEmailProfiles) ModelState.AddModelError(nameof(vm.QID), "Derived email already exists.");
            if (!ModelState.IsValid)
                return View("~/Views/Auth/Signup.cshtml", vm);

            // Save temporary data & send OTP
            HttpContext.Session.SetString("SU_Name", vm.Name);
            HttpContext.Session.SetString("SU_Phone", vm.Phone);
            HttpContext.Session.SetString("SU_QID", vm.QID);

            if (!CheckRateLimit(out var msg, enforceCooldown: false))
            {
                TempData["err"] = msg;
                await LogOtpAsync(null, vm.Phone, "VehicleOwnerSignup", "rate_limited", false, null);
                return View("~/Views/Auth/Signup.cshtml", vm);
            }

            SetOtpSession("", "VehicleOwnerSignup", vm.Phone);
            await LogOtpAsync(null, vm.Phone, "VehicleOwnerSignup", "issued", true, "******");
            var langSu = lang ?? Request?.Query["lang"].ToString();
            TempData["ok"] = langSu == "ar" ? "تم إرسال رمز التحقق إلى هاتفك )." : "OTP sent to your phone."; //(استخدم 123456 للتطوير (use 123456 for dev)
            return RedirectToAction(nameof(UserOtp), new { lang = lang == "ar" ? "ar" : null });
        }

        [HttpGet]
        [Route("user/otp")]
        public IActionResult UserOtp(string? lang = null)
        {
            return View("~/Views/Auth/UserOtp.cshtml");
        }

        [HttpPost]
        [Route("user/otp")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UserOtpPost(string code, string? lang = null)
        {
            var expected = HttpContext.Session.GetString("OTP_Code");
            var expStr = HttpContext.Session.GetString("OTP_ExpiresUtc");
            var role = HttpContext.Session.GetString("OTP_TargetRole");
            var phone = HttpContext.Session.GetString("OTP_TargetPhone");
            var name = HttpContext.Session.GetString("SU_Name");
            var qid = HttpContext.Session.GetString("SU_QID");
            if (role != "VehicleOwnerSignup" || string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(qid))
            {
                TempData["err"] = "Session expired. Please signup again.";
                return RedirectToAction(nameof(UserSignup), new { lang = lang == "ar" ? "ar" : null });
            }
            if (!DateTime.TryParse(expStr, out var expUtc) || DateTime.UtcNow > expUtc)
            {
                TempData["err"] = "OTP expired. Please request a new code.";
                await LogOtpAsync(null, phone, "VehicleOwnerSignup", "expired", false, null);
                return RedirectToAction(nameof(UserOtp), new { lang = lang == "ar" ? "ar" : null });
            }
            if (string.IsNullOrWhiteSpace(code) || expected != code.Trim())
            {
                TempData["err"] = "Invalid OTP.";
                await LogOtpAsync(null, phone, "VehicleOwnerSignup", "failed", false, null);
                return RedirectToAction(nameof(UserOtp), new { lang = lang == "ar" ? "ar" : null });
            }

            var email = ($"{qid}@gmail.com");
            var user = new IdentityUser { UserName = qid, Email = email, PhoneNumber = phone };
            var createRes = await _userManager.CreateAsync(user, "Qid@1234");
            if (!createRes.Succeeded)
            {
                TempData["err"] = string.Join("; ", createRes.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(UserSignup));
            }

            var roleId = await EnsureRoleExistsAsync("VehicleOwner");
            await _userManager.AddToRoleAsync(user, "VehicleOwner");

            var profile = new UserProfile
            {
                UserId = user.Id,
                Name = name!,
                Email = email,
                Phone = phone!,
                QID = qid!,
                RoleId = roleId,
                Username = qid!,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "self-signup"
            };
            _db.UserProfiles.Add(profile);
            await _db.SaveChangesAsync();

            // Clear session
            HttpContext.Session.Remove("SU_Name");
            HttpContext.Session.Remove("SU_Phone");
            HttpContext.Session.Remove("SU_QID");
            HttpContext.Session.Remove("OTP_TargetUserId");
            HttpContext.Session.Remove("OTP_TargetRole");
            HttpContext.Session.Remove("OTP_Code");
            HttpContext.Session.Remove("OTP_ExpiresUtc");
            HttpContext.Session.Remove("OTP_TargetPhone");

            await _signInManager.SignInAsync(user, isPersistent: true);
            await LogOtpAsync(user.Id, phone, "VehicleOwnerSignup", "verified", true, null);
            return RedirectToAction("Register", "Vehicle", new { lang = lang == "ar" ? "ar" : null });
        }

        [HttpPost]
        [Route("user/resend")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UserResend(string? lang = null)
        {
            var role = HttpContext.Session.GetString("OTP_TargetRole");
            var phone = HttpContext.Session.GetString("OTP_TargetPhone");
            if (role != "VehicleOwnerSignup" || string.IsNullOrWhiteSpace(phone))
            {
                TempData["err"] = "Session expired. Please signup again.";
                return RedirectToAction(nameof(UserSignup));
            }
            if (!CheckRateLimit(out var msg))
            {
                TempData["err"] = msg;
                await LogOtpAsync(null, phone, "VehicleOwnerSignup", "rate_limited", false, null);
                return RedirectToAction(nameof(UserOtp), new { lang = lang == "ar" ? "ar" : null });
            }
            SetOtpSession("", "VehicleOwnerSignup", phone);
            await LogOtpAsync(null, phone, "VehicleOwnerSignup", "resend", true, "******");
            var langSUR = lang ?? Request?.Query["lang"].ToString();
            TempData["ok"] = langSUR == "ar" ? "تمت إعادة إرسال رمز التحقق ." : "OTP resent ."; //(use 123456 in dev)
            return RedirectToAction(nameof(UserOtp), new { lang = lang == "ar" ? "ar" : null });
        }

        // Public checks
        [HttpGet]
        [Route("user/check-phone")]
        public async Task<IActionResult> CheckPhonePublic(string? phone)
        {
            var normalized = NormalizePhone11(phone);
            if (string.IsNullOrWhiteSpace(normalized)) return Json(new { ok = true });
            var existsInUsers = await _db.Users.AnyAsync(u => u.PhoneNumber == normalized);
            var existsInProfiles = await _db.UserProfiles.AnyAsync(p => p.Phone == normalized);
            return Json(new { ok = !(existsInUsers || existsInProfiles) });
        }

        [HttpGet]
        [Route("user/check-qid")]
        public async Task<IActionResult> CheckQidPublic(string? qid)
        {
            var n = DigitsOnly(qid ?? string.Empty);
            if (string.IsNullOrWhiteSpace(n)) return Json(new { ok = true });
            if (n.Length > 11) n = n.Substring(0, 11);
            var exists = await _db.UserProfiles.AnyAsync(p => p.QID == n);
            return Json(new { ok = !exists, normalized = n });
        }

        // ===== VEHICLE OWNER LOGIN (QID + Phone -> OTP) =====
        [HttpGet]
        [Route("user/login")]
        public IActionResult UserLogin(string? lang = null)
        {
            return View("~/Views/Auth/UserLogin.cshtml");
        }

        [HttpPost]
        [Route("user/login")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UserLoginPost(string qid, string phone, string? lang = null)
        {
            var nQid = DigitsOnly(qid ?? string.Empty);
            if (string.IsNullOrWhiteSpace(nQid) || nQid.Length != 11)
            {
                TempData["err"] = "QID must be exactly 11 digits.";
                return RedirectToAction(nameof(UserLogin), new { lang = lang == "ar" ? "ar" : null });
            }
            var nPhone = NormalizePhone11(phone);
            if (string.IsNullOrWhiteSpace(nPhone) || nPhone.Length != 11)
            {
                TempData["err"] = "Phone must be 11 digits (974########).";
                return RedirectToAction(nameof(UserLogin));
            }
            // Match existing VehicleOwner user by QID and Phone via UserProfiles
            var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.QID == nQid && p.Phone == nPhone);
            if (profile == null)
            {
                TempData["err"] = "Account not found for provided QID and Phone.";
                return RedirectToAction(nameof(UserLogin));
            }
            var user = await _userManager.FindByIdAsync(profile.UserId);
            if (user == null)
            {
                TempData["err"] = "User not found.";
                return RedirectToAction(nameof(UserLogin));
            }
            if (!await _userManager.IsInRoleAsync(user, "VehicleOwner") && !await _userManager.IsInRoleAsync(user, "Owner"))
            {
                TempData["err"] = "User is not a Vehicle Owner.";
                return RedirectToAction(nameof(UserLogin));
            }
            if (!CheckRateLimit(out var msg, enforceCooldown: false))
            {
                TempData["err"] = msg;
                await LogOtpAsync(user.Id, nPhone, "VehicleOwnerLogin", "rate_limited", false, null);
                return RedirectToAction(nameof(UserLogin));
            }
            SetOtpSession(user.Id, "VehicleOwnerLogin", nPhone);
            await LogOtpAsync(user.Id, nPhone, "VehicleOwnerLogin", "issued", true, "******");
            var langUL = lang ?? Request?.Query["lang"].ToString();
            TempData["ok"] = langUL == "ar" ? "تم إرسال رمز التحقق إلى هاتفك ." : "OTP sent to your phone."; //(use 123456 for dev)
            return RedirectToAction(nameof(UserLoginOtp), new { lang = lang == "ar" ? "ar" : null });
        }

        [HttpGet]
        [Route("user/login-otp")]
        public IActionResult UserLoginOtp(string? lang = null)
        {
            return View("~/Views/Auth/UserLoginOtp.cshtml");
        }

        [HttpPost]
        [Route("user/login-otp")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UserLoginOtpPost(string code, string? returnUrl, string? lang = null)
        {
            var targetUserId = HttpContext.Session.GetString("OTP_TargetUserId");
            var role = HttpContext.Session.GetString("OTP_TargetRole");
            var expected = HttpContext.Session.GetString("OTP_Code");
            var expStr = HttpContext.Session.GetString("OTP_ExpiresUtc");
            var phone = HttpContext.Session.GetString("OTP_TargetPhone");
            if (string.IsNullOrEmpty(targetUserId) || role != "VehicleOwnerLogin")
            {
                TempData["err"] = "Session expired. Please login again.";
                return RedirectToAction(nameof(UserLogin));
            }
            if (!DateTime.TryParse(expStr, out var expUtc) || DateTime.UtcNow > expUtc)
            {
                TempData["err"] = "OTP expired. Please request a new code.";
                await LogOtpAsync(targetUserId, phone, "VehicleOwnerLogin", "expired", false, null);
                return RedirectToAction(nameof(UserLoginOtp), new { lang = lang == "ar" ? "ar" : null });
            }
            if (string.IsNullOrWhiteSpace(code) || expected != code.Trim())
            {
                TempData["err"] = "Invalid OTP.";
                await LogOtpAsync(targetUserId, phone, "VehicleOwnerLogin", "failed", false, null);
                return RedirectToAction(nameof(UserLoginOtp));
            }
            var user = await _userManager.FindByIdAsync(targetUserId);
            if (user == null)
            {
                TempData["err"] = "Account not found.";
                return RedirectToAction(nameof(UserLogin));
            }
            await _signInManager.SignInAsync(user, isPersistent: true);
            await LogOtpAsync(targetUserId, phone, "VehicleOwnerLogin", "verified", true, null);

            // Clear session
            HttpContext.Session.Remove("OTP_TargetUserId");
            HttpContext.Session.Remove("OTP_TargetRole");
            HttpContext.Session.Remove("OTP_Code");
            HttpContext.Session.Remove("OTP_ExpiresUtc");
            HttpContext.Session.Remove("OTP_TargetPhone");

            return Redirect(string.IsNullOrWhiteSpace(returnUrl) ? Url.Action("List", "Vehicle", new { type = "truck" })! : returnUrl);
        }
    }
}
