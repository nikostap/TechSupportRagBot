using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public static class DbInitializer
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = serviceProvider.GetRequiredService<ApplicationDbContext>();
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");

        string[] roles =
        {
            "Admin",
            "Operator",
            "Client"
        };

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        var adminLogin = configuration["BootstrapAdmin:Login"]?.Trim();
        var adminEmail = configuration["BootstrapAdmin:Email"]?.Trim();
        var adminPassword = configuration["BootstrapAdmin:Password"];

        var admin = string.IsNullOrWhiteSpace(adminLogin)
            ? null
            : await userManager.FindByNameAsync(adminLogin);
        admin ??= string.IsNullOrWhiteSpace(adminEmail)
            ? null
            : await userManager.FindByEmailAsync(adminEmail);
        admin ??= await userManager.FindByNameAsync("admin")
            ?? await userManager.FindByEmailAsync("admin@class-engineering.local")
            ?? await userManager.FindByEmailAsync("admin@techsupport.local");

        if (admin == null)
        {
            if (string.IsNullOrWhiteSpace(adminLogin)
                || string.IsNullOrWhiteSpace(adminEmail)
                || string.IsNullOrWhiteSpace(adminPassword))
            {
                logger.LogWarning(
                    "Bootstrap administrator was not created. Set BootstrapAdmin__Login, BootstrapAdmin__Email and BootstrapAdmin__Password for the first startup.");
                await SeedKnowledgeCategoriesAsync(db);
                return;
            }

            admin = new ApplicationUser
            {
                UserName = adminLogin,
                Email = adminEmail,
                FullName = "System Administrator",
                EmailConfirmed = true,
                MustChangePassword = true
            };

            var result = await userManager.CreateAsync(admin, adminPassword);
            if (!result.Succeeded)
            {
                var errors = string.Join("; ", result.Errors.Select(x => $"{x.Code}: {x.Description}"));
                throw new InvalidOperationException($"Could not create bootstrap administrator: {errors}");
            }

            await userManager.AddToRoleAsync(admin, "Admin");
        }
        else
        {
            admin.EmailConfirmed = true;
            await userManager.UpdateAsync(admin);

            if (!await userManager.IsInRoleAsync(admin, "Admin"))
            {
                await userManager.AddToRoleAsync(admin, "Admin");
            }
        }

        await SeedKnowledgeCategoriesAsync(db);
    }

    private static async Task SeedKnowledgeCategoriesAsync(ApplicationDbContext db)
    {
        await NormalizeInstructionCategoryAsync(db);
        var defaultCategories = new[]
        {
            "Инструкция",
            "Ошибки и коды",
            "Электрика",
            "Пневматика",
            "Обслуживание",
            "Запчасти",
            "Решённые обращения"
        };

        foreach (var name in defaultCategories)
        {
            if (!await db.KnowledgeCategories.AnyAsync(x => x.Name == name))
            {
                db.KnowledgeCategories.Add(new KnowledgeCategory { Name = name });
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task NormalizeInstructionCategoryAsync(ApplicationDbContext db)
    {
        const string obsoleteName = "Инструкции";
        const string canonicalName = "Инструкция";

        await db.KnowledgeDocuments
            .Where(x => x.Category == obsoleteName)
            .ExecuteUpdateAsync(x => x.SetProperty(d => d.Category, canonicalName));

        await db.KnowledgeChunks
            .Where(x => x.Category == obsoleteName)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.Category, canonicalName));

        await db.QAEntries
            .Where(x => x.Category == obsoleteName)
            .ExecuteUpdateAsync(x => x.SetProperty(q => q.Category, canonicalName));

        await db.ResolvedAnswers
            .Where(x => x.Category == obsoleteName)
            .ExecuteUpdateAsync(x => x.SetProperty(r => r.Category, canonicalName));

        var obsolete = await db.KnowledgeCategories.FirstOrDefaultAsync(x => x.Name == obsoleteName);
        var canonical = await db.KnowledgeCategories.FirstOrDefaultAsync(x => x.Name == canonicalName);

        if (obsolete == null)
        {
            return;
        }

        if (canonical == null)
        {
            obsolete.Name = canonicalName;
            return;
        }

        db.KnowledgeCategories.Remove(obsolete);
    }
}
