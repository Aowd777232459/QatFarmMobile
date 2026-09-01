using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QatFarm.Web.Models;

namespace QatFarm.Web.Pages.Account;
public sealed class LogoutModel(SignInManager<ApplicationUser> signInManager) : PageModel
{
    public async Task<IActionResult> OnGetAsync() { await signInManager.SignOutAsync(); return RedirectToPage("/Account/Login"); }
}
