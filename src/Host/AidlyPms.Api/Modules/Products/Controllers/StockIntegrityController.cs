using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Products.Models;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Products.Controllers;

[Route("api/v1/inv/integrity")]
public class StockIntegrityController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly IStockIntegrityService _integrityService;

    public StockIntegrityController(
        IDbConnectionFactory db,
        IStockIntegrityService integrityService)
    {
        _db = db;
        _integrityService = integrityService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<StockIntegrityResult>>> GetIntegrityReport()
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        var result = await _integrityService.CheckIntegrityAsync(conn, CurrentPharmacyNo, CurrentBranchNo);

        var message = result.IsClean
            ? "Stock ledger integrity check passed with 0 discrepancies."
            : $"Stock ledger integrity check detected {result.TotalDiscrepancies} discrepant batch(es).";

        return OkResponse(result, message);
    }
}
