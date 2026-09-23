using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Doctors.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Doctors.Controllers;

[Route("api/v1/doc")]
public class DoctorController : BaseController
{
    private readonly IDbConnectionFactory _db;

    public DoctorController(IDbConnectionFactory db)
    {
        _db = db;
    }

    [HttpGet("doctors")]
    public async Task<ActionResult<ApiResponse<PagedResult<DocDoctor>>>> GetDoctors(
        [FromQuery] QueryFilter filter,
        [FromQuery] string? search)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT * FROM doc_doctors
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo";

        var countSql = @"
            SELECT COUNT(1) FROM doc_doctors
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo";

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (first_name_en ILIKE @Search OR last_name_en ILIKE @Search OR phone ILIKE @Search)";
            countSql += " AND (first_name_en ILIKE @Search OR last_name_en ILIKE @Search OR phone ILIKE @Search)";
            pms.Add("Search", $"%{search.Trim()}%");
        }

        sql += " ORDER BY doctor_no DESC LIMIT @PageSize OFFSET @Offset;";
        pms.Add("PageSize", filter.PageSize);
        pms.Add("Offset", (filter.PageIndex - 1) * filter.PageSize);

        var total = await conn.ExecuteScalarAsync<int>(countSql, pms);
        var items = (await conn.QueryAsync<DocDoctor>(sql, pms)).ToList();

        var result = new PagedResult<DocDoctor>(items, total, filter.PageIndex, filter.PageSize);
        return PagedResponse(result);
    }

    [HttpGet("doctors/{id}")]
    public async Task<ActionResult<ApiResponse<DocDoctor>>> GetDoctorById(long id)
    {
        using var conn = _db.CreateConnection();
        var doc = await conn.QuerySingleOrDefaultAsync<DocDoctor>(@"
            SELECT * FROM doc_doctors
            WHERE doctor_no = @Id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { Id = id, CurrentPharmacyNo, CurrentBranchNo });

        if (doc == null)
            return FailResponse<DocDoctor>("Doctor not found.", statusCode: 404);

        return OkResponse(doc);
    }

    [HttpPost("doctors")]
    public async Task<ActionResult<ApiResponse<DocDoctor>>> CreateDoctor([FromBody] DocDoctor model)
    {
        if (string.IsNullOrWhiteSpace(model.FirstNameEn))
            return FailResponse<DocDoctor>("First name is required.");

        if (string.IsNullOrWhiteSpace(model.Phone))
            return FailResponse<DocDoctor>("Phone number is required.");

        using var conn = _db.CreateConnection();

        var exists = await conn.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(
                SELECT 1 FROM doc_doctors
                WHERE phone = @Phone AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
            );",
            new { model.Phone, CurrentPharmacyNo, CurrentBranchNo });

        if (exists)
            return FailResponse<DocDoctor>($"Doctor with phone '{model.Phone}' already exists in this pharmacy branch.");

        var nextNo = await conn.ExecuteScalarAsync<long>(
            "SELECT COALESCE(MAX(doctor_no), 0) + 1 FROM doc_doctors;");

        model.DoctorNo = nextNo;
        model.PharmacyNo = CurrentPharmacyNo;
        model.BranchNo = CurrentBranchNo;
        model.IsActive = true;
        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            INSERT INTO doc_doctors (
                doctor_no, pharmacy_no, branch_no, first_name_en, last_name_en,
                first_name_bn, last_name_bn, phone, gender, description_en,
                description_bn, password_hash, signature_image_url, is_active,
                created_at, updated_at
            ) VALUES (
                @DoctorNo, @PharmacyNo, @BranchNo, @FirstNameEn, @LastNameEn,
                @FirstNameBn, @LastNameBn, @Phone, @Gender, @DescriptionEn,
                @DescriptionBn, '', @SignatureImageUrl, @IsActive,
                @CreatedAt, @UpdatedAt
            );", model);

        return OkResponse(model, $"Doctor '{model.FirstNameEn}' registered successfully.");
    }

    [HttpPut("doctors/{id}")]
    public async Task<ActionResult<ApiResponse<DocDoctor>>> UpdateDoctor(long id, [FromBody] DocDoctor model)
    {
        using var conn = _db.CreateConnection();
        var existing = await conn.QuerySingleOrDefaultAsync<DocDoctor>(@"
            SELECT * FROM doc_doctors
            WHERE doctor_no = @Id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { Id = id, CurrentPharmacyNo, CurrentBranchNo });

        if (existing == null)
            return FailResponse<DocDoctor>("Doctor not found.", statusCode: 404);

        existing.FirstNameEn = !string.IsNullOrWhiteSpace(model.FirstNameEn) ? model.FirstNameEn : existing.FirstNameEn;
        existing.LastNameEn = model.LastNameEn ?? existing.LastNameEn;
        existing.FirstNameBn = model.FirstNameBn ?? existing.FirstNameBn;
        existing.LastNameBn = model.LastNameBn ?? existing.LastNameBn;
        existing.Phone = !string.IsNullOrWhiteSpace(model.Phone) ? model.Phone : existing.Phone;
        existing.Gender = model.Gender ?? existing.Gender;
        existing.DescriptionEn = model.DescriptionEn ?? existing.DescriptionEn;
        existing.DescriptionBn = model.DescriptionBn ?? existing.DescriptionBn;
        existing.SignatureImageUrl = model.SignatureImageUrl ?? existing.SignatureImageUrl;
        existing.IsActive = model.IsActive;
        existing.UpdatedAt = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            UPDATE doc_doctors
            SET first_name_en = @FirstNameEn, last_name_en = @LastNameEn,
                first_name_bn = @FirstNameBn, last_name_bn = @LastNameBn,
                phone = @Phone, gender = @Gender, description_en = @DescriptionEn,
                description_bn = @DescriptionBn, signature_image_url = @SignatureImageUrl,
                is_active = @IsActive, updated_at = @UpdatedAt
            WHERE doctor_no = @DoctorNo AND pharmacy_no = @PharmacyNo AND branch_no = @BranchNo;",
            existing);

        return OkResponse(existing, $"Doctor '{existing.FirstNameEn}' updated successfully.");
    }

    [HttpDelete("doctors/{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteDoctor(long id)
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.ExecuteAsync(@"
            DELETE FROM doc_doctors
            WHERE doctor_no = @Id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { Id = id, CurrentPharmacyNo, CurrentBranchNo });

        if (rows == 0) return NotFoundResponse<bool>("Doctor not found.");
        return OkResponse(true, "Doctor deleted successfully.");
    }

    [HttpGet("prescriptions")]
    public async Task<ActionResult<ApiResponse<PagedResult<DocPrescription>>>> GetPrescriptions(
        [FromQuery] QueryFilter filter,
        [FromQuery] long? doctorNo,
        [FromQuery] string? search)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT p.*, CONCAT(d.first_name_en, ' ', COALESCE(d.last_name_en, '')) AS doctor_name
            FROM doc_prescriptions p
            JOIN doc_doctors d ON p.doctor_no = d.doctor_no
            WHERE p.pharmacy_no = @CurrentPharmacyNo AND p.branch_no = @CurrentBranchNo";

        var countSql = @"
            SELECT COUNT(1)
            FROM doc_prescriptions p
            JOIN doc_doctors d ON p.doctor_no = d.doctor_no
            WHERE p.pharmacy_no = @CurrentPharmacyNo AND p.branch_no = @CurrentBranchNo";

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        if (doctorNo.HasValue)
        {
            sql += " AND p.doctor_no = @DoctorNo";
            countSql += " AND p.doctor_no = @DoctorNo";
            pms.Add("DoctorNo", doctorNo.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (p.prescription_code ILIKE @Search OR p.patient_name ILIKE @Search)";
            countSql += " AND (p.prescription_code ILIKE @Search OR p.patient_name ILIKE @Search)";
            pms.Add("Search", $"%{search.Trim()}%");
        }

        sql += " ORDER BY p.prescription_no DESC LIMIT @PageSize OFFSET @Offset;";
        pms.Add("PageSize", filter.PageSize);
        pms.Add("Offset", (filter.PageIndex - 1) * filter.PageSize);

        var total = await conn.ExecuteScalarAsync<int>(countSql, pms);
        var items = (await conn.QueryAsync<DocPrescription>(sql, pms)).ToList();

        var result = new PagedResult<DocPrescription>(items, total, filter.PageIndex, filter.PageSize);
        return PagedResponse(result);
    }

    [HttpPost("prescriptions")]
    public async Task<ActionResult<ApiResponse<DocPrescription>>> CreatePrescription([FromBody] DocPrescription model)
    {
        if (model.DoctorNo <= 0)
            return FailResponse<DocPrescription>("Doctor must be selected.");

        if (string.IsNullOrWhiteSpace(model.PatientName))
            return FailResponse<DocPrescription>("Patient name is required.");

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            var nextNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(prescription_no), 0) + 1 FROM doc_prescriptions;", transaction: tran);

            var code = $"RX-{DateTime.UtcNow:yyyyMMdd}-{nextNo:D4}";

            model.PrescriptionNo = nextNo;
            model.PharmacyNo = CurrentPharmacyNo;
            model.BranchNo = CurrentBranchNo;
            model.PrescriptionCode = code;
            model.CreatedAt = DateTime.UtcNow;

            await conn.ExecuteAsync(@"
                INSERT INTO doc_prescriptions (
                    prescription_no, prescription_code, pharmacy_no, branch_no,
                    doctor_no, customer_no, patient_name, patient_age,
                    patient_gender, clinical_notes, created_at
                ) VALUES (
                    @PrescriptionNo, @PrescriptionCode, @PharmacyNo, @BranchNo,
                    @DoctorNo, @CustomerNo, @PatientName, @PatientAge,
                    @PatientGender, @ClinicalNotes, @CreatedAt
                );", model, transaction: tran);

            if (model.Items != null && model.Items.Count > 0)
            {
                foreach (var item in model.Items)
                {
                    var nextItemNo = await conn.ExecuteScalarAsync<long>(
                        "SELECT COALESCE(MAX(prescription_item_no), 0) + 1 FROM doc_prescription_items;", transaction: tran);

                    await conn.ExecuteAsync(@"
                        INSERT INTO doc_prescription_items (
                            prescription_item_no, pharmacy_no, branch_no, prescription_no,
                            product_no, medicine_name, dosage, instructions, duration
                        ) VALUES (
                            @PrescriptionItemNo, @PharmacyNo, @BranchNo, @PrescriptionNo,
                            @ProductNo, @MedicineName, @Dosage, @Instructions, @Duration
                        );",
                        new
                        {
                            PrescriptionItemNo = nextItemNo,
                            PharmacyNo = CurrentPharmacyNo,
                            BranchNo = CurrentBranchNo,
                            PrescriptionNo = nextNo,
                            item.ProductNo,
                            item.MedicineName,
                            item.Dosage,
                            item.Instructions,
                            item.Duration
                        }, transaction: tran);
                }
            }

            tran.Commit();
            return OkResponse(model, $"Prescription {code} generated successfully.");
        }
        catch (Exception ex)
        {
            tran.Rollback();
            return FailResponse<DocPrescription>($"Prescription creation failed: {ex.Message}");
        }
    }

    [HttpGet("prescription-settings")]
    public async Task<ActionResult<ApiResponse<DocPrescriptionSettings>>> GetPrescriptionSettings()
    {
        using var conn = _db.CreateConnection();
        var settings = await conn.QuerySingleOrDefaultAsync<DocPrescriptionSettings>(@"
            SELECT * FROM doc_prescription_settings
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { CurrentPharmacyNo, CurrentBranchNo });

        if (settings == null)
        {
            settings = new DocPrescriptionSettings
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                PrescriptionBottomRowText = "For questions or refills, please contact the dispensary hotline.",
                UpdatedAt = DateTime.UtcNow
            };
        }

        return OkResponse(settings);
    }

    [HttpPut("prescription-settings")]
    public async Task<ActionResult<ApiResponse<DocPrescriptionSettings>>> UpdatePrescriptionSettings([FromBody] DocPrescriptionSettings model)
    {
        using var conn = _db.CreateConnection();
        var exists = await conn.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(
                SELECT 1 FROM doc_prescription_settings
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
            );", new { CurrentPharmacyNo, CurrentBranchNo });

        if (exists)
        {
            await conn.ExecuteAsync(@"
                UPDATE doc_prescription_settings
                SET clinic_logo_url = @ClinicLogoUrl,
                    prescription_bottom_row_text = @PrescriptionBottomRowText,
                    updated_at = NOW()
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { model.ClinicLogoUrl, model.PrescriptionBottomRowText, CurrentPharmacyNo, CurrentBranchNo });
        }
        else
        {
            var nextNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(prescription_setting_no), 0) + 1 FROM doc_prescription_settings;");

            await conn.ExecuteAsync(@"
                INSERT INTO doc_prescription_settings (
                    prescription_setting_no, pharmacy_no, branch_no,
                    clinic_logo_url, prescription_bottom_row_text, updated_at
                ) VALUES (
                    @NextNo, @CurrentPharmacyNo, @CurrentBranchNo,
                    @ClinicLogoUrl, @PrescriptionBottomRowText, NOW()
                );",
                new { NextNo = nextNo, CurrentPharmacyNo, CurrentBranchNo, model.ClinicLogoUrl, model.PrescriptionBottomRowText });
        }

        model.PharmacyNo = CurrentPharmacyNo;
        model.BranchNo = CurrentBranchNo;
        return OkResponse(model, "Prescription pad settings saved successfully.");
    }
}
