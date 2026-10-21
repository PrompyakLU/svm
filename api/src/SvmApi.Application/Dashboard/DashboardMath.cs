using System.Text.Json;
using SvmApi.Application.Risk;

namespace SvmApi.Application.Dashboard;

/// <summary>
/// คณิตศาสตร์ล้วนของ dashboard (ไม่แตะ DB/เวลาปัจจุบัน): ขอบเขตสัปดาห์ของ trend, bucket อายุที่เกิน SLA, เปอร์เซ็นต์ SLA compliance.
/// SQL ทำ aggregate (GROUP BY) แล้วส่งตัวเลขมาจัดรูปที่นี่ — ไม่โหลดแถวดิบ.
/// </summary>
public static class DashboardMath
{
    public const int DefaultWeeks = 12;
    public const int MinWeeks = 4;
    public const int MaxWeeks = 26;

    public const int DefaultTopAssets = 10;
    public const int MaxTopAssets = 20;

    public const int DefaultListSize = 5;
    public const int MaxListSize = 20;

    /// <summary>ขอบบน (วัน) ของ bucket ที่เกิน SLA: 0-7 (รวมเกินไม่ถึงวัน), 8-30, 31-90, แล้ว 90+.</summary>
    public static readonly IReadOnlyList<int> OverdueBucketUpperBounds = [7, 30, 90];

    /// <summary>ชื่อ bucket เรียงตาม index (0..3) — ตรงกับ <see cref="OverdueBucketIndex"/>.</summary>
    public static readonly IReadOnlyList<string> OverdueBucketLabels = ["0-7", "8-30", "31-90", "90+"];

    /// <summary>จำนวนสัปดาห์ของ trend ต้องอยู่ในช่วง <see cref="MinWeeks"/>..<see cref="MaxWeeks"/>.</summary>
    public static bool IsValidWeeks(int weeks) => weeks is >= MinWeeks and <= MaxWeeks;

    public static bool IsValidTopAssets(int top) => top is >= 1 and <= MaxTopAssets;

    public static bool IsValidListSize(int size) => size is >= 1 and <= MaxListSize;

    /// <summary>วันจันทร์ 00:00 UTC ของสัปดาห์ที่มี <paramref name="date"/> (สัปดาห์ ISO เริ่มวันจันทร์).</summary>
    public static DateTime StartOfWeek(DateTime date)
    {
        var day = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        var offset = ((int)day.DayOfWeek + 6) % 7; // จันทร์=0 … อาทิตย์=6
        return day.AddDays(-offset);
    }

    /// <summary>จุดเริ่มหน้าต่าง trend = จันทร์ของสัปดาห์ปัจจุบันย้อนหลัง (<paramref name="weeks"/> − 1) สัปดาห์.</summary>
    public static DateTime TrendWindowStart(DateTime now, int weeks)
        => StartOfWeek(now).AddDays(-7 * (weeks - 1));

    /// <summary>
    /// index สัปดาห์ (0 = สัปดาห์แรกของหน้าต่าง) ของ <paramref name="value"/>; null เมื่ออยู่นอกหน้าต่าง.
    /// ตรงกับ <c>DateDiffDay(windowStart, value) / 7</c> ที่ฝั่ง SQL ใช้ (ค่าก่อนหน้าต่างต้องถูกกรองออกก่อน).
    /// </summary>
    public static int? WeekIndex(DateTime windowStart, int weeks, DateTime value)
    {
        var days = (int)Math.Floor((value.Date - windowStart.Date).TotalDays);
        if (days < 0)
        {
            return null;
        }

        var index = days / 7;
        return index < weeks ? index : null;
    }

    /// <summary>
    /// ประกอบ trend รายสัปดาห์ครบทุกสัปดาห์ (สัปดาห์ที่ไม่มีข้อมูล = 0) จากผล GROUP BY.
    /// index นอกช่วงถูกข้าม (กันข้อมูลนอกหน้าต่างทำให้ล้ม).
    /// </summary>
    public static IReadOnlyList<WeeklyTrendPoint> BuildWeeklyTrend(
        DateTime windowStart,
        int weeks,
        IReadOnlyDictionary<int, (long Total, long Critical)> created,
        IReadOnlyDictionary<int, long> resolved)
    {
        ArgumentNullException.ThrowIfNull(created);
        ArgumentNullException.ThrowIfNull(resolved);

        var points = new List<WeeklyTrendPoint>(Math.Max(0, weeks));
        for (var i = 0; i < weeks; i++)
        {
            created.TryGetValue(i, out var made);
            resolved.TryGetValue(i, out var done);
            points.Add(new WeeklyTrendPoint(windowStart.AddDays(7L * i), made.Total, made.Critical, done));
        }

        return points;
    }

    /// <summary>bucket (0..3) ของจำนวนวันที่เกิน SLA (0 = เกินไม่ถึงวัน); null เมื่อค่าติดลบ (ยังไม่เกินกำหนด).</summary>
    public static int? OverdueBucketIndex(int daysOverdue)
    {
        if (daysOverdue < 0)
        {
            return null;
        }

        for (var i = 0; i < OverdueBucketUpperBounds.Count; i++)
        {
            if (daysOverdue <= OverdueBucketUpperBounds[i])
            {
                return i;
            }
        }

        return OverdueBucketUpperBounds.Count;
    }

    /// <summary>จัด bucket ครบทุกช่วง (ช่วงที่ไม่มีข้อมูล = 0) จากผล GROUP BY ตาม index.</summary>
    public static IReadOnlyList<CountDto> BuildOverdueAging(IReadOnlyDictionary<int, long> byBucketIndex)
    {
        ArgumentNullException.ThrowIfNull(byBucketIndex);

        var result = new List<CountDto>(OverdueBucketLabels.Count);
        for (var i = 0; i < OverdueBucketLabels.Count; i++)
        {
            byBucketIndex.TryGetValue(i, out var count);
            result.Add(new CountDto(OverdueBucketLabels[i], count));
        }

        return result;
    }

    /// <summary>
    /// SLA compliance % = สัดส่วน instance ที่มี timer และ "ไม่เกินกำหนด" (หยุดนับโดยไม่เกิน + กำลังนับที่ยังไม่เกิน) ต่อทั้งหมดที่มี timer.
    /// null เมื่อไม่มี timer เลย (หลีกเลี่ยงการหารด้วยศูนย์/โชว์ 100% ปลอม). ปัดทศนิยม 1 ตำแหน่ง.
    /// </summary>
    public static decimal? CompliancePercent(long withinSla, long totalWithTimer)
    {
        if (totalWithTimer <= 0)
        {
            return null;
        }

        var clamped = Math.Clamp(withinSla, 0, totalWithTimer);
        return Math.Round(clamped * 100m / totalWithTimer, 1, MidpointRounding.AwayFromZero);
    }

    private static readonly JsonSerializerOptions BreakdownJson = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// อ่าน factor ของ <c>trans_riskScoreHistory.factorBreakdown</c> (JSON ของ <see cref="RiskBreakdown"/>).
    /// ว่าง/JSON เสีย/ไม่มี factors (เช่น การปรับด้วยมือ) = รายการว่าง (ไม่ throw); จำกัดไม่เกิน 50 ปัจจัย.
    /// </summary>
    public static IReadOnlyList<RiskFactorDto> ParseRiskFactors(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var breakdown = JsonSerializer.Deserialize<RiskBreakdown>(json, BreakdownJson);
            if (breakdown?.Factors is null)
            {
                return [];
            }

            return breakdown.Factors
                .Where(f => f is not null && !string.IsNullOrWhiteSpace(f.Factor))
                .Take(50)
                .Select(f => new RiskFactorDto(f.Factor, f.Weight, f.Normalized, f.Contribution, f.Source))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>เติมค่า 0 ให้ทุก key ของ enum (เรียงตามลำดับ enum) แล้วเติมผล GROUP BY.</summary>
    public static IReadOnlyList<CountDto> ZeroFill<TEnum>(IReadOnlyDictionary<TEnum, long> counts)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(counts);

        return Enum.GetValues<TEnum>()
            .Select(v => new CountDto(v.ToString(), counts.TryGetValue(v, out var n) ? n : 0))
            .ToList();
    }
}
