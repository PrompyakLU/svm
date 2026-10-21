namespace SvmApi.Infrastructure.Persistence.Seed;

/// <summary>
/// ตัวเลือก seed ข้อมูลเริ่มต้น (section <c>Seed</c>) · FR-64/65/66, NFR-16.
/// ค่า <see cref="DemoPassword"/> ต้องมาจาก env var / user-secrets เท่านั้น (<c>Seed__DemoPassword</c>) ห้าม commit ลง appsettings.
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>เปิด/ปิดการ seed ตอน API start (default = ปิด).</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// รหัสผ่านของ demo users — ถ้าว่างจะไม่สร้าง demo users/roles ของผู้ใช้/scope/asset ownership
    /// (ส่วนที่เหลือ เช่น permission, role, reference, config, asset ยัง seed ตามปกติ).
    /// </summary>
    public string? DemoPassword { get; set; }
}
