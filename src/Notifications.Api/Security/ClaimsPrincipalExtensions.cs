using System.Security.Claims;

namespace Notifications.Api.Security;

public static class ClaimsPrincipalExtensions
{
    public static string? GetAuthUserId(this ClaimsPrincipal user)
    {
        return user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    public static string? GetEmail(this ClaimsPrincipal user)
    {
        return user.FindFirst("email")?.Value
            ?? user.FindFirst(ClaimTypes.Email)?.Value;
    }

    public static string? GetSessionId(this ClaimsPrincipal user)
    {
        return user.FindFirst("sid")?.Value;
    }

    public static bool HasImmunity(this ClaimsPrincipal user)
    {
        var val = user.FindFirst("immunity")?.Value;
        return string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAdmin(this ClaimsPrincipal user)
    {
        var role = user.FindFirst("role")?.Value ?? user.FindFirst(ClaimTypes.Role)?.Value;
        if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "system_admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "superadmin", StringComparison.OrdinalIgnoreCase)
            || user.IsInRole("admin")
            || user.IsInRole("Admin")
            || user.IsInRole("SYSTEM_ADMIN"))
        {
            return true;
        }

        // Check permissions claim
        var permissions = user.FindAll("permission").Select(p => p.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return permissions.Contains("notifications.read")
            || permissions.Contains("notifications.send")
            || permissions.Contains("notifications.campaigns.manage")
            || permissions.Contains("system.admin");
    }

    public static bool HasPermission(this ClaimsPrincipal user, string permission)
    {
        if (user.IsAdmin()) return true;

        var permissions = user.FindAll("permission").Select(p => p.Value);
        return permissions.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase));
    }
}
