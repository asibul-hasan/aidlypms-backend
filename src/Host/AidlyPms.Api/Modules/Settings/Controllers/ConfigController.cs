using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Settings.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Settings.Controllers;

[Route("api/v1/cfg")]
public class ConfigController : BaseController
{
    private readonly IDbConnectionFactory _db;

    public ConfigController(IDbConnectionFactory db)
    {
        _db = db;
    }

    [HttpGet("sale-configs")]
    public async Task<ActionResult<ApiResponse<CfgSaleConfig>>> GetSaleConfig()
    {
        using var conn = _db.CreateConnection();
        var cfg = await conn.QuerySingleOrDefaultAsync<CfgSaleConfig>(@"
            SELECT * FROM cfg_sale_configs
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { CurrentPharmacyNo, CurrentBranchNo });

        if (cfg == null)
        {
            cfg = new CfgSaleConfig
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo
            };
        }

        return OkResponse(cfg);
    }

    [HttpPut("sale-configs")]
    public async Task<ActionResult<ApiResponse<CfgSaleConfig>>> UpdateSaleConfig([FromBody] CfgSaleConfig model)
    {
        using var conn = _db.CreateConnection();
        var exists = await conn.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(SELECT 1 FROM cfg_sale_configs WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo);",
            new { CurrentPharmacyNo, CurrentBranchNo });

        if (exists)
        {
            await conn.ExecuteAsync(@"
                UPDATE cfg_sale_configs
                SET cash_received = @CashReceived,
                    out_of_stock_sale = @OutOfStockSale,
                    allow_loss_sale = @AllowLossSale,
                    product_purchase_cost = @ProductPurchaseCost,
                    stock_qty = @StockQty,
                    company_name = @CompanyName,
                    profit_margin = @ProfitMargin,
                    rak_number = @RakNumber,
                    opening_stock_percent = @OpeningStockPercent,
                    max_sale_discount_percent = @MaxSaleDiscountPercent,
                    is_max_discount_enabled = @IsMaxDiscountEnabled,
                    business_type = @BusinessType,
                    updated_at = NOW()
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new
                {
                    model.CashReceived,
                    model.OutOfStockSale,
                    model.AllowLossSale,
                    model.ProductPurchaseCost,
                    model.StockQty,
                    model.CompanyName,
                    model.ProfitMargin,
                    model.RakNumber,
                    model.OpeningStockPercent,
                    model.MaxSaleDiscountPercent,
                    model.IsMaxDiscountEnabled,
                    model.BusinessType,
                    CurrentPharmacyNo,
                    CurrentBranchNo
                });
        }
        else
        {
            var nextNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(sale_config_no), 0) + 1 FROM cfg_sale_configs;");

            await conn.ExecuteAsync(@"
                INSERT INTO cfg_sale_configs (
                    sale_config_no, pharmacy_no, branch_no, cash_received, out_of_stock_sale,
                    allow_loss_sale, product_purchase_cost, stock_qty, company_name,
                    profit_margin, rak_number, opening_stock_percent, max_sale_discount_percent,
                    is_max_discount_enabled, business_type, updated_at
                ) VALUES (
                    @NextNo, @CurrentPharmacyNo, @CurrentBranchNo, @CashReceived, @OutOfStockSale,
                    @AllowLossSale, @ProductPurchaseCost, @StockQty, @CompanyName,
                    @ProfitMargin, @RakNumber, @OpeningStockPercent, @MaxSaleDiscountPercent,
                    @IsMaxDiscountEnabled, @BusinessType, NOW()
                );",
                new
                {
                    NextNo = nextNo,
                    CurrentPharmacyNo,
                    CurrentBranchNo,
                    model.CashReceived,
                    model.OutOfStockSale,
                    model.AllowLossSale,
                    model.ProductPurchaseCost,
                    model.StockQty,
                    model.CompanyName,
                    model.ProfitMargin,
                    model.RakNumber,
                    model.OpeningStockPercent,
                    model.MaxSaleDiscountPercent,
                    model.IsMaxDiscountEnabled,
                    model.BusinessType
                });
        }

        model.PharmacyNo = CurrentPharmacyNo;
        model.BranchNo = CurrentBranchNo;
        return OkResponse(model, "Sale business rules updated successfully.");
    }

    [HttpGet("invoice-configs")]
    public async Task<ActionResult<ApiResponse<CfgInvoiceConfig>>> GetInvoiceConfig()
    {
        using var conn = _db.CreateConnection();
        var cfg = await conn.QuerySingleOrDefaultAsync<CfgInvoiceConfig>(@"
            SELECT * FROM cfg_invoice_configs
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { CurrentPharmacyNo, CurrentBranchNo });

        if (cfg == null)
        {
            cfg = new CfgInvoiceConfig
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo
            };
        }

        return OkResponse(cfg);
    }

    [HttpPut("invoice-configs")]
    public async Task<ActionResult<ApiResponse<CfgInvoiceConfig>>> UpdateInvoiceConfig([FromBody] CfgInvoiceConfig model)
    {
        using var conn = _db.CreateConnection();
        var exists = await conn.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(SELECT 1 FROM cfg_invoice_configs WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo);",
            new { CurrentPharmacyNo, CurrentBranchNo });

        if (exists)
        {
            await conn.ExecuteAsync(@"
                UPDATE cfg_invoice_configs
                SET paper_type = @PaperType,
                    top_row_text = @TopRowText,
                    show_served_by_row = @ShowServedByRow,
                    show_discount_less_row = @ShowDiscountLessRow,
                    show_card_fee_row = @ShowCardFeeRow,
                    show_customer_name = @ShowCustomerName,
                    show_customer_phone = @ShowCustomerPhone,
                    simple_print = @SimplePrint,
                    show_customer_vendor_balance = @ShowCustomerVendorBalance,
                    show_shop_logo = @ShowShopLogo,
                    shop_logo_url = @ShopLogoUrl,
                    bottom_row_text = @BottomRowText,
                    updated_at = NOW()
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new
                {
                    model.PaperType,
                    model.TopRowText,
                    model.ShowServedByRow,
                    model.ShowDiscountLessRow,
                    model.ShowCardFeeRow,
                    model.ShowCustomerName,
                    model.ShowCustomerPhone,
                    model.SimplePrint,
                    model.ShowCustomerVendorBalance,
                    model.ShowShopLogo,
                    model.ShopLogoUrl,
                    model.BottomRowText,
                    CurrentPharmacyNo,
                    CurrentBranchNo
                });
        }
        else
        {
            var nextNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(invoice_config_no), 0) + 1 FROM cfg_invoice_configs;");

            await conn.ExecuteAsync(@"
                INSERT INTO cfg_invoice_configs (
                    invoice_config_no, pharmacy_no, branch_no, paper_type, top_row_text,
                    show_served_by_row, show_discount_less_row, show_card_fee_row,
                    show_customer_name, show_customer_phone, simple_print,
                    show_customer_vendor_balance, show_shop_logo, shop_logo_url,
                    bottom_row_text, updated_at
                ) VALUES (
                    @NextNo, @CurrentPharmacyNo, @CurrentBranchNo, @PaperType, @TopRowText,
                    @ShowServedByRow, @ShowDiscountLessRow, @ShowCardFeeRow,
                    @ShowCustomerName, @ShowCustomerPhone, @SimplePrint,
                    @ShowCustomerVendorBalance, @ShowShopLogo, @ShopLogoUrl,
                    @BottomRowText, NOW()
                );",
                new
                {
                    NextNo = nextNo,
                    CurrentPharmacyNo,
                    CurrentBranchNo,
                    model.PaperType,
                    model.TopRowText,
                    model.ShowServedByRow,
                    model.ShowDiscountLessRow,
                    model.ShowCardFeeRow,
                    model.ShowCustomerName,
                    model.ShowCustomerPhone,
                    model.SimplePrint,
                    model.ShowCustomerVendorBalance,
                    model.ShowShopLogo,
                    model.ShopLogoUrl,
                    model.BottomRowText
                });
        }

        model.PharmacyNo = CurrentPharmacyNo;
        model.BranchNo = CurrentBranchNo;
        return OkResponse(model, "Thermal receipt template settings saved successfully.");
    }

    [HttpGet("product-configs")]
    public async Task<ActionResult<ApiResponse<CfgProductConfig>>> GetProductConfig()
    {
        using var conn = _db.CreateConnection();
        var cfg = await conn.QuerySingleOrDefaultAsync<CfgProductConfig>(@"
            SELECT * FROM cfg_product_configs
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { CurrentPharmacyNo, CurrentBranchNo });

        if (cfg == null)
        {
            cfg = new CfgProductConfig
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                ExpiredNotificationDays = 90
            };
        }

        return OkResponse(cfg);
    }

    [HttpPut("product-configs")]
    public async Task<ActionResult<ApiResponse<CfgProductConfig>>> UpdateProductConfig([FromBody] CfgProductConfig model)
    {
        using var conn = _db.CreateConnection();
        var exists = await conn.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(SELECT 1 FROM cfg_product_configs WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo);",
            new { CurrentPharmacyNo, CurrentBranchNo });

        if (exists)
        {
            await conn.ExecuteAsync(@"
                UPDATE cfg_product_configs
                SET expired_notification_days = @ExpiredNotificationDays, updated_at = NOW()
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { model.ExpiredNotificationDays, CurrentPharmacyNo, CurrentBranchNo });
        }
        else
        {
            var nextNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(product_config_no), 0) + 1 FROM cfg_product_configs;");

            await conn.ExecuteAsync(@"
                INSERT INTO cfg_product_configs (product_config_no, pharmacy_no, branch_no, expired_notification_days, updated_at)
                VALUES (@NextNo, @CurrentPharmacyNo, @CurrentBranchNo, @ExpiredNotificationDays, NOW());",
                new { NextNo = nextNo, CurrentPharmacyNo, CurrentBranchNo, model.ExpiredNotificationDays });
        }

        model.PharmacyNo = CurrentPharmacyNo;
        model.BranchNo = CurrentBranchNo;
        return OkResponse(model, "Product alert thresholds saved successfully.");
    }

    [HttpGet("loyalty-configs")]
    public async Task<ActionResult<ApiResponse<CfgLoyaltyConfig>>> GetLoyaltyConfig()
    {
        using var conn = _db.CreateConnection();
        var cfg = await conn.QuerySingleOrDefaultAsync<CfgLoyaltyConfig>(@"
            SELECT * FROM cfg_loyalty_configs
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { CurrentPharmacyNo, CurrentBranchNo });

        if (cfg == null)
        {
            cfg = new CfgLoyaltyConfig
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo
            };
        }

        return OkResponse(cfg);
    }

    [HttpPut("loyalty-configs")]
    public async Task<ActionResult<ApiResponse<CfgLoyaltyConfig>>> UpdateLoyaltyConfig([FromBody] CfgLoyaltyConfig model)
    {
        using var conn = _db.CreateConnection();
        var exists = await conn.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(SELECT 1 FROM cfg_loyalty_configs WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo);",
            new { CurrentPharmacyNo, CurrentBranchNo });

        if (exists)
        {
            await conn.ExecuteAsync(@"
                UPDATE cfg_loyalty_configs
                SET is_enabled = @IsEnabled,
                    earning_spend_amount_per_point = @EarningSpendAmountPerPoint,
                    redemption_points_per_taka = @RedemptionPointsPerTaka,
                    min_redeem_points = @MinRedeemPoints,
                    max_redeem_limit_per_tx = @MaxRedeemLimitPerTx,
                    updated_at = NOW()
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new
                {
                    model.IsEnabled,
                    model.EarningSpendAmountPerPoint,
                    model.RedemptionPointsPerTaka,
                    model.MinRedeemPoints,
                    model.MaxRedeemLimitPerTx,
                    CurrentPharmacyNo,
                    CurrentBranchNo
                });
        }
        else
        {
            var nextNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(loyalty_config_no), 0) + 1 FROM cfg_loyalty_configs;");

            await conn.ExecuteAsync(@"
                INSERT INTO cfg_loyalty_configs (
                    loyalty_config_no, pharmacy_no, branch_no, is_enabled,
                    earning_spend_amount_per_point, redemption_points_per_taka,
                    min_redeem_points, max_redeem_limit_per_tx, updated_at
                ) VALUES (
                    @NextNo, @CurrentPharmacyNo, @CurrentBranchNo, @IsEnabled,
                    @EarningSpendAmountPerPoint, @RedemptionPointsPerTaka,
                    @MinRedeemPoints, @MaxRedeemLimitPerTx, NOW()
                );",
                new
                {
                    NextNo = nextNo,
                    CurrentPharmacyNo,
                    CurrentBranchNo,
                    model.IsEnabled,
                    model.EarningSpendAmountPerPoint,
                    model.RedemptionPointsPerTaka,
                    model.MinRedeemPoints,
                    model.MaxRedeemLimitPerTx
                });
        }

        model.PharmacyNo = CurrentPharmacyNo;
        model.BranchNo = CurrentBranchNo;
        return OkResponse(model, "Customer loyalty rewards configuration saved successfully.");
    }

    [HttpGet("bulk-sms-configs")]
    [HttpGet("sms-configs")]
    public async Task<ActionResult<ApiResponse<CfgBulkSmsConfig>>> GetBulkSmsConfig()
    {
        using var conn = _db.CreateConnection();
        var cfg = await conn.QuerySingleOrDefaultAsync<CfgBulkSmsConfig>(@"
            SELECT * FROM cfg_bulk_sms_configs
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { CurrentPharmacyNo, CurrentBranchNo });

        if (cfg == null)
        {
            cfg = new CfgBulkSmsConfig
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo
            };
        }

        return OkResponse(cfg);
    }

    [HttpPut("bulk-sms-configs")]
    [HttpPut("sms-configs")]
    public async Task<ActionResult<ApiResponse<CfgBulkSmsConfig>>> UpdateBulkSmsConfig([FromBody] CfgBulkSmsConfig model)
    {
        using var conn = _db.CreateConnection();
        var exists = await conn.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(SELECT 1 FROM cfg_bulk_sms_configs WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo);",
            new { CurrentPharmacyNo, CurrentBranchNo });

        if (exists)
        {
            await conn.ExecuteAsync(@"
                UPDATE cfg_bulk_sms_configs
                SET sender_id = @SenderId,
                    sms_pack_unit_size = @SmsPackUnitSize,
                    sms_pack_unit_price = @SmsPackUnitPrice,
                    updated_at = NOW()
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { model.SenderId, model.SmsPackUnitSize, model.SmsPackUnitPrice, CurrentPharmacyNo, CurrentBranchNo });
        }
        else
        {
            var nextNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(bulk_sms_config_no), 0) + 1 FROM cfg_bulk_sms_configs;");

            await conn.ExecuteAsync(@"
                INSERT INTO cfg_bulk_sms_configs (
                    bulk_sms_config_no, pharmacy_no, branch_no, current_sms_count,
                    sms_pack_unit_size, sms_pack_unit_price, sender_id, updated_at
                ) VALUES (
                    @NextNo, @CurrentPharmacyNo, @CurrentBranchNo, @CurrentSmsCount,
                    @SmsPackUnitSize, @SmsPackUnitPrice, @SenderId, NOW()
                );",
                new
                {
                    NextNo = nextNo,
                    CurrentPharmacyNo,
                    CurrentBranchNo,
                    model.CurrentSmsCount,
                    model.SmsPackUnitSize,
                    model.SmsPackUnitPrice,
                    model.SenderId
                });
        }

        model.PharmacyNo = CurrentPharmacyNo;
        model.BranchNo = CurrentBranchNo;
        return OkResponse(model, "Bulk SMS settings saved successfully.");
    }
}
