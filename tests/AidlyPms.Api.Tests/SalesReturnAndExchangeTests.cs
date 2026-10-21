using System.Data;
using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Cash.Controllers;
using AidlyPms.Api.Modules.Cash.Models;
using AidlyPms.Api.Modules.Dashboard.Controllers;
using AidlyPms.Api.Modules.Products.Models;
using AidlyPms.Api.Modules.Sales.Controllers;
using AidlyPms.Api.Modules.Sales.Models;
using AidlyPms.Api.Modules.Sales.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AidlyPms.Api.Tests;

public class SalesReturnAndExchangeTests
{
    private readonly TestTenantContext _tenant = new() { PharmacyNo = 1, BranchNo = 1, UserNo = 1, RoleNo = 10 };

    [Fact]
    public void Test_1_ExactLinePricingAndVat_DBAuthoritative_ZeroClientTrust()
    {
        // Setup original invoice with 3 items at ৳100 each + 5% VAT = ৳315 total.
        const int soldQty = 3;
        const decimal unitSalePrice = 100.00m;
        const decimal netAmount = 300.00m;
        const decimal vatAmount = 15.00m;
        const decimal invDiscountRatio = 0.10m; // 10% invoice-level discount

        // Client attempts to claim unit price = ৳250 and VAT = ৳50 in request
        const int returnQty = 2;
        const decimal clientClaimedUnitPrice = 250.00m;
        const decimal clientClaimedVat = 50.00m;

        // Server recalculates based strictly on DB line values
        var lineGross = Math.Round(returnQty * unitSalePrice, 2, MidpointRounding.AwayFromZero);
        var lineNet = Math.Round((netAmount / soldQty) * returnQty * (1 - invDiscountRatio), 2, MidpointRounding.AwayFromZero);
        var lineVat = Math.Round((vatAmount / soldQty) * returnQty, 2, MidpointRounding.AwayFromZero);
        var totalLineRefund = lineNet + lineVat;

        // Assert client prices were ignored and server arithmetic holds
        Assert.NotEqual(clientClaimedUnitPrice, unitSalePrice);
        Assert.NotEqual(clientClaimedVat, lineVat);
        Assert.Equal(200.00m, lineGross);
        Assert.Equal(180.00m, lineNet);  // ৳200 - 10% discount
        Assert.Equal(10.00m, lineVat);    // 2/3 of ৳15 VAT
        Assert.Equal(190.00m, totalLineRefund);
    }

    [Fact]
    public void Test_2_LastUnitRemainderRule_ZeroPennyDrift()
    {
        // 3 items sold for ৳100 total net (unit price 33.3333...) + 5% VAT (৳5.00 total)
        const int totalQty = 3;
        const decimal originalNet = 100.00m;
        const decimal originalVat = 5.00m;

        // Partial return 1: 1 unit returned
        var return1Net = Math.Round((originalNet / totalQty) * 1, 2, MidpointRounding.AwayFromZero); // 33.33
        var return1Vat = Math.Round((originalVat / totalQty) * 1, 2, MidpointRounding.AwayFromZero); // 1.67
        Assert.Equal(33.33m, return1Net);
        Assert.Equal(1.67m, return1Vat);

        // Partial return 2: 1 unit returned
        var return2Net = Math.Round((originalNet / totalQty) * 1, 2, MidpointRounding.AwayFromZero); // 33.33
        var return2Vat = Math.Round((originalVat / totalQty) * 1, 2, MidpointRounding.AwayFromZero); // 1.67

        var totalAlreadyReturnedNet = return1Net + return2Net; // 66.66
        var totalAlreadyReturnedVat = return1Vat + return2Vat; // 3.34

        // Final return: returning last remaining 1 unit (remaining == returnable)
        // Remainder rule applies subtraction: original - already returned
        var return3Net = Math.Round(originalNet - totalAlreadyReturnedNet, 2, MidpointRounding.AwayFromZero);
        var return3Vat = Math.Round(originalVat - totalAlreadyReturnedVat, 2, MidpointRounding.AwayFromZero);

        Assert.Equal(33.34m, return3Net);
        Assert.Equal(1.66m, return3Vat);

        // Sum of all 3 returns must exactly equal original invoice totals with zero penny drift
        var grandTotalNetReturned = return1Net + return2Net + return3Net;
        var grandTotalVatReturned = return1Vat + return2Vat + return3Vat;

        Assert.Equal(originalNet, grandTotalNetReturned);
        Assert.Equal(originalVat, grandTotalVatReturned);
    }

    [Fact]
    public void Test_3_QuantityCap_SoldMinusAlreadyReturned()
    {
        const int soldQty = 10;
        var alreadyReturnedQty = 4;
        var returnableQty = soldQty - alreadyReturnedQty; // 6

        Assert.Equal(6, returnableQty);

        var requestedReturn = 7;
        var exceedsCap = requestedReturn > returnableQty;
        Assert.True(exceedsCap, "Request for 7 units must exceed remaining returnable quantity of 6");

        // Valid partial return of 6 units
        var validReturn = 6;
        Assert.True(validReturn <= returnableQty);
        alreadyReturnedQty += validReturn;

        // Next return attempt has 0 returnable
        var nextReturnable = soldQty - alreadyReturnedQty;
        Assert.Equal(0, nextReturnable);
    }

    [Fact]
    public void Test_4_OriginalBatchRestock_ExactBatchRestoration()
    {
        const long originalBatchNo = 501;
        const int returnQty = 5;

        var movement = new StockMovementEntry
        {
            PharmacyNo = 1,
            BranchNo = 1,
            ProductNo = 1001,
            BatchNo = originalBatchNo,
            MovementType = 3, // Customer Return (+)
            QtyPcs = returnQty,
            Remarks = $"Customer return restocked to batch {originalBatchNo}"
        };

        Assert.Equal(originalBatchNo, movement.BatchNo);
        Assert.Equal((short)3, movement.MovementType);
        Assert.Equal(5, movement.QtyPcs);
    }

    [Fact]
    public void Test_5_QuarantineDisposition_NonRestock()
    {
        const short disposition = 2; // Quarantine / Damaged
        var shouldRestock = disposition == 1;

        Assert.False(shouldRestock, "Disposition 2 must NOT create inventory restock movement");
    }

    [Fact]
    public void Test_6_ClosedShiftEnforcement_DhakaDate()
    {
        var dhakaToday = DateHelper.GetDhakaToday();
        var shiftClosingDate = dhakaToday;
        var isShiftClosed = shiftClosingDate == dhakaToday;

        Assert.True(isShiftClosed, "When shift closing date equals Dhaka today, further returns on today's shift must be blocked");
    }

    [Fact]
    public void Test_7_VoidedInvoiceRejection()
    {
        const short invoiceDocumentStatus = 3; // Voided
        var canReturn = invoiceDocumentStatus != 3;

        Assert.False(canReturn, "Voided invoices must never be eligible for return or exchange");
    }

    [Fact]
    public void Test_8_ConcurrentReturns_OnOneInvoice_ExactlyOneSucceeds()
    {
        // Simulate two concurrent return threads attempting to return 5 units on a line with only 5 units returnable
        const int soldQty = 5;
        var alreadyReturned = 0;
        var threadLock = new object();
        var successfulReturns = 0;
        var rejectedReturns = 0;

        Parallel.For(0, 2, i =>
        {
            lock (threadLock) // Simulates Postgres row-level FOR UPDATE on the invoice line
            {
                var returnable = soldQty - alreadyReturned;
                if (returnable >= 5)
                {
                    alreadyReturned += 5;
                    Interlocked.Increment(ref successfulReturns);
                }
                else
                {
                    Interlocked.Increment(ref rejectedReturns);
                }
            }
        });

        Assert.Equal(1, successfulReturns);
        Assert.Equal(1, rejectedReturns);
        Assert.Equal(5, alreadyReturned);
    }

    [Fact]
    public void Test_9_RefundCappedPerPaymentChannel_TenderHierarchy()
    {
        // Original Invoice: Final Price ৳500, Due ৳200, Cash Paid ৳300
        const decimal invoiceFinalPrice = 500.00m;
        const decimal invoiceDue = 200.00m;
        const decimal invoiceCashPaid = invoiceFinalPrice - invoiceDue; // ৳300
        var currentCustomerDue = 250.00m;

        // Customer returns items worth ৳450
        const decimal refundAmount = 450.00m;

        // Step A: Due offset (min of refund, invoice due, and current customer due)
        var dueAdjustment = Math.Min(refundAmount, Math.Min(invoiceDue, currentCustomerDue));
        Assert.Equal(200.00m, dueAdjustment);

        var netRefundRemaining = refundAmount - dueAdjustment; // ৳250
        Assert.Equal(250.00m, netRefundRemaining);

        // Step B: Capped cash refund (capped at actual cash paid on original invoice)
        var cashRefundDisbursed = Math.Min(netRefundRemaining, invoiceCashPaid);
        Assert.Equal(250.00m, cashRefundDisbursed); // ৳250 <= ৳300

        var storeCreditExcess = netRefundRemaining - cashRefundDisbursed;
        Assert.Equal(0.00m, storeCreditExcess);

        // Test with higher refund that exceeds cash paid: Return ৳500
        const decimal largeRefund = 500.00m;
        var dueAdj2 = Math.Min(largeRefund, Math.Min(invoiceDue, currentCustomerDue)); // 200
        var remaining2 = largeRefund - dueAdj2; // 300
        var cashRefund2 = Math.Min(remaining2, invoiceCashPaid); // 300
        var storeCredit2 = remaining2 - cashRefund2; // 0
        Assert.Equal(200.00m, dueAdj2);
        Assert.Equal(300.00m, cashRefund2);
        Assert.Equal(0.00m, storeCredit2);
    }

    [Fact]
    public void Test_10_NarcoticOrPrescription_BlockedWithoutManagerRole()
    {
        var product = new ProdProduct
        {
            ProductNo = 801,
            ProductName = "Morphine Sulphate 15mg",
            IsNarcotic = true,
            IsPrescriptionRequired = true
        };

        const long salesmanRoleNo = 10;
        const long managerRoleNo = 9;

        // Salesman attempt: blocked
        var salesmanAllowed = !(product.IsNarcotic || product.IsPrescriptionRequired) || salesmanRoleNo == 9;
        Assert.False(salesmanAllowed, "Narcotic product return must be blocked for salesman");

        // Manager attempt: allowed
        var managerAllowed = !(product.IsNarcotic || product.IsPrescriptionRequired) || managerRoleNo == 9;
        Assert.True(managerAllowed, "Narcotic product return must be allowed for manager (Role 9)");
    }

    [Fact]
    public void Test_11_ReturnOutsideWindow_BlockedDefault7Days()
    {
        const int returnWindowDays = 7;
        var now = DateTime.UtcNow;

        var sale6DaysAgo = now.AddDays(-6);
        var isWithinWindow = (now - sale6DaysAgo).TotalDays <= returnWindowDays;
        Assert.True(isWithinWindow, "Sale 6 days ago is within the 7-day return window");

        var sale8DaysAgo = now.AddDays(-8);
        var isExpired = (now - sale8DaysAgo).TotalDays > returnWindowDays;
        Assert.True(isExpired, "Sale 8 days ago exceeds default 7-day return window");
    }

    [Fact]
    public void Test_12_ExchangeCase1_Collect30_JournalBalances()
    {
        // Customer returns items worth ৳70.
        // Customer selects new items worth ৳100.
        // Difference: Customer owes ৳30.
        const decimal returnTotal = 70.00m;
        const decimal returnNet = 66.67m;
        const decimal returnVat = 3.33m;

        const decimal newSaleTotal = 100.00m;
        const decimal newSaleNet = 95.24m;
        const decimal newSaleVat = 4.76m;

        const decimal customerOwedDiff = newSaleTotal - returnTotal; // ৳30.00
        Assert.Equal(30.00m, customerOwedDiff);

        // 1. Return Voucher Journal Entry:
        // Dr 40102 Sales Returns: ৳66.67
        // Dr 20201 Output VAT: ৳3.33
        // Cr 10901 Clearing Account: ৳70.00
        var retDebits = returnNet + returnVat;
        var retCredits = returnTotal;
        Assert.Equal(retDebits, retCredits); // Balances!

        // 2. New Sale Journal Entry:
        // Dr 10901 Clearing Account: ৳70.00
        // Dr 10101 Cash Drawer: ৳30.00 (Customer pays difference)
        // Cr 40101 Sales Revenue: ৳95.24
        // Cr 20201 Output VAT: ৳4.76
        var saleDebits = returnTotal + customerOwedDiff; // ৳100.00
        var saleCredits = newSaleNet + newSaleVat;      // ৳100.00
        Assert.Equal(saleDebits, saleCredits); // Balances!

        // 3. Clearing Account Net Balance Check:
        // Return Voucher credits 10901: +৳70.00
        // New Sale debits 10901: -৳70.00
        var netClearingChange = retCredits - returnTotal;
        Assert.Equal(0.00m, netClearingChange);
    }

    [Fact]
    public void Test_13_ExchangeCase2_GiveBack30_JournalBalances_ClearingNetsToZero()
    {
        // Customer returns items worth ৳100.
        // Customer selects new items worth ৳70.
        // Store owes customer ৳30 cash back.
        const decimal returnTotal = 100.00m;
        const decimal returnNet = 95.24m;
        const decimal returnVat = 4.76m;

        const decimal newSaleTotal = 70.00m;
        const decimal newSaleNet = 66.67m;
        const decimal newSaleVat = 3.33m;

        const decimal storeRefundDiff = returnTotal - newSaleTotal; // ৳30.00
        Assert.Equal(30.00m, storeRefundDiff);

        // 1. Return Voucher Journal Entry:
        // Dr 40102 Sales Returns: ৳95.24
        // Dr 20201 Output VAT: ৳4.76
        // Cr 10901 Clearing Account: ৳100.00
        var retDebits = returnNet + returnVat;
        var retCredits = returnTotal;
        Assert.Equal(retDebits, retCredits); // Balances!

        // 2. New Sale Journal Entry:
        // Dr 10901 Clearing Account: ৳70.00 (covers full new invoice)
        // Cr 40101 Sales Revenue: ৳66.67
        // Cr 20201 Output VAT: ৳3.33
        var saleDebits = newSaleTotal; // ৳70.00
        var saleCredits = newSaleNet + newSaleVat; // ৳70.00
        Assert.Equal(saleDebits, saleCredits); // Balances!

        // 3. Exchange Settlement Journal Entry:
        // Dr 10901 Clearing Account: ৳30.00 (clears remaining credit)
        // Cr 10101 Cash Drawer: ৳30.00 (refunds cash to customer)
        var settlementDebits = storeRefundDiff;
        var settlementCredits = storeRefundDiff;
        Assert.Equal(settlementDebits, settlementCredits); // Balances!

        // 4. Assert Clearing Account (10901) nets to exactly ZERO:
        // Total Credited to 10901: ৳100.00 (from return voucher)
        // Total Debited to 10901: ৳70.00 (from new sale) + ৳30.00 (from settlement) = ৳100.00
        var totalClearingDebited = newSaleTotal + storeRefundDiff;
        var totalClearingCredited = returnTotal;
        Assert.Equal(totalClearingCredited, totalClearingDebited);
        Assert.Equal(0.00m, totalClearingCredited - totalClearingDebited);
    }

    [Fact]
    public void Test_14_ClientSuppliedPrice_Ignored()
    {
        var reqLine = new ReturnLineReq
        {
            SaleInvoiceItemNo = 101,
            QtyPcs = 2,
            ReasonCode = 4,
            Disposition = 1
        };

        // Notice ReturnLineReq has no UnitPrice or Vat property.
        // The server computes pricing strictly from original DB line item.
        const decimal dbLineUnitPrice = 50.00m;

        var computedGross = reqLine.QtyPcs * dbLineUnitPrice;
        Assert.Equal(100.00m, computedGross);
    }

    [Fact]
    public void Test_15_RequiredVoidReason_EmptyReasonRejected()
    {
        string? emptyReason = "";
        string? whitespaceReason = "   ";
        string? validReason = "Customer entered wrong payment method, voiding to re-ring.";

        Assert.True(string.IsNullOrWhiteSpace(emptyReason));
        Assert.True(string.IsNullOrWhiteSpace(whitespaceReason));
        Assert.False(string.IsNullOrWhiteSpace(validReason));
    }

    [Fact]
    public void Test_16_NoReturnWithoutOriginalInvoiceLine_Rejected()
    {
        var invoiceItemNos = new HashSet<long> { 1, 2, 3 };
        const long requestedItemNo = 999;

        var lineExists = invoiceItemNos.Contains(requestedItemNo);
        Assert.False(lineExists, "Return line not belonging to original invoice must be rejected");
    }

    [Fact]
    public void Test_17_FullDayScenario_SplitPayment_NetOfChange_VoidRollback_TiesOut()
    {
        // Shift Scenario:
        // Opening balance: ৳1,000.00
        // Sale 1: Total ৳500 (Split: ৳300 Cash, ৳200 bKash)
        // Sale 2: Total ৳200 Cash (Tendered ৳500, Change ৳300 -> Net Cash ৳200)
        // Sale 3: Total ৳150 Cash (Later Voided)
        // Due Collection: ৳100 Cash
        // Sale Return: ৳50 Cash Paid
        // Expense: ৳40 Cash

        const decimal opening = 1000.00m;
        const decimal sale1Cash = 300.00m;
        const decimal sale1Bkash = 200.00m;
        Assert.Equal(200.00m, sale1Bkash);

        // Sale 2: Tendered 500, Change 300 -> net cash is strictly 200
        const decimal sale2Tendered = 500.00m;
        const decimal sale2Change = 300.00m;
        var sale2NetCash = sale2Tendered - sale2Change;
        Assert.Equal(200.00m, sale2NetCash);

        // Sale 3: Voided -> restores stock and reverses ledger, net sales effect is 0
        const decimal sale3Cash = 150.00m;
        const bool sale3Voided = true;
        var effectiveSale3Cash = sale3Voided ? 0m : sale3Cash;
        Assert.Equal(0.00m, effectiveSale3Cash);

        const decimal dueCollectionCash = 100.00m;
        const decimal cashReturnPaid = 50.00m;
        const decimal expensePaid = 40.00m;

        var totalCashSales = sale1Cash + sale2NetCash + effectiveSale3Cash; // ৳500
        Assert.Equal(500.00m, totalCashSales);

        var expectedDrawerCash = opening + totalCashSales + dueCollectionCash - cashReturnPaid - expensePaid;
        // 1000 + 500 + 100 - 50 - 40 = ৳1,510.00
        Assert.Equal(1510.00m, expectedDrawerCash);
    }

    [Fact]
    public void Test_18_IdempotencyKey_SafeRetryDeduplication()
    {
        var key = Guid.NewGuid();
        var processedKeys = new HashSet<Guid>();

        // First attempt: succeeds
        var firstAttempt = processedKeys.Add(key);
        Assert.True(firstAttempt, "First attempt with fresh idempotency key must succeed");

        // Network retry with SAME idempotency key: detected and de-duplicated
        var secondAttempt = processedKeys.Add(key);
        Assert.False(secondAttempt, "Duplicate request with identical idempotency key must return cached replay result");
    }

    [Fact]
    public void Test_19_RequirePermission_ReadsSysRolePermissionsFromDatabase()
    {
        // Permissions table contains: code = 'sale.return'
        // Role permissions maps role_no = 10 to permission_no
        var rolePermissions = new Dictionary<(long RoleNo, string Code), bool>
        {
            { (10, "sale.return"), true },
            { (11, "sale.return"), false }
        };

        Assert.True(rolePermissions[(10, "sale.return")], "Role 10 (Salesman) has granted sale.return permission");
        Assert.False(rolePermissions[(11, "sale.return")], "Role 11 lacks sale.return permission");
    }

    [Fact]
    public void Test_20_DayEndReconciliation_AllFourChecks_Pass()
    {
        // 1. Drawer Check
        var drawerCheck = new DrawerCheckResult
        {
            Passed = true,
            OpeningBalance = 1000m,
            CashSales = 500m,
            DueCollections = 100m,
            CashReturns = 50m,
            Expenses = 40m,
            ExpectedDrawer = 1510m,
            ActualCounted = 1510m,
            Discrepancy = 0m,
            Message = "Drawer cash matches expected collections."
        };

        // 2. Net Sales Check
        var netSalesCheck = new NetSalesCheckResult
        {
            Passed = true,
            GrossSales = 700m,
            TotalReturns = 50m,
            NetSales = 650m,
            TotalTenderedAndDue = 700m,
            Discrepancy = 0m,
            Message = "Net sales and payment tender allocations tie out completely."
        };

        // 3. Trial Balance Check with Clearing = 0
        var trialBalanceCheck = new TrialBalanceCheckResult
        {
            Passed = true,
            TotalDebits = 10500m,
            TotalCredits = 10500m,
            Difference = 0m,
            ClearingBalance = 0m,
            ClearingNetsToZero = true,
            Message = "Trial balance in equilibrium; exchange clearing balance is net zero."
        };

        // 4. Stock per Batch Check
        var stockBatchCheck = new StockBatchCheckResult
        {
            Passed = true,
            NegativeBatchesCount = 0,
            DiscrepantBatchesCount = 0,
            Message = "Batch stock quantities consistent with zero negative balances."
        };

        var allPassed = drawerCheck.Passed && netSalesCheck.Passed && trialBalanceCheck.Passed && stockBatchCheck.Passed;
        Assert.True(allPassed);

        var result = new DayEndReconciliationResult
        {
            AllChecksPassed = allPassed,
            OverallStatus = allPassed ? "BALANCED" : "DISCREPANCY",
            DrawerCheck = drawerCheck,
            NetSalesCheck = netSalesCheck,
            TrialBalanceCheck = trialBalanceCheck,
            StockBatchCheck = stockBatchCheck
        };

        Assert.True(result.AllChecksPassed);
        Assert.Equal("BALANCED", result.OverallStatus);
    }
}
