namespace AidlyPms.Api.Common;

/// <summary>
/// Canonical constants for universal inventory ledger movement types (inv_stock_movement.movement_type).
/// Every physical stock movement in Aidly PMS must use one of these unique codes.
/// </summary>
public static class StockMovementTypes
{
    public const short Opening = 1;
    public const short PurchaseReceipt = 2;
    public const short PosSale = 3;
    public const short CustomerReturn = 4;
    public const short VoidRestock = 5;
    public const short SupplierReturn = 6;
    public const short Adjustment = 7;
    public const short StockTake = 8;
    public const short TransferOut = 9;
    public const short TransferIn = 10;
    public const short Settlement = 11;

    public static readonly IReadOnlyDictionary<short, string> Names = new Dictionary<short, string>
    {
        [Opening] = "opening",
        [PurchaseReceipt] = "purchase receipt",
        [PosSale] = "POS sale",
        [CustomerReturn] = "customer return",
        [VoidRestock] = "void restock",
        [SupplierReturn] = "supplier return",
        [Adjustment] = "adjustment",
        [StockTake] = "stock-take",
        [TransferOut] = "transfer out",
        [TransferIn] = "transfer in",
        [Settlement] = "settlement"
    };
}
