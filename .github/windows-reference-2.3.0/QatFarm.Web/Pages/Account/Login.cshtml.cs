using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QatFarm.Web.Models;

namespace QatFarm.Web.Pages.Account;

[AllowAnonymous]
public sealed class LoginModel(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    ILogger<LoginModel> logger) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public bool FirstRun { get; private set; }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "أدخل الاسم الرباعي")] public string FullName { get; set; } = string.Empty;
        [Required(ErrorMessage = "أدخل كلمة السر"), DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated == true) return Redirect("/");
        FirstRun = !await userManager.Users.AnyAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        FirstRun = !await userManager.Users.AnyAsync();
        if (!ModelState.IsValid) return Page();
        var fullName = NormalizeName(Input.FullName);
        if (fullName is null)
        {
            ErrorMessage = "أدخل الاسم الرباعي الكامل.";
            return Page();
        }
        if (Input.Password.Trim().Length != 6)
        {
            ErrorMessage = "كلمة السر يجب أن تتكون من 6 أحرف أو أرقام بالضبط.";
            return Page();
        }

        if (FirstRun)
        {
            var internalEmail = $"admin-{Guid.NewGuid():N}@local.awad";
            var user = new ApplicationUser
            {
                FullName = fullName,
                UserName = fullName,
                Email = internalEmail,
                EmailConfirmed = true,
                IsActive = true,
                MustChangePassword = false,
                CreatedAt = DateTime.UtcNow
            };
            var created = await userManager.CreateAsync(user, Input.Password.Trim());
            if (!created.Succeeded)
            {
                ErrorMessage = string.Join(" | ", created.Errors.Select(x => x.Description));
                return Page();
            }
            var roleResult = await userManager.AddToRoleAsync(user, "Administrator");
            if (!roleResult.Succeeded)
            {
                await userManager.DeleteAsync(user);
                ErrorMessage = string.Join(" | ", roleResult.Errors.Select(x => x.Description));
                return Page();
            }
            await signInManager.SignInAsync(user, isPersistent: true);
            return LocalRedirect(string.IsNullOrWhiteSpace(ReturnUrl) ? "/" : ReturnUrl);
        }

        var matches = await userManager.Users.Where(x => x.IsActive && x.FullName == fullName).ToListAsync();
        if (matches.Count != 1)
        {
            ErrorMessage = matches.Count == 0 ? "الاسم أو كلمة السر غير صحيحة." : "يوجد أكثر من حساب بهذا الاسم. راجع مدير النظام.";
            return Page();
        }
        var account = matches[0];
        var result = await signInManager.PasswordSignInAsync(account, Input.Password.Trim(), Input.RememberMe, lockoutOnFailure: true);
        if (result.IsLockedOut) ErrorMessage = "تم قفل الحساب مؤقتًا بسبب تكرار المحاولات الخاطئة.";
        else if (!result.Succeeded) ErrorMessage = "الاسم أو كلمة السر غير صحيحة.";
        else
        {
            account.LastLoginAt = DateTime.UtcNow;
            var update = await userManager.UpdateAsync(account);
            if (!update.Succeeded) logger.LogWarning("تعذر تحديث وقت الدخول للمستخدم {UserId}", account.Id);
            return LocalRedirect(string.IsNullOrWhiteSpace(ReturnUrl) ? "/" : ReturnUrl);
        }
        return Page();
    }

    private static string? NormalizeName(string? value)
    {
        var parts = (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 4 ? string.Join(' ', parts) : null;
    }
}
