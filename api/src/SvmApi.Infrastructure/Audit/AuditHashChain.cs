using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SvmApi.Application.Audit;
using SvmApi.Domain.Audit;

namespace SvmApi.Infrastructure.Audit;

/// <summary>
/// คำนวณ/ตรวจ hash chain ของ trans_auditLog (FR-68, BR-33, NFR-20/21, EX-100). Pure logic ไม่แตะ DB.
/// RowHash = SHA-256 (hex lowercase 64 ตัว) ของ canonical string ที่ deterministic:
/// ทุกฟิลด์ถูก encode เป็น <c>{length}:{value}</c> (null = <c>-</c>) คั่นด้วย U+001F
/// จึงไม่เกิด ambiguity ที่ขอบฟิลด์ แม้ค่าจะมีตัวคั่นอยู่ข้างใน.
/// </summary>
public static class AuditHashChain
{
    /// <summary>เวอร์ชันของรูปแบบ canonical (ถ้าเปลี่ยนรูปแบบ ต้อง bump เพื่อไม่ชนกับ chain เดิม).</summary>
    public const string CanonicalVersion = "svm.audit.v1";

    /// <summary>ตัวคั่นฟิลด์ (Unit Separator).</summary>
    public const char FieldSeparator = '\u001f';

    /// <summary>ความยาว hex ของ SHA-256.</summary>
    public const int HashLength = 64;

    /// <summary>สร้าง canonical string ของ record (ใช้คำนวณ hash และใช้ในเทสต์).</summary>
    public static string BuildCanonical(
        Guid? userId,
        string action,
        string objectType,
        string objectId,
        string? beforeValue,
        string? afterValue,
        string result,
        string source,
        DateTime timestamp,
        string? prevHash)
    {
        var sb = new StringBuilder(256);
        AppendField(sb, CanonicalVersion);
        AppendField(sb, userId?.ToString("N", CultureInfo.InvariantCulture));
        AppendField(sb, action);
        AppendField(sb, objectType);
        AppendField(sb, objectId);
        AppendField(sb, beforeValue);
        AppendField(sb, afterValue);
        AppendField(sb, result);
        AppendField(sb, source);
        AppendField(sb, NormalizeTimestamp(timestamp).ToString("O", CultureInfo.InvariantCulture));
        AppendField(sb, prevHash);
        return sb.ToString();
    }

    /// <summary>คำนวณ RowHash (SHA-256 hex lowercase) จากฟิลด์ของ record.</summary>
    public static string ComputeRowHash(
        Guid? userId,
        string action,
        string objectType,
        string objectId,
        string? beforeValue,
        string? afterValue,
        string result,
        string source,
        DateTime timestamp,
        string? prevHash)
    {
        var canonical = BuildCanonical(
            userId, action, objectType, objectId, beforeValue, afterValue, result, source, timestamp, prevHash);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>คำนวณ RowHash จากแถวที่มีอยู่ (ไม่สนใจ <see cref="AuditLog.RowHash"/> เดิม).</summary>
    public static string ComputeRowHash(AuditLog row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return ComputeRowHash(
            row.UserId, row.Action, row.ObjectType, row.ObjectId, row.BeforeValue, row.AfterValue,
            row.Result, row.Source, row.Timestamp, row.PrevHash);
    }

    /// <summary>ตรวจแถวเดียว: RowHash ที่เก็บไว้ต้องตรงกับที่คำนวณจากฟิลด์ + PrevHash ของแถวนั้น.</summary>
    public static bool IsRowHashValid(AuditLog row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return !string.IsNullOrEmpty(row.RowHash)
               && string.Equals(row.RowHash, ComputeRowHash(row), StringComparison.Ordinal);
    }

    /// <summary>
    /// ตรวจลำดับแถว (ต้องเรียงตาม Id น้อย→มาก และเริ่มจากแถวแรกของตาราง): แถวแรกต้องมี PrevHash = null,
    /// แถวถัดไปต้องมี PrevHash = RowHash ของแถวก่อนหน้า และ RowHash ทุกแถวต้องตรง.
    /// คืน <see cref="AuditChainVerification.FirstBrokenId"/> = Id แถวแรกที่ผิด.
    /// </summary>
    public static AuditChainVerification VerifySequence(IEnumerable<AuditLog> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var verifier = new Verifier();
        foreach (var row in rows)
        {
            if (!verifier.Verify(row))
            {
                return new AuditChainVerification(false, verifier.CheckedCount, row.Id);
            }
        }

        return new AuditChainVerification(true, verifier.CheckedCount, null);
    }

    /// <summary>
    /// ตัวตรวจแบบ incremental (ใช้กับการอ่านเป็น batch โดยไม่โหลดทั้งตาราง).
    /// ป้อนแถวตามลำดับ Id; จำ RowHash ของแถวล่าสุดที่ผ่านไว้เป็น PrevHash ที่คาดหวัง.
    /// </summary>
    public sealed class Verifier
    {
        private string? _previousHash;

        /// <summary>จำนวนแถวที่ตรวจแล้ว (รวมแถวที่ผิดแถวแรก).</summary>
        public long CheckedCount { get; private set; }

        /// <summary>RowHash ของแถวล่าสุดที่ผ่านการตรวจ (null ถ้ายังไม่มี).</summary>
        public string? LastHash => _previousHash;

        /// <summary>คืน true ถ้าแถวนี้ต่อ chain ถูกต้องและ RowHash ตรง.</summary>
        public bool Verify(AuditLog row)
        {
            ArgumentNullException.ThrowIfNull(row);
            CheckedCount++;

            if (!string.Equals(row.PrevHash, _previousHash, StringComparison.Ordinal))
            {
                return false;
            }

            if (!IsRowHashValid(row))
            {
                return false;
            }

            _previousHash = row.RowHash;
            return true;
        }
    }

    /// <summary>
    /// ทำให้ timestamp เป็น UTC แบบ deterministic: Utc ใช้ตามเดิม, Local แปลงเป็น UTC,
    /// Unspecified (ค่าที่อ่านกลับจาก datetime2) ถือว่าเป็น UTC อยู่แล้ว.
    /// </summary>
    public static DateTime NormalizeTimestamp(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static void AppendField(StringBuilder sb, string? value)
    {
        if (sb.Length > 0)
        {
            sb.Append(FieldSeparator);
        }

        if (value is null)
        {
            sb.Append('-');
            return;
        }

        sb.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
    }
}
