namespace AidlyPms.Api.Common.Security;

public sealed class RequestAccess
{
    public const string ItemKey = "Aidly.ValidatedAccess";
    public long UserNo { get; set; }
    public long RoleNo { get; set; }
    public long PharmacyNo { get; set; }
    public long BranchNo { get; set; }
    public DateTime UserUpdatedAt { get; set; }
    public string? AccessScope { get; set; }
    public bool IsPlatformOwner => AccessScope == "PLATFORM_OWNER";
    public bool IsStoreManager => AccessScope == "STORE_MANAGER";
    public bool IsFullAccess => IsPlatformOwner || IsStoreManager;
    public List<PlatformIdentity> PlatformIdentities { get; set; } = [];
    public HashSet<string> Permissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, MenuGrant> Menus { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public static RequestAccess? From(HttpContext context) => context.Items[ItemKey] as RequestAccess;
    public bool HasMenu(IEnumerable<string> codes, MenuOperation operation) => IsPlatformOwner ||
        codes.Any(code => !IsDeveloperMenu(code) && (IsStoreManager ||
            (Menus.TryGetValue(code, out var grant) && grant.Allows(operation))));
    public static bool IsDeveloperMenu(string code) => code is "MENU_1904" or "MENU_1905";
    public static bool IsReservedName(string? name) => new[] { "Admin", "Platform Owner", "Developer", "System Owner", "PMS Manager" }
        .Contains(name?.Trim(), StringComparer.OrdinalIgnoreCase);
}

public sealed class MenuGrant
{
    public string MenuCode { get; set; } = "";
    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool Allows(MenuOperation operation) => operation switch
    {
        MenuOperation.View => CanView, MenuOperation.Create => CanCreate,
        MenuOperation.Edit => CanEdit, MenuOperation.Delete => CanDelete, _ => false
    };
}
