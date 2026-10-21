using System.Text;
using Microsoft.Extensions.Options;
using SvmApi.Application.Auth;

namespace SvmApi.Api.Auth;

/// <summary>
/// ตรวจ <see cref="JwtOptions"/> ตอน startup: ต้องมี signing key ยาว &gt;= 32 ไบต์ (NFR security).
/// ข้อความ error ไม่พิมพ์ค่า key.
/// </summary>
public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public const int MinSigningKeyBytes = 32;

    /// <summary>จำนวนอักขระต่างกันขั้นต่ำ — กัน key ซ้ำ ๆ เช่น "aaaa..." / "1234123412..." ที่ยาวพอแต่คาดเดาง่าย.</summary>
    public const int MinDistinctSigningKeyChars = 8;

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            failures.Add(
                $"Auth:Jwt:SigningKey is required. Set it with user-secrets or the environment variable Auth__Jwt__SigningKey (at least {MinSigningKeyBytes} bytes).");
        }
        else if (Encoding.UTF8.GetByteCount(options.SigningKey) < MinSigningKeyBytes)
        {
            failures.Add($"Auth:Jwt:SigningKey must be at least {MinSigningKeyBytes} bytes long.");
        }
        else if (options.SigningKey.Distinct().Count() < MinDistinctSigningKeyChars)
        {
            failures.Add("Auth:Jwt:SigningKey is too predictable; use a random value (for example 48 random bytes, base64).");
        }

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add("Auth:Jwt:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Auth:Jwt:Audience is required.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>ตรวจ <see cref="AuthOptions"/> ตอน startup (อายุ token/session ต้องเป็นบวก).</summary>
public sealed class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        var failures = new List<string>();

        if (options.AccessTokenMinutes <= 0)
        {
            failures.Add("Auth:AccessTokenMinutes must be greater than 0.");
        }

        if (options.IdleTimeoutMinutes <= 0)
        {
            failures.Add("Auth:IdleTimeoutMinutes must be greater than 0.");
        }

        if (options.AbsoluteSessionHours <= 0)
        {
            failures.Add("Auth:AbsoluteSessionHours must be greater than 0.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
