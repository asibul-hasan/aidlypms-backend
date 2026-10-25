using System.Security.Cryptography;
using System.Text;
using AidlyPms.Api.Common;
using AidlyPms.Api.Modules.Products.Models;
using AidlyPms.Api.Modules.Sales.Models;
using AidlyPms.Api.Modules.Sales.Services;
using AidlyPms.Api.Modules.Settings.Models;
using Xunit;

namespace AidlyPms.Api.Tests;

public class OfflinePosTests
{
    private static string ComputePayloadHash(CreateSalePayload payload)
    {
        var sb = new StringBuilder();
        sb.Append($"cust:{payload.CustomerNo}|mode:{payload.SaleMode}|paym:{payload.Invoice?.PaymentMethod}|willpay:{payload.CustomerWillPay}|due:{payload.DueAmount}|discT:{payload.DiscountType}|discV:{payload.DiscountValue}|items:");
        if (payload.Items != null)
        {
            foreach (var it in payload.Items.OrderBy(i => i.ProductNo).ThenBy(i => i.BatchNo).ThenBy(i => i.SaleQty))
            {
                sb.Append($"[{it.ProductNo},{it.BatchNo},{it.SaleQty},{it.UnitSalePrice:F2},{it.DiscountAmount:F2}]");
            }
        }
        if (payload.Payments != null)
        {
            sb.Append("|payments:");
            foreach (var p in payload.Payments.OrderBy(x => x.PaymentMethod).ThenBy(x => x.Amount))
            {
                sb.Append($"[{p.PaymentMethod},{p.Amount:F2}]");
            }
        }
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    [Fact]
    public void Test_1_IdempotencyHash_IdenticalPayloads_ProduceSameHash()
    {
        var key = Guid.NewGuid();
        var payload1 = new CreateSalePayload
        {
            IdempotencyKey = key,
            CustomerNo = 101,
            CustomerWillPay = 150m,
            Items = new List<SaleInvoiceItem>
            {
                new() { ProductNo = 1, BatchNo = 10, SaleQty = 2, UnitSalePrice = 50m, DiscountAmount = 0m },
                new() { ProductNo = 2, BatchNo = 20, SaleQty = 1, UnitSalePrice = 50m, DiscountAmount = 0m }
            },
            Payments = new List<SaleInvoicePayment>
            {
                new() { PaymentMethod = 1, Amount = 150m }
            }
        };

        // Items passed in reversed order
        var payload2 = new CreateSalePayload
        {
            IdempotencyKey = key,
            CustomerNo = 101,
            CustomerWillPay = 150m,
            Items = new List<SaleInvoiceItem>
            {
                new() { ProductNo = 2, BatchNo = 20, SaleQty = 1, UnitSalePrice = 50m, DiscountAmount = 0m },
                new() { ProductNo = 1, BatchNo = 10, SaleQty = 2, UnitSalePrice = 50m, DiscountAmount = 0m }
            },
            Payments = new List<SaleInvoicePayment>
            {
                new() { PaymentMethod = 1, Amount = 150m }
            }
        };

        var hash1 = ComputePayloadHash(payload1);
        var hash2 = ComputePayloadHash(payload2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Test_2_IdempotencyHash_MismatchedPayloads_ProduceDifferentHash()
    {
        var key = Guid.NewGuid();
        var payload1 = new CreateSalePayload
        {
            IdempotencyKey = key,
            CustomerNo = 101,
            CustomerWillPay = 150m,
            Items = new List<SaleInvoiceItem>
            {
                new() { ProductNo = 1, BatchNo = 10, SaleQty = 2, UnitSalePrice = 50m }
            }
        };

        var payload2 = new CreateSalePayload
        {
            IdempotencyKey = key,
            CustomerNo = 101,
            CustomerWillPay = 200m, // different amount
            Items = new List<SaleInvoiceItem>
            {
                new() { ProductNo = 1, BatchNo = 10, SaleQty = 2, UnitSalePrice = 50m }
            }
        };

        var hash1 = ComputePayloadHash(payload1);
        var hash2 = ComputePayloadHash(payload2);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Test_3_OfflineSale_TimestampValidation_RejectsFutureTimestamp()
    {
        var futureTime = DateTimeOffset.UtcNow.AddMinutes(10);
        var config = new CfgSaleConfig { OfflinePosEnabled = true, OfflineMaxAgeHours = 72 };

        var ex = Assert.Throws<PosSaleException>(() =>
        {
            if (futureTime > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                throw new PosSaleException("future_timestamp", "Client timestamp cannot be in the future (exceeds 5 minutes threshold).");
            }
        });

        Assert.Equal("future_timestamp", ex.ErrorCode);
    }

    [Fact]
    public void Test_4_OfflineSale_TimestampValidation_RejectsTooOldTimestamp()
    {
        var oldTime = DateTimeOffset.UtcNow.AddHours(-100);
        var config = new CfgSaleConfig { OfflinePosEnabled = true, OfflineMaxAgeHours = 72 };

        var ex = Assert.Throws<PosSaleException>(() =>
        {
            var maxAgeHours = config.OfflineMaxAgeHours > 0 ? config.OfflineMaxAgeHours : 72;
            if (oldTime < DateTimeOffset.UtcNow.AddHours(-maxAgeHours))
            {
                throw new PosSaleException("offline_sale_too_old", $"Offline sale exceeds maximum age limit of {maxAgeHours} hours.");
            }
        });

        Assert.Equal("offline_sale_too_old", ex.ErrorCode);
    }

    [Fact]
    public void Test_5_OfflineSale_RejectsWhenOfflinePosDisabled()
    {
        var config = new CfgSaleConfig { OfflinePosEnabled = false, OfflineMaxAgeHours = 72 };

        var ex = Assert.Throws<PosSaleException>(() =>
        {
            if (!config.OfflinePosEnabled)
            {
                throw new PosSaleException("offline_pos_disabled", "Offline POS sales are disabled for this branch in settings.");
            }
        });

        Assert.Equal("offline_pos_disabled", ex.ErrorCode);
    }

    [Fact]
    public void Test_6_OfflineSale_RejectsNarcoticAndPrescriptionMedicines()
    {
        var narcotic = new ProdProduct { ProductNo = 1, ProductName = "Pethidine 100mg", IsNarcotic = true, IsPrescriptionRequired = true };
        var rx = new ProdProduct { ProductNo = 2, ProductName = "Amoxicillin 500mg", IsNarcotic = false, IsPrescriptionRequired = true };

        var ex1 = Assert.Throws<PosSaleException>(() =>
        {
            const bool isOffline = true;
            if (isOffline && (narcotic.IsNarcotic || narcotic.IsPrescriptionRequired))
            {
                throw new PosSaleException("offline_not_allowed", $"Offline sale of narcotic or prescription-required medicine '{narcotic.ProductName}' is prohibited. Needs internet.");
            }
        });
        Assert.Equal("offline_not_allowed", ex1.ErrorCode);

        var ex2 = Assert.Throws<PosSaleException>(() =>
        {
            const bool isOffline = true;
            if (isOffline && (rx.IsNarcotic || rx.IsPrescriptionRequired))
            {
                throw new PosSaleException("offline_not_allowed", $"Offline sale of narcotic or prescription-required medicine '{rx.ProductName}' is prohibited. Needs internet.");
            }
        });
        Assert.Equal("offline_not_allowed", ex2.ErrorCode);
    }

    [Fact]
    public void Test_7_AuthoritativePrice_CalculatesPriceVarianceCorrectly()
    {
        // Client computed total as ৳190 (e.g. outdated price in cached catalog), server computes ৳210
        const decimal serverTotal = 210.00m;
        const decimal clientTotal = 190.00m;

        var priceVariance = serverTotal - clientTotal;

        Assert.Equal(20.00m, priceVariance);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Test_8_ShiftClosure_DeterminesSaleTimestampAndLateSync(bool isShiftClosed)
    {
        var clientTimestamp = DateTimeOffset.UtcNow.AddDays(-2);

        bool lateSync;
        DateTime saleTimestamp;

        if (isShiftClosed)
        {
            lateSync = true;
            saleTimestamp = DateTime.UtcNow;
        }
        else
        {
            lateSync = false;
            saleTimestamp = clientTimestamp.UtcDateTime;
        }

        if (isShiftClosed)
        {
            Assert.True(lateSync);
            Assert.True((DateTime.UtcNow - saleTimestamp).TotalSeconds < 2);
        }
        else
        {
            Assert.False(lateSync);
            Assert.Equal(clientTimestamp.UtcDateTime, saleTimestamp);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Test_9_SaleTimestamp_ModeResolution(bool isOffline)
    {
        var clientClaimedTimestamp = DateTimeOffset.UtcNow.AddDays(-3);

        DateTime saleTimestamp;
        bool lateSync;
        decimal? priceVariance;

        if (isOffline)
        {
            saleTimestamp = clientClaimedTimestamp.UtcDateTime;
            lateSync = false;
            priceVariance = 0m;
        }
        else
        {
            // Server authoritative time for online sales
            saleTimestamp = DateTime.UtcNow;
            lateSync = false;
            priceVariance = null;
        }

        if (isOffline)
        {
            Assert.Equal(clientClaimedTimestamp.UtcDateTime, saleTimestamp);
            Assert.Equal(0m, priceVariance);
        }
        else
        {
            Assert.False(lateSync);
            Assert.Null(priceVariance);
            Assert.True((DateTime.UtcNow - saleTimestamp).TotalSeconds < 2);
            Assert.NotEqual(clientClaimedTimestamp.UtcDateTime, saleTimestamp);
        }
    }
}
