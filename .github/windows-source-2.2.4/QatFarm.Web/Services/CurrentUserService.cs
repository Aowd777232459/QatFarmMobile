using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace QatFarm.Web.Services;

public sealed record CurrentUserInfo(
    string? UserId,
    string? UserName,
    string? IpAddress,
    bool IsAdministrator,
    bool IsAccountant);

public sealed class CurrentUserService(
    AuthenticationStateProvider authenticationStateProvider,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<CurrentUserInfo> GetAsync()
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var principal = state.User;
        return new CurrentUserInfo(
            principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            principal.Identity?.Name,
            httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            principal.IsInRole("Administrator"),
            principal.IsInRole("Accountant"));
    }

    public async Task EnsureAdministratorAsync()
    {
        var current = await GetAsync();
        if (!current.IsAdministrator)
            throw new UnauthorizedAccessException("هذه العملية متاحة لمدير النظام فقط.");
    }

    public async Task EnsureFinancialRoleAsync()
    {
        var current = await GetAsync();
        if (!current.IsAdministrator && !current.IsAccountant)
            throw new UnauthorizedAccessException("هذه العملية متاحة للمدير أو المحاسب فقط.");
    }
}
