using AidlyPms.Api.Modules.Sales.Models;
using Npgsql;

namespace AidlyPms.Api.Modules.Sales.Services;

public class SalePostingRequest
{
    public long PharmacyNo { get; set; }
    public long BranchNo { get; set; }
    public long UserNo { get; set; }
    public SaleInvoice Invoice { get; set; } = null!;
    public List<SaleInvoiceItem> Items { get; set; } = new();
    public List<SaleInvoicePayment>? Payments { get; set; }
    public long? ExchangeOfReturnNo { get; set; }
}

public class SalePostingResult
{
    public SaleInvoice Invoice { get; set; } = null!;
    public List<SaleInvoiceItem> Items { get; set; } = new();
    public List<SaleInvoicePayment> Payments { get; set; } = new();
    public long SaleInvoiceNo { get; set; }
    public string SalesNumber { get; set; } = string.Empty;
}

public interface ISalePostingEngine
{
    Task<SalePostingResult> ExecuteSaleAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tran,
        SalePostingRequest request);
}
