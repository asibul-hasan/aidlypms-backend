using System.Data;
using Dapper;
using Npgsql;

namespace AidlyPms.Api.Infrastructure;

public interface IDocumentNumberService
{
    Task<string> GetNextDocumentNumberAsync(
        NpgsqlConnection conn, 
        NpgsqlTransaction? tran, 
        long pharmacyNo, 
        long branchNo, 
        string docType);
}

public class DocumentNumberService : IDocumentNumberService
{
    public async Task<string> GetNextDocumentNumberAsync(
        NpgsqlConnection conn, 
        NpgsqlTransaction? tran, 
        long pharmacyNo, 
        long branchNo, 
        string docType)
    {
        var year = DateTime.UtcNow.Year.ToString();
        var prefix = docType switch
        {
            "SALE_INVOICE" => "POS",
            "PUR_INVOICE" => "PUR",
            "SALE_RETURN" => "SR",
            "PUR_RETURN" => "PR",
            "EXPENSE" => "EXP",
            "INCOME" => "INC",
            "DUE_COLL" => "DUE",
            "SUPP_PAY" => "PAY",
            "TRANSFER" => "TR",
            "ADJUSTMENT" => "ADJ",
            _ => docType
        };

        const string sql = @"
            INSERT INTO sys_doc_sequence (sequence_no, pharmacy_no, branch_no, doc_type, fin_year, last_seq_number, prefix_format, updated_at)
            VALUES (
                (COALESCE((SELECT MAX(sequence_no) FROM sys_doc_sequence), 0) + 1),
                @pharmacyNo, @branchNo, @docType, @year, 1, @prefixFormat, NOW()
            )
            ON CONFLICT (pharmacy_no, branch_no, doc_type, fin_year)
            DO UPDATE SET last_seq_number = sys_doc_sequence.last_seq_number + 1, updated_at = NOW()
            RETURNING last_seq_number;
        ";

        var prefixFormat = $"{prefix}-{{BRANCH:D2}}-{{YEAR}}-{{SEQ:6}}";

        var seq = await conn.ExecuteScalarAsync<long>(sql, new
        {
            pharmacyNo,
            branchNo,
            docType,
            year,
            prefixFormat
        }, tran);

        return $"{prefix}-{branchNo:D2}-{year}-{seq:D6}";
    }
}
