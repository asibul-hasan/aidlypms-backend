using System.Text.Json.Serialization;

namespace AidlyPms.Api.Modules.Doctors.Models;

public class DocDoctor
{
    [JsonPropertyName("doctor_no")]
    public long DoctorNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("first_name_en")]
    public string FirstNameEn { get; set; } = string.Empty;

    [JsonPropertyName("last_name_en")]
    public string? LastNameEn { get; set; }

    [JsonPropertyName("first_name_bn")]
    public string? FirstNameBn { get; set; }

    [JsonPropertyName("last_name_bn")]
    public string? LastNameBn { get; set; }

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonPropertyName("gender")]
    public short? Gender { get; set; } = 1;

    [JsonPropertyName("description_en")]
    public string? DescriptionEn { get; set; }

    [JsonPropertyName("description_bn")]
    public string? DescriptionBn { get; set; }

    [JsonPropertyName("signature_image_url")]
    public string? SignatureImageUrl { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class DocPrescriptionSettings
{
    [JsonPropertyName("prescription_setting_no")]
    public long PrescriptionSettingNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("clinic_logo_url")]
    public string? ClinicLogoUrl { get; set; }

    [JsonPropertyName("prescription_bottom_row_text")]
    public string? PrescriptionBottomRowText { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class DocPrescription
{
    [JsonPropertyName("prescription_no")]
    public long PrescriptionNo { get; set; }

    [JsonPropertyName("prescription_code")]
    public string PrescriptionCode { get; set; } = string.Empty;

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("doctor_no")]
    public long DoctorNo { get; set; }

    [JsonPropertyName("customer_no")]
    public long? CustomerNo { get; set; }

    [JsonPropertyName("patient_name")]
    public string PatientName { get; set; } = string.Empty;

    [JsonPropertyName("patient_age")]
    public string? PatientAge { get; set; }

    [JsonPropertyName("patient_gender")]
    public short? PatientGender { get; set; } = 1;

    [JsonPropertyName("clinical_notes")]
    public string? ClinicalNotes { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Joined
    [JsonPropertyName("doctor_name")]
    public string? DoctorName { get; set; }

    [JsonPropertyName("items")]
    public List<DocPrescriptionItem>? Items { get; set; }
}

public class DocPrescriptionItem
{
    [JsonPropertyName("prescription_item_no")]
    public long PrescriptionItemNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("prescription_no")]
    public long PrescriptionNo { get; set; }

    [JsonPropertyName("product_no")]
    public long? ProductNo { get; set; }

    [JsonPropertyName("medicine_name")]
    public string MedicineName { get; set; } = string.Empty;

    [JsonPropertyName("dosage")]
    public string Dosage { get; set; } = string.Empty;

    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }

    [JsonPropertyName("duration")]
    public string? Duration { get; set; }
}
