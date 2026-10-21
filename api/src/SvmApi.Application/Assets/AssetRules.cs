using System.Net;
using System.Text.RegularExpressions;
using SvmApi.Domain.Assets;

namespace SvmApi.Application.Assets;

public sealed record RuleResult<T>(T? Value, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>field ของ asset ที่ validate/normalize แล้ว (ใช้ร่วมกันทั้ง create/update/CMDB sync).</summary>
public sealed record AssetFields(
    string Name,
    AssetType AssetType,
    string? IpAddress,
    string? Hostname,
    string? Url,
    AssetEnvironment Environment,
    string? BusinessUnit,
    bool InternetExposure,
    string? CloudResourceId,
    string? RepositoryId);

public sealed record ValidOwner(OwnerType OwnerType, Guid? OwnerUserId, string? TeamName);

public sealed record NewAssetInput(string AssetCode, AssetFields Fields, IReadOnlyList<ValidOwner> Owners);

public sealed record UpdateAssetInput(AssetFields Fields, AssetStatus? Status, IReadOnlyList<ValidOwner>? Owners);

/// <summary>การกำหนด/ยืนยัน criticality ที่ผ่านกติกาแล้ว. <see cref="Changed"/> = false คือ "ยืนยัน" ค่าเดิม.</summary>
public sealed record CriticalityChange(AssetCriticality? Previous, AssetCriticality New, string Reason)
{
    public bool Changed => Previous != New;
}

public sealed record ParsedAssetQuery(
    string? Search,
    AssetType? AssetType,
    AssetEnvironment? Environment,
    string? BusinessUnit,
    AssetCriticality? Criticality,
    bool? InternetExposure,
    AssetStatus? Status,
    bool? CriticalityMissing,
    int Page,
    int PageSize);

/// <summary>เก็บ validation error แบบ field -> messages (รูปแบบเดียวกับ ValidationProblemDetails.errors).</summary>
public sealed class AssetErrorBag
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool HasErrors => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            list = [];
            _errors[field] = list;
        }

        list.Add(message);
    }

    public IReadOnlyDictionary<string, string[]> ToDictionary()
        => _errors.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
}

/// <summary>
/// กติกา domain ล้วน (ไม่แตะ DB) ของ AssetModule: validation, criticality rules, warning (BR-05/EX-28).
/// </summary>
public static class AssetRules
{
    public const int MaxAssetCodeLength = 100;
    public const int MaxNameLength = 300;
    public const int MaxIpLength = 45;
    public const int MaxHostnameLength = 300;
    public const int MaxUrlLength = 500;
    public const int MaxBusinessUnitLength = 200;
    public const int MaxExternalRefLength = 300;
    public const int MaxTeamNameLength = 200;
    public const int MaxReasonLength = 1000;
    public const int MaxOwners = 20;
    public const int MaxSearchLength = 100;
    public const int MaxPageSize = 100;

    private static readonly Regex AssetCodePattern =
        new(@"^[A-Za-z0-9][A-Za-z0-9._\-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HostnamePattern =
        new(@"^[A-Za-z0-9]([A-Za-z0-9._\-]*[A-Za-z0-9])?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // ---------- public validators ----------

    public static RuleResult<NewAssetInput> ValidateCreate(CreateAssetRequest? request)
    {
        var errors = new AssetErrorBag();
        if (request is null)
        {
            errors.Add("body", "Request body is required.");
            return new RuleResult<NewAssetInput>(null, errors.ToDictionary());
        }

        var code = ParseAssetCode(errors, "assetCode", request.AssetCode);
        var fields = ParseFields(
            errors, request.Name, request.AssetType, request.IpAddress, request.Hostname, request.Url,
            request.Environment, request.BusinessUnit, request.InternetExposure, request.CloudResourceId,
            request.RepositoryId);
        var owners = ParseOwners(errors, request.Owners) ?? [];

        return errors.HasErrors || code is null || fields is null
            ? new RuleResult<NewAssetInput>(null, errors.ToDictionary())
            : new RuleResult<NewAssetInput>(new NewAssetInput(code, fields, owners), Empty);
    }

    public static RuleResult<UpdateAssetInput> ValidateUpdate(UpdateAssetRequest? request)
    {
        var errors = new AssetErrorBag();
        if (request is null)
        {
            errors.Add("body", "Request body is required.");
            return new RuleResult<UpdateAssetInput>(null, errors.ToDictionary());
        }

        var fields = ParseFields(
            errors, request.Name, request.AssetType, request.IpAddress, request.Hostname, request.Url,
            request.Environment, request.BusinessUnit, request.InternetExposure, request.CloudResourceId,
            request.RepositoryId);

        AssetStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            status = ParseEnum<AssetStatus>(errors, "status", request.Status, required: false);
        }

        var owners = request.Owners is null ? null : ParseOwners(errors, request.Owners);

        return errors.HasErrors || fields is null
            ? new RuleResult<UpdateAssetInput>(null, errors.ToDictionary())
            : new RuleResult<UpdateAssetInput>(new UpdateAssetInput(fields, status, owners), Empty);
    }

    /// <summary>
    /// กติกา criticality (FR-04, BR-05): ต้องเป็น Critical/High/Medium/Low (ล้างค่าไม่ได้) และต้องมีเหตุผลเสมอ
    /// (รวมถึงการ "ยืนยัน" ค่าเดิม). ผู้แก้ไข/เวลาบันทึกโดยผู้เรียก (audit).
    /// </summary>
    public static RuleResult<CriticalityChange> ValidateCriticality(CriticalityRequest? request, AssetCriticality? current)
    {
        var errors = new AssetErrorBag();
        if (request is null)
        {
            errors.Add("body", "Request body is required.");
            return new RuleResult<CriticalityChange>(null, errors.ToDictionary());
        }

        var criticality = ParseEnum<AssetCriticality>(errors, "criticality", request.Criticality, required: true);
        var reason = Clean(request.Reason);

        if (reason is null)
        {
            errors.Add("reason", "reason is required.");
        }
        else if (reason.Length > MaxReasonLength)
        {
            errors.Add("reason", $"reason must not exceed {MaxReasonLength} characters.");
        }
        else if (HasControlChars(reason))
        {
            errors.Add("reason", "reason contains invalid characters.");
        }

        return errors.HasErrors || criticality is null || reason is null
            ? new RuleResult<CriticalityChange>(null, errors.ToDictionary())
            : new RuleResult<CriticalityChange>(new CriticalityChange(current, criticality.Value, reason), Empty);
    }

    public static RuleResult<ParsedAssetQuery> ValidateQuery(AssetQuery? query)
    {
        var errors = new AssetErrorBag();
        query ??= new AssetQuery(null, null, null, null, null, null, null, null);

        if (query.Page < 1)
        {
            errors.Add("page", "page must be >= 1.");
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            errors.Add("pageSize", $"pageSize must be between 1 and {MaxPageSize}.");
        }

        var search = Clean(query.Search);
        if (search is { Length: > MaxSearchLength })
        {
            errors.Add("q", $"q must not exceed {MaxSearchLength} characters.");
        }

        var businessUnit = Clean(query.BusinessUnit);
        if (businessUnit is { Length: > MaxBusinessUnitLength })
        {
            errors.Add("businessUnit", $"businessUnit must not exceed {MaxBusinessUnitLength} characters.");
        }

        var type = ParseEnum<AssetType>(errors, "assetType", query.AssetType, required: false);
        var env = ParseEnum<AssetEnvironment>(errors, "environment", query.Environment, required: false);
        var criticality = ParseEnum<AssetCriticality>(errors, "criticality", query.Criticality, required: false);
        var status = ParseEnum<AssetStatus>(errors, "status", query.Status, required: false);

        if (query.CriticalityMissing == true && criticality is not null)
        {
            errors.Add("criticalityMissing", "criticalityMissing=true cannot be combined with a criticality filter.");
        }

        return errors.HasErrors
            ? new RuleResult<ParsedAssetQuery>(null, errors.ToDictionary())
            : new RuleResult<ParsedAssetQuery>(
                new ParsedAssetQuery(
                    search, type, env, businessUnit, criticality, query.InternetExposure, status,
                    query.CriticalityMissing, query.Page, query.PageSize),
                Empty);
    }

    // ---------- BR-05 / EX-28 ----------

    /// <summary>asset ใน production scope (Prod + Active) ที่ยังไม่มี criticality.</summary>
    public static bool IsCriticalityMissing(AssetEnvironment environment, AssetStatus status, AssetCriticality? criticality)
        => criticality is null && environment == AssetEnvironment.Prod && status == AssetStatus.Active;

    public static IReadOnlyList<string> GetWarnings(
        AssetEnvironment environment, AssetStatus status, AssetCriticality? criticality, int ownerCount)
    {
        var warnings = new List<string>();
        if (IsCriticalityMissing(environment, status, criticality))
        {
            warnings.Add(AssetWarnings.CriticalityMissing);
        }

        if (ownerCount == 0)
        {
            warnings.Add(AssetWarnings.NoOwner);
        }

        return warnings;
    }

    // ---------- shared parsing (internal building blocks, also used by AssetSyncPlanner) ----------

    internal static readonly IReadOnlyDictionary<string, string[]> Empty = new Dictionary<string, string[]>();

    /// <summary>trim แล้วคืน null ถ้าว่าง — public เพราะ AssetService (Infrastructure) ใช้ normalize ค่าจาก CMDB feed.</summary>
    public static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    internal static bool HasControlChars(string value)
        => value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'));

    internal static string? ParseAssetCode(AssetErrorBag errors, string field, string? raw)
    {
        var value = Clean(raw);
        if (value is null)
        {
            errors.Add(field, $"{field} is required.");
            return null;
        }

        if (value.Length > MaxAssetCodeLength)
        {
            errors.Add(field, $"{field} must not exceed {MaxAssetCodeLength} characters.");
            return null;
        }

        if (!AssetCodePattern.IsMatch(value))
        {
            errors.Add(field, $"{field} may contain only letters, digits, '.', '_' and '-'.");
            return null;
        }

        return value;
    }

    /// <summary>enum ต้องเป็นชื่อ (ตัวพิมพ์ไม่สำคัญ) เท่านั้น — ไม่รับตัวเลข.</summary>
    internal static T? ParseEnum<T>(AssetErrorBag errors, string field, string? raw, bool required)
        where T : struct, Enum
    {
        var value = Clean(raw);
        if (value is null)
        {
            if (required)
            {
                errors.Add(field, $"{field} is required.");
            }

            return null;
        }

        if (char.IsLetter(value[0]) && Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        errors.Add(field, $"{field} must be one of: {string.Join(", ", Enum.GetNames<T>())}.");
        return null;
    }

    internal static string? ParseText(AssetErrorBag errors, string field, string? raw, int maxLength, bool required)
    {
        var value = Clean(raw);
        if (value is null)
        {
            if (required)
            {
                errors.Add(field, $"{field} is required.");
            }

            return null;
        }

        if (value.Length > maxLength)
        {
            errors.Add(field, $"{field} must not exceed {maxLength} characters.");
            return null;
        }

        if (HasControlChars(value))
        {
            errors.Add(field, $"{field} contains invalid characters.");
            return null;
        }

        return value;
    }

    /// <summary>validate field ของ asset ทั้งชุด; คืน null ถ้ามี error (error ถูกเติมลง <paramref name="errors"/>).</summary>
    internal static AssetFields? ParseFields(
        AssetErrorBag errors,
        string? name,
        string? assetType,
        string? ipAddress,
        string? hostname,
        string? url,
        string? environment,
        string? businessUnit,
        bool? internetExposure,
        string? cloudResourceId,
        string? repositoryId)
    {
        var parsedName = ParseText(errors, "name", name, MaxNameLength, required: true);
        var type = ParseEnum<AssetType>(errors, "assetType", assetType, required: true);
        var env = ParseEnum<AssetEnvironment>(errors, "environment", environment, required: true);
        var bu = ParseText(errors, "businessUnit", businessUnit, MaxBusinessUnitLength, required: false);
        var cloud = ParseText(errors, "cloudResourceId", cloudResourceId, MaxExternalRefLength, required: false);
        var repo = ParseText(errors, "repositoryId", repositoryId, MaxExternalRefLength, required: false);

        if (internetExposure is null)
        {
            errors.Add("internetExposure", "internetExposure is required.");
        }

        var ip = ParseText(errors, "ipAddress", ipAddress, MaxIpLength, required: false);
        if (ip is not null && !IsValidIp(ip))
        {
            errors.Add("ipAddress", "ipAddress must be a valid IPv4 or IPv6 address.");
        }

        var host = ParseText(errors, "hostname", hostname, MaxHostnameLength, required: false);
        if (host is not null && !HostnamePattern.IsMatch(host))
        {
            errors.Add("hostname", "hostname may contain only letters, digits, '.', '_' and '-'.");
        }

        var parsedUrl = ParseText(errors, "url", url, MaxUrlLength, required: false);
        if (parsedUrl is not null && !IsValidHttpUrl(parsedUrl))
        {
            errors.Add("url", "url must be an absolute http or https URL.");
        }

        if (errors.HasErrors || parsedName is null || type is null || env is null || internetExposure is null)
        {
            return null;
        }

        return new AssetFields(parsedName, type.Value, ip, host, parsedUrl, env.Value, bu, internetExposure.Value, cloud, repo);
    }

    internal static IReadOnlyList<ValidOwner>? ParseOwners(AssetErrorBag errors, IReadOnlyList<AssetOwnerInput>? inputs)
    {
        if (inputs is null)
        {
            return [];
        }

        if (inputs.Count > MaxOwners)
        {
            errors.Add("owners", $"owners must not contain more than {MaxOwners} items.");
            return null;
        }

        var result = new List<ValidOwner>(inputs.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ok = true;

        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            var prefix = $"owners[{i}]";

            if (input is null)
            {
                errors.Add(prefix, "owner entry must not be null.");
                ok = false;
                continue;
            }

            var type = ParseEnum<OwnerType>(errors, $"{prefix}.ownerType", input.OwnerType, required: true);
            var team = ParseText(errors, $"{prefix}.teamName", input.TeamName, MaxTeamNameLength, required: false);

            if (type is null)
            {
                ok = false;
                continue;
            }

            if (type == OwnerType.RemediationTeam)
            {
                if (team is null)
                {
                    errors.Add($"{prefix}.teamName", "teamName is required for ownerType RemediationTeam.");
                    ok = false;
                    continue;
                }
            }
            else if (input.OwnerUserId is null || input.OwnerUserId == Guid.Empty)
            {
                errors.Add($"{prefix}.ownerUserId", $"ownerUserId is required for ownerType {type}.");
                ok = false;
                continue;
            }

            var userId = input.OwnerUserId == Guid.Empty ? null : input.OwnerUserId;
            if (!seen.Add($"{type}|{userId}|{team}"))
            {
                errors.Add(prefix, "duplicate owner entry.");
                ok = false;
                continue;
            }

            result.Add(new ValidOwner(type.Value, userId, team));
        }

        return ok ? result : null;
    }

    private static bool IsValidIp(string value)
    {
        var isV6 = value.Contains(':');
        if (!isV6 && value.Count(c => c == '.') != 3)
        {
            return false;
        }

        return IPAddress.TryParse(value, out var ip)
               && ip.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork
                   or System.Net.Sockets.AddressFamily.InterNetworkV6;
    }

    private static bool IsValidHttpUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
