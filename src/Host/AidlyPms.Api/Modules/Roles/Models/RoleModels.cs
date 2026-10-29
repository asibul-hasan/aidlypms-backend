using System.Text.Json.Serialization;

namespace AidlyPms.Api.Modules.Roles.Models;

public class SysRole
{
    [JsonPropertyName("role_no")]
    public long RoleNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long? BranchNo { get; set; }

    [JsonPropertyName("role_name")]
    public string RoleName { get; set; } = string.Empty;

    [JsonPropertyName("alias")]
    public string Alias { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("is_system")]
    public bool IsSystem { get; set; }

    [JsonPropertyName("status")]
    public bool Status { get; set; } = true;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("permissions")]
    public List<string>? Permissions { get; set; }
}

public class SysPermission
{
    [JsonPropertyName("permission_no")]
    public long PermissionNo { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("module_group")]
    public string ModuleGroup { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class SysUser
{
    [JsonPropertyName("user_no")]
    public long UserNo { get; set; }

    [JsonPropertyName("uuid")]
    public Guid Uuid { get; set; } = Guid.NewGuid();

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("role_no")]
    public long RoleNo { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("password")]
    [JsonIgnore]
    public string? Password { get; set; }

    [JsonPropertyName("password_hash")]
    [JsonIgnore]
    public string? PasswordHash { get; set; }

    [JsonPropertyName("gender")]
    public short? Gender { get; set; } = 1;

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Joined
    [JsonPropertyName("role_name")]
    public string? RoleName { get; set; }
}

public class MenuPermissionsDto
{
    [JsonPropertyName("view")]
    public bool View { get; set; }

    [JsonPropertyName("create")]
    public bool Create { get; set; }

    [JsonPropertyName("edit")]
    public bool Edit { get; set; }

    [JsonPropertyName("delete")]
    public bool Delete { get; set; }
}

public class SysMenu
{
    [JsonPropertyName("menu_no")]
    public long MenuNo { get; set; }

    [JsonPropertyName("menu_code")]
    public string MenuCode { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("title_bn")]
    public string TitleBn { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("route")]
    public string? Route { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("parent_menu_no")]
    public long? ParentMenuNo { get; set; }

    [JsonPropertyName("parent_title")]
    public string? ParentTitle { get; set; }

    [JsonPropertyName("order_no")]
    public int OrderNo { get; set; } = 1;

    [JsonPropertyName("menu_type")]
    public string MenuType { get; set; } = "SCREEN";

    [JsonPropertyName("status")]
    public bool Status { get; set; } = true;

    [JsonPropertyName("permissions")]
    public MenuPermissionsDto? Permissions { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("children")]
    public List<SysMenu>? Children { get; set; }
}

public class SysRoleMenuPermission
{
    [JsonPropertyName("role_menu_perm_no")]
    public long RoleMenuPermNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("role_no")]
    public long RoleNo { get; set; }

    [JsonPropertyName("menu_no")]
    public long MenuNo { get; set; }

    [JsonPropertyName("can_view")]
    public bool CanView { get; set; }

    [JsonPropertyName("can_create")]
    public bool CanCreate { get; set; }

    [JsonPropertyName("can_edit")]
    public bool CanEdit { get; set; }

    [JsonPropertyName("can_delete")]
    public bool CanDelete { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class RoleMenuPermissionItemDto
{
    [JsonPropertyName("role_menu_perm_no")]
    public long? RoleMenuPermNo { get; set; }

    [JsonPropertyName("menu_no")]
    public long MenuNo { get; set; }

    [JsonPropertyName("menu_code")]
    public string MenuCode { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("title_bn")]
    public string TitleBn { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("route")]
    public string? Route { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("parent_menu_no")]
    public long? ParentMenuNo { get; set; }

    [JsonPropertyName("parent_title")]
    public string? ParentTitle { get; set; }

    [JsonPropertyName("order_no")]
    public int OrderNo { get; set; }

    [JsonPropertyName("menu_type")]
    public string MenuType { get; set; } = "SCREEN";

    [JsonPropertyName("can_view")]
    public bool CanView { get; set; }

    [JsonPropertyName("can_create")]
    public bool CanCreate { get; set; }

    [JsonPropertyName("can_edit")]
    public bool CanEdit { get; set; }

    [JsonPropertyName("can_delete")]
    public bool CanDelete { get; set; }
}

public class SaveRoleMenuPermissionsRequest
{
    [JsonPropertyName("role_no")]
    public long RoleNo { get; set; }

    [JsonPropertyName("permissions")]
    public List<RoleMenuPermissionItemDto> Permissions { get; set; } = new();
}

// Input-only DTO. Response models never expose either password field.
public class StaffUserRequest
{
    [JsonPropertyName("first_name")] public string FirstName { get; set; } = "";
    [JsonPropertyName("last_name")] public string LastName { get; set; } = "";
    [JsonPropertyName("phone")] public string Phone { get; set; } = "";
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("role_no")] public long RoleNo { get; set; }
    [JsonPropertyName("gender")] public short? Gender { get; set; }
    [JsonPropertyName("is_active")] public bool IsActive { get; set; } = true;
    [JsonPropertyName("password")] public string? Password { get; set; }
    public SysUser ToModel() => new() { FirstName = FirstName, LastName = LastName, Phone = Phone, Email = Email,
        RoleNo = RoleNo, Gender = Gender, IsActive = IsActive, Password = Password };
}
