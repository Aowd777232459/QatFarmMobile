using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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

    public sealed class InputModel
    {
        [Required(ErrorMessage = "أدخل البريد الإلكتروني"), EmailAddress] public string Email { get; set; } = string.Empty;
        [Required(ErrorMessage = "أدخل كلمة المرور"), DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; }
    }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true) return Redirect("/");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var user = await userManager.FindByEmailAsync(Input.Email.Trim());
        if (user is null || !user.IsActive)
        {
            ErrorMessage = "بيانات الدخول غير صحيحة أو الحساب غير نشط.";
            return Page();
        }
        var result = await signInManager.PasswordSignInAsync(user, Input.Password, Input.RememberMe, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            ErrorMessage = "تم قفل الحساب مؤقتًا بسبب تكرار المحاولات الخاطئة.";
            return Page();
        }
        if (!result.Succeeded)
        {
            ErrorMessage = "البريد الإلكتروني أو كلمة المرور غير صحيحة.";
            return Page();
        }
        user.LastLoginAt = DateTime.UtcNow;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            logger.LogWarning("تعذر تحديث وقت آخر دخول للمستخدم {UserId}: {Errors}",
                user.Id,
                string.Join(" | ", updateResult.Errors.Select(x => x.Description)));
        }
        if (user.MustChangePassword) return RedirectToPage("/Account/ChangePassword");
        return LocalRedirect(string.IsNullOrWhiteSpace(ReturnUrl) ? "/" : ReturnUrl);
    }
}
