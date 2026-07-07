using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public class AccessProfileService
{
    public const string Manager = "Manager";
    public const string Operator = "Operator";
    public const string Programmer = "Programmer";
    public const string Administrator = "Administrator";
    public const string Observer = "Observer";

    public static readonly IReadOnlyList<(string Key, string Name)> ProfileOptions =
    [
        (Manager, "Менеджер"),
        (Operator, "Оператор"),
        (Programmer, "Программист"),
        (Administrator, "Администратор"),
        (Observer, "Наблюдатель")
    ];

    public static readonly IReadOnlyList<AccessPermissionDefinition> PermissionDefinitions =
    [
        new("AdminDashboard", "Админ-панель"),
        new("ManageAdmins", "Сотрудники"),
        new("ManageClients", "Клиенты"),
        new("ManageMachines", "Станки"),
        new("ManageLicenses", "Лицензии"),
        new("ManageOperators", "Операторы"),
        new("KnowledgeBase", "База знаний"),
        new("QA", "QA вопросы-ответы"),
        new("Tickets", "Обращения"),
        new("TimeTracking", "Учёт времени"),
        new("Settings", "Настройки"),
        new("AccessProfiles", "Профили доступа"),
        new("ClientCabinet", "Кабинет клиента"),
        new("CreateTickets", "Создание обращений"),
        new("CompanyUsers", "Пользователи компании"),
        new("OperatorQueue", "Кабинет оператора"),
        new("ChatWrite", "Писать в чатах"),
        new("CloseTickets", "Закрывать обращения")
    ];

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public AccessProfileService(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<List<AccessProfileRule>> GetProfilesAsync(CancellationToken cancellationToken = default)
    {
        var raw = await _db.SystemSettings
            .Where(x => x.Key == SystemSettingKeys.AccessProfiles)
            .Select(x => x.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<AccessProfileRule>>(raw);
                if (parsed is { Count: > 0 })
                {
                    return MergeWithDefaults(parsed);
                }
            }
            catch
            {
                // Если JSON настроек поврежден, возвращаем безопасные дефолты.
            }
        }

        return CreateDefaultProfiles();
    }

    public async Task SaveProfilesAsync(IEnumerable<AccessProfileRule> profiles, CancellationToken cancellationToken = default)
    {
        var normalized = MergeWithDefaults(profiles.ToList());
        var json = JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = false });
        var setting = await _db.SystemSettings.FirstOrDefaultAsync(x => x.Key == SystemSettingKeys.AccessProfiles, cancellationToken);
        if (setting == null)
        {
            _db.SystemSettings.Add(new SystemSetting
            {
                Key = SystemSettingKeys.AccessProfiles,
                Value = json,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            setting.Value = json;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task FillMissingUserProfilesAsync(CancellationToken cancellationToken = default)
    {
        var users = await _db.Users
            .Where(x => string.IsNullOrWhiteSpace(x.AccessProfile))
            .ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            user.AccessProfile = await ResolveProfileKeyAsync(user, cancellationToken);
        }

        if (users.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> IsAllowedAsync(ClaimsPrincipal principal, string permission, CancellationToken cancellationToken = default)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var user = await _userManager.GetUserAsync(principal);
        if (user == null)
        {
            return false;
        }

        var profileKey = await ResolveProfileKeyAsync(user, cancellationToken);
        var profiles = await GetProfilesAsync(cancellationToken);
        var profile = profiles.FirstOrDefault(x => x.Key.Equals(profileKey, StringComparison.OrdinalIgnoreCase))
            ?? profiles.First(x => x.Key == Observer);

        return profile.Permissions.TryGetValue(permission, out var allowed) && allowed;
    }

    public async Task<string> ResolveProfileKeyAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(user.AccessProfile))
        {
            return NormalizeProfileKey(user.AccessProfile);
        }

        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Contains("Admin"))
        {
            return Administrator;
        }

        if (roles.Contains("Operator"))
        {
            return Operator;
        }

        return Manager;
    }

    public static string NormalizeProfileKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Observer;
        }

        var normalized = value.Trim();
        var option = ProfileOptions.FirstOrDefault(x =>
            x.Key.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || x.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(option.Key) ? Observer : option.Key;
    }

    public static string DisplayProfile(string? value)
    {
        var key = NormalizeProfileKey(value);
        return ProfileOptions.FirstOrDefault(x => x.Key == key).Name ?? key;
    }

    public static string? PermissionForPath(PathString path)
    {
        var value = path.Value ?? string.Empty;
        if (value.StartsWith("/Admin/AccessProfiles", StringComparison.OrdinalIgnoreCase)) return "AccessProfiles";
        if (value.StartsWith("/Admin/Admins", StringComparison.OrdinalIgnoreCase)) return "ManageAdmins";
        if (value.StartsWith("/Admin/Clients", StringComparison.OrdinalIgnoreCase)) return "ManageClients";
        if (value.StartsWith("/Admin/Machines", StringComparison.OrdinalIgnoreCase)) return "ManageMachines";
        if (value.StartsWith("/Admin/Licenses", StringComparison.OrdinalIgnoreCase)) return "ManageLicenses";
        if (value.StartsWith("/Admin/Operators", StringComparison.OrdinalIgnoreCase)) return "ManageOperators";
        if (value.StartsWith("/Admin/Knowledge", StringComparison.OrdinalIgnoreCase)) return "KnowledgeBase";
        if (value.StartsWith("/Admin/QA", StringComparison.OrdinalIgnoreCase)) return "QA";
        if (value.StartsWith("/Admin/Tickets", StringComparison.OrdinalIgnoreCase)) return "Tickets";
        if (value.StartsWith("/Admin/TimeTracking", StringComparison.OrdinalIgnoreCase)) return "TimeTracking";
        if (value.StartsWith("/Admin/Settings", StringComparison.OrdinalIgnoreCase)) return "Settings";
        if (value.Equals("/Admin", StringComparison.OrdinalIgnoreCase) || value.StartsWith("/Admin/Index", StringComparison.OrdinalIgnoreCase)) return "AdminDashboard";
        if (value.StartsWith("/Client/NewTicket", StringComparison.OrdinalIgnoreCase)) return "CreateTickets";
        if (value.StartsWith("/Client/Users", StringComparison.OrdinalIgnoreCase)) return "CompanyUsers";
        if (value.StartsWith("/Client", StringComparison.OrdinalIgnoreCase)) return "ClientCabinet";
        if (value.StartsWith("/Operator/Ticket", StringComparison.OrdinalIgnoreCase)) return "Tickets";
        if (value.StartsWith("/Operator", StringComparison.OrdinalIgnoreCase)) return "OperatorQueue";
        return null;
    }

    private static List<AccessProfileRule> MergeWithDefaults(List<AccessProfileRule> saved)
    {
        var defaults = CreateDefaultProfiles();
        foreach (var defaultProfile in defaults)
        {
            var current = saved.FirstOrDefault(x => x.Key.Equals(defaultProfile.Key, StringComparison.OrdinalIgnoreCase));
            if (current == null)
            {
                saved.Add(defaultProfile);
                continue;
            }

            current.Name = defaultProfile.Name;
            foreach (var permission in PermissionDefinitions)
            {
                current.Permissions.TryAdd(permission.Key, defaultProfile.Permissions[permission.Key]);
            }
        }

        return saved
            .Where(x => ProfileOptions.Any(p => p.Key == x.Key))
            .OrderBy(x => ProfileOptions.ToList().FindIndex(p => p.Key == x.Key))
            .ToList();
    }

    private static List<AccessProfileRule> CreateDefaultProfiles()
    {
        var all = PermissionDefinitions.ToDictionary(x => x.Key, _ => true);
        Dictionary<string, bool> Only(params string[] allowed) =>
            PermissionDefinitions.ToDictionary(x => x.Key, x => allowed.Contains(x.Key));

        return
        [
            new AccessProfileRule
            {
                Key = Manager,
                Name = "Менеджер",
                Permissions = Only("AdminDashboard", "ManageClients", "ManageMachines", "ManageLicenses", "Tickets", "ClientCabinet", "CreateTickets", "CompanyUsers", "ChatWrite")
            },
            new AccessProfileRule
            {
                Key = Operator,
                Name = "Оператор",
                Permissions = Only("OperatorQueue", "Tickets", "ChatWrite", "CloseTickets")
            },
            new AccessProfileRule
            {
                Key = Programmer,
                Name = "Программист",
                Permissions = Only("AdminDashboard", "ManageMachines", "KnowledgeBase", "QA", "Tickets", "Settings", "ChatWrite")
            },
            new AccessProfileRule
            {
                Key = Administrator,
                Name = "Администратор",
                Permissions = all
            },
            new AccessProfileRule
            {
                Key = Observer,
                Name = "Наблюдатель",
                Permissions = Only("AdminDashboard", "Tickets", "ClientCabinet", "OperatorQueue")
            }
        ];
    }
}

public sealed class AccessProfileRule
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, bool> Permissions { get; set; } = new();
}

public sealed record AccessPermissionDefinition(string Key, string Name);
