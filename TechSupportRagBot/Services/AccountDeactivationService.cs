using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public sealed class AccountDeactivationService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager)
{
    public static readonly DateTimeOffset DisabledUntil = DateTimeOffset.MaxValue;

    public static bool IsDisabled(ApplicationUser user) => user.LockoutEnd == DisabledUntil;

    public async Task<IdentityResult> DeactivateAsync(ApplicationUser user)
    {
        user.LockoutEnabled = true;
        user.LockoutEnd = DisabledUntil;
        user.EmailNotificationsEnabled = false;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded) return result;

        await userManager.UpdateSecurityStampAsync(user);
        await db.BrowserPushSubscriptions.Where(x => x.UserId == user.Id).ExecuteDeleteAsync();
        return result;
    }
}
