using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Customers.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Customers.Controllers;

[Route("api/v1/cust/customers")]
public class CustomerController : BaseController
{
    private readonly IDbConnectionFactory _db;

    public CustomerController(IDbConnectionFactory db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<object>>> GetCustomers(
        [FromQuery] string? search = null,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 50)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();

        var offset = (pageIndex - 1) * pageSize;

        const string countSql = @"
            SELECT COUNT(*)
            FROM cust_customers
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND (@search IS NULL OR name ILIKE '%' || @search || '%' OR phone ILIKE '%' || @search || '%');";

        var total = await conn.ExecuteScalarAsync<int>(countSql, new { CurrentPharmacyNo, CurrentBranchNo, search });

        const string dataSql = @"
            SELECT 
                customer_no, uuid, pharmacy_no, branch_no, name, phone,
                gender, customer_type, discount_percent, address,
                has_due, current_due, advance_balance, is_loyalty_member,
                loyalty_points, is_active, created_at, updated_at
            FROM cust_customers
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND (@search IS NULL OR name ILIKE '%' || @search || '%' OR phone ILIKE '%' || @search || '%')
            ORDER BY name ASC
            OFFSET @offset LIMIT @pageSize;";

        var items = (await conn.QueryAsync<CustCustomer>(dataSql, new
        {
            CurrentPharmacyNo,
            CurrentBranchNo,
            search,
            offset,
            pageSize
        })).AsList();

        // If a direct search autocomplete is executed without pagination request
        if (!string.IsNullOrEmpty(search) && pageSize == 50 && pageIndex == 1)
        {
            return OkResponse<object>(items);
        }

        var paged = new PagedResult<CustCustomer>(items, total, pageIndex, pageSize);
        return OkResponse<object>(paged);
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<CustCustomer>>> GetCustomerById(long id)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        const string sql = @"
            SELECT 
                customer_no, uuid, pharmacy_no, branch_no, name, phone,
                gender, customer_type, discount_percent, address,
                has_due, current_due, advance_balance, is_loyalty_member,
                loyalty_points, is_active, created_at, updated_at
            FROM cust_customers
            WHERE customer_no = @id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;";

        var customer = await conn.QuerySingleOrDefaultAsync<CustCustomer>(sql, new
        {
            id,
            CurrentPharmacyNo,
            CurrentBranchNo
        });

        if (customer == null)
        {
            return FailResponse<CustCustomer>("Customer not found or access denied.", statusCode: 404);
        }

        return OkResponse(customer);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<CustCustomer>>> CreateCustomer([FromBody] CustCustomer model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            return FailResponse<CustCustomer>("Customer name is required.");
        }

        if (string.IsNullOrWhiteSpace(model.Phone))
        {
            return FailResponse<CustCustomer>("Customer phone number is required.");
        }

        await using var conn = await _db.CreateOpenConnectionAsync();

        // Check if phone already exists in this pharmacy branch
        var exists = await conn.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(
                SELECT 1 FROM cust_customers 
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo AND phone = @Phone
            );", new { CurrentPharmacyNo, CurrentBranchNo, model.Phone });

        if (exists)
        {
            return FailResponse<CustCustomer>($"A customer with phone number '{model.Phone}' already exists.");
        }

        var customerNo = await conn.ExecuteScalarAsync<long>(
            "SELECT COALESCE(MAX(customer_no), 0) + 1 FROM cust_customers;");

        const string insertSql = @"
            INSERT INTO cust_customers (
                customer_no, uuid, pharmacy_no, branch_no, name, phone,
                gender, customer_type, discount_percent, address,
                has_due, current_due, advance_balance, is_loyalty_member,
                loyalty_points, is_active, created_at, updated_at
            ) VALUES (
                @customerNo, gen_random_uuid(), @CurrentPharmacyNo, @CurrentBranchNo, @Name, @Phone,
                @Gender, @CustomerType, @DiscountPercent, @Address,
                @HasDue, @CurrentDue, @AdvanceBalance, @IsLoyaltyMember,
                @LoyaltyPoints, @IsActive, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            )
            RETURNING 
                customer_no, uuid, pharmacy_no, branch_no, name, phone,
                gender, customer_type, discount_percent, address,
                has_due, current_due, advance_balance, is_loyalty_member,
                loyalty_points, is_active, created_at, updated_at;";

        var created = await conn.QuerySingleAsync<CustCustomer>(insertSql, new
        {
            customerNo,
            CurrentPharmacyNo,
            CurrentBranchNo,
            model.Name,
            model.Phone,
            model.Gender,
            model.CustomerType,
            model.DiscountPercent,
            model.Address,
            model.HasDue,
            model.CurrentDue,
            model.AdvanceBalance,
            model.IsLoyaltyMember,
            model.LoyaltyPoints,
            model.IsActive
        });

        return OkResponse(created, "Customer registered successfully.");
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ApiResponse<CustCustomer>>> UpdateCustomer(long id, [FromBody] CustCustomer model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            return FailResponse<CustCustomer>("Customer name is required.");
        }

        await using var conn = await _db.CreateOpenConnectionAsync();

        const string sql = @"
            UPDATE cust_customers
            SET name = @Name,
                phone = @Phone,
                gender = @Gender,
                customer_type = @CustomerType,
                discount_percent = @DiscountPercent,
                address = @Address,
                is_loyalty_member = @IsLoyaltyMember,
                is_active = @IsActive,
                updated_at = CURRENT_TIMESTAMP
            WHERE customer_no = @id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
            RETURNING 
                customer_no, uuid, pharmacy_no, branch_no, name, phone,
                gender, customer_type, discount_percent, address,
                has_due, current_due, advance_balance, is_loyalty_member,
                loyalty_points, is_active, created_at, updated_at;";

        var updated = await conn.QuerySingleOrDefaultAsync<CustCustomer>(sql, new
        {
            id,
            CurrentPharmacyNo,
            CurrentBranchNo,
            model.Name,
            model.Phone,
            model.Gender,
            model.CustomerType,
            model.DiscountPercent,
            model.Address,
            model.IsLoyaltyMember,
            model.IsActive
        });

        if (updated == null)
        {
            return FailResponse<CustCustomer>("Customer not found or access denied.", statusCode: 404);
        }

        return OkResponse(updated, "Customer updated successfully.");
    }
}
