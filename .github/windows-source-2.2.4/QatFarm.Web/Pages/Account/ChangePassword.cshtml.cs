using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QatFarm.Web.Models;

namespace QatFarm.Web.Pages.Account;

[Authorize]
public sealed class ChangePasswordModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public sealed class InputModel
    {
        [Required, DataType(DataType.Password)] public string OldPassword { get; set; } = string.Empty;
        [Required, StringLength(100, MinimumLength = 10), DataType(DataType.Password)] public string NewPassword { get; set; } = string.Empty;
        [Required, Compare(nameof(NewPassword)), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = string.Empty;
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var result = await userManager.ChangePasswordAsync(user, Input.OldPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }
        user.MustChangePassword = false;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            foreach (var error in updateResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }
        await signInManager.RefreshSignInAsync(user);
        return Redirect("/");
    }
}
