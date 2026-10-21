using AidlyPms.Api.Common;

namespace AidlyPms.Api.Tests;

public class TestTenantContext : ITenantContext
{
    public long PharmacyNo { get; set; } = 1;
    public long BranchNo { get; set; } = 1;
    public long UserNo { get; set; } = 1;
    public long RoleNo { get; set; } = 10; // Default salesman
    public string UserRole { get; set; } = "PMS Salesman";
    public string UserName { get; set; } = "Test Salesman";
}
