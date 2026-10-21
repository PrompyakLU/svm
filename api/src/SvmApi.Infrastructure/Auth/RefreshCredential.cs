using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace SvmApi.Infrastructure.Auth;

/// <summary>
/// Refresh credential = session id + secret สุ่มแบบ crypto (US-001/003, docs/authentication-server.md).
/// รูปแบบ token: <c>{sessionId:N}.{secret base64url 32 ไบต์}</c>. ฝั่ง server เก็บเฉพาะ SHA-256 hash ของ secret.
/// Pure logic — ไม่แตะ DB/เวลา.
/// </summary>
public sealed record RefreshCredential(Guid SessionId, string Secret)
{
    public const int SecretByteLength = 32;
    private const int MaxTokenLength = 128;

    /// <summary>สร้าง credential ใหม่ด้วย secret สุ่มใหม่ทุกครั้ง.</summary>
    public static RefreshCredential Generate(Guid sessionId)
    {
        Span<byte> bytes = stackalloc byte[SecretByteLength];
        RandomNumberGenerator.Fill(bytes);
        return new RefreshCredential(sessionId, Base64Url.EncodeToString(bytes));
    }

    /// <summary>ค่าที่ส่งให้ client (ใส่ cookie เท่านั้น).</summary>
    public string Encode() => $"{SessionId:N}.{Secret}";

    /// <summary>SHA-256 (hex ตัวพิมพ์เล็ก 64 ตัวอักษร) ของ secret — ค่าที่เก็บใน DB.</summary>
    public string ComputeHash() => HashSecret(Secret);

    public static string HashSecret(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    /// <summary>แยก token; คืน false ถ้ารูปแบบไม่ถูกต้อง (ไม่ throw).</summary>
    public static bool TryParse(string? token, [NotNullWhen(true)] out RefreshCredential? credential)
    {
        credential = null;

        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength)
        {
            return false;
        }

        var separator = token.IndexOf('.');
        if (separator != 32 || token.IndexOf('.', separator + 1) >= 0)
        {
            return false;
        }

        if (!Guid.TryParseExact(token.AsSpan(0, separator), "N", out var sessionId) || sessionId == Guid.Empty)
        {
            return false;
        }

        var secret = token.AsSpan(separator + 1);
        if (!Base64Url.IsValid(secret, out var decodedLength) || decodedLength != SecretByteLength)
        {
            return false;
        }

        credential = new RefreshCredential(sessionId, secret.ToString());
        return true;
    }

    /// <summary>เทียบ hash แบบ constant-time (กัน timing attack). <paramref name="stored"/> เป็น null = ไม่ตรง.</summary>
    public static bool HashEquals(string? stored, string candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (stored is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(stored),
            Encoding.UTF8.GetBytes(candidate));
    }

    /// <summary>ไม่พิมพ์ secret ลง log.</summary>
    public override string ToString() => $"RefreshCredential {{ SessionId = {SessionId} }}";
}
