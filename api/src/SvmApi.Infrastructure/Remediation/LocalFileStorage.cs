using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SvmApi.Application.Remediation;

namespace SvmApi.Infrastructure.Remediation;

/// <summary>
/// <see cref="IFileStorage"/> บนระบบไฟล์ท้องถิ่น (mock/dev): เก็บนอก web root, key ต้องเป็นรูป
/// <c>{32 hex}/{32 hex}.{ext}</c> ที่ service สร้างเอง (ไม่มีชื่อไฟล์ผู้ใช้), resolve path แล้วต้องอยู่ใต้ root เท่านั้น,
/// เขียนด้วย <see cref="FileMode.CreateNew"/> (ไม่ทับไฟล์เดิม). ไม่ log เนื้อหา.
/// </summary>
public sealed partial class LocalFileStorage : IFileStorage
{
    [GeneratedRegex(@"^[0-9a-f]{32}/[0-9a-f]{32}\.[a-z0-9]{2,5}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    private readonly string _root;

    public LocalFileStorage(IOptions<EvidenceOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _root = ResolveRoot(options.Value.StorageRoot);
    }

    public async Task SaveAsync(string storageKey, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        var path = ResolvePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var stream = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920, FileOptions.Asynchronous);
        await stream.WriteAsync(content, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = ResolvePath(storageKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <summary>validate key + ตรวจว่า path เต็มอยู่ใต้ root (กัน traversal/symlink-ish ผ่านการ normalize).</summary>
    private string ResolvePath(string storageKey)
    {
        if (string.IsNullOrEmpty(storageKey) || !KeyPattern().IsMatch(storageKey))
        {
            throw new ArgumentException("Invalid storage key.", nameof(storageKey));
        }

        var full = Path.GetFullPath(Path.Combine(_root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invalid storage key.", nameof(storageKey));
        }

        return full;
    }

    private static string ResolveRoot(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = Path.GetTempPath();
        }

        return Path.GetFullPath(Path.Combine(baseDir, "svm", "evidence"));
    }
}
