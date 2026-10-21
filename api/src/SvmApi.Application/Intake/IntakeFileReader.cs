using System.Text;
using System.Text.Json;

namespace SvmApi.Application.Intake;

public enum IntakeFileFormat
{
    Csv,
    Json,
}

/// <summary>ข้อผิดพลาดระดับไฟล์ (EX-11..14) — ไม่มีเนื้อหาไฟล์; <see cref="Fields"/> = ชื่อ column ที่ผิด (มาจาก template หรือถูกตัด/กรองแล้ว).</summary>
public sealed record FileError(string Code, string Message, IReadOnlyList<string> Fields)
{
    public FileError(string code, string message)
        : this(code, message, [])
    {
    }
}

/// <summary>แถวข้อมูลที่อ่านได้ พร้อมเลขแถว (CSV = เลขบรรทัดข้อมูลเริ่ม 1 ไม่นับ header; JSON = ลำดับใน array เริ่ม 1).</summary>
public sealed record ParsedRow(int Row, RawFindingRow Data);

public sealed record ParsedFile(IntakeFileFormat Format, IReadOnlyList<ParsedRow> Rows);

/// <summary>
/// อ่านไฟล์ import อย่างปลอดภัย (EX-11..14): ตรวจนามสกุล + เนื้อหาจริง (sniff — ต้องเป็น UTF-8 text, ไม่มี NUL/magic ของ binary,
/// JSON ต้อง parse ได้), จำกัดขนาด/จำนวนแถว/คอลัมน์/ความยาว cell, ตรวจ header ตาม <see cref="IntakeTemplate"/>.
/// ข้อความ error ไม่สะท้อนเนื้อหาไฟล์ (มีแค่ชื่อ column ของ template หรือชื่อที่ถูก sanitize แล้ว).
/// </summary>
public static class IntakeFileReader
{
    public const int MaxColumns = 50;
    public const int MaxCellLength = 4_000;
    public const int MaxJsonDepth = 8;

    private const int MaxReportedHeaderLength = 50;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static (ParsedFile? File, FileError? Error) Read(string? fileName, ReadOnlySpan<byte> content, long maxBytes, int maxRows)
    {
        var format = DetectFormatFromName(fileName);
        if (format is null)
        {
            return Fail(IntakeErrorCodes.UnsupportedFormat, "Only .csv and .json files are supported.");
        }

        if (content.Length == 0)
        {
            return Fail(IntakeErrorCodes.FileEmpty, "The file is empty.");
        }

        if (content.Length > maxBytes)
        {
            return Fail(IntakeErrorCodes.FileTooLarge, $"The file exceeds the maximum size of {maxBytes} bytes.");
        }

        if (LooksBinary(content))
        {
            return Fail(IntakeErrorCodes.UnsupportedFormat, "The file content is not a supported text format.");
        }

        var bytes = content;
        if (bytes.StartsWith(Encoding.UTF8.Preamble))
        {
            bytes = bytes[Encoding.UTF8.Preamble.Length..];
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Fail(IntakeErrorCodes.UnsupportedFormat, "The file must be UTF-8 encoded text.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return Fail(IntakeErrorCodes.FileEmpty, "The file is empty.");
        }

        // เนื้อหาต้องสอดคล้องกับนามสกุล (กันการเปลี่ยนนามสกุลหลบ)
        var first = text.TrimStart()[0];
        var looksJson = first is '[' or '{';
        if (format == IntakeFileFormat.Json && !looksJson)
        {
            return Fail(IntakeErrorCodes.UnsupportedFormat, "The file content does not match the .json extension.");
        }

        if (format == IntakeFileFormat.Csv && looksJson)
        {
            return Fail(IntakeErrorCodes.UnsupportedFormat, "The file content does not match the .csv extension.");
        }

        return format == IntakeFileFormat.Json ? ReadJson(text, maxRows) : ReadCsv(text, maxRows);
    }

    public static IntakeFileFormat? DetectFormatFromName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var ext = Path.GetExtension(fileName.Trim());
        if (string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase))
        {
            return IntakeFileFormat.Csv;
        }

        return string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase) ? IntakeFileFormat.Json : null;
    }

    /// <summary>ชื่อไฟล์ที่ปลอดภัยสำหรับเก็บ (ตัด path, control char, จำกัดความยาว). ไม่ใช้ใน log/audit.</summary>
    public static string SanitizeFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/').Split('/').LastOrDefault() ?? string.Empty);
        var cleaned = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length == 0)
        {
            cleaned = "upload";
        }

        return cleaned.Length > FindingLimits.FileName ? cleaned[..FindingLimits.FileName] : cleaned;
    }

    // ------------------------------------------------------------------ CSV

    private static (ParsedFile?, FileError?) ReadCsv(string text, int maxRows)
    {
        var (records, parseError) = ParseCsv(text, maxRows + 1);
        if (parseError is not null)
        {
            return (null, parseError);
        }

        // ตัดบรรทัดว่างล้วน
        var nonEmpty = records!.Where(r => r.Fields.Any(f => f.Length > 0)).ToList();
        if (nonEmpty.Count == 0)
        {
            return Fail(IntakeErrorCodes.FileEmpty, "The file is empty.");
        }

        var header = nonEmpty[0].Fields;
        var headerError = MapHeader(header, out var columnMap);
        if (headerError is not null)
        {
            return (null, headerError);
        }

        var dataRows = nonEmpty.Skip(1).ToList();
        if (dataRows.Count == 0)
        {
            return Fail(IntakeErrorCodes.FileEmpty, "The file has no data rows.");
        }

        if (dataRows.Count > maxRows)
        {
            return Fail(IntakeErrorCodes.TooManyRows, $"The file exceeds the maximum of {maxRows} data rows.");
        }

        var rows = new List<ParsedRow>(dataRows.Count);
        for (var i = 0; i < dataRows.Count; i++)
        {
            var fields = dataRows[i].Fields;
            if (fields.Count > header.Count && fields.Skip(header.Count).Any(f => f.Length > 0))
            {
                return Fail(
                    IntakeErrorCodes.InvalidStructure,
                    $"Data row {i + 1} has more columns than the header.");
            }

            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < columnMap.Count; c++)
            {
                values[columnMap[c]] = c < fields.Count ? fields[c] : null;
            }

            rows.Add(new ParsedRow(i + 1, ToRow(values)));
        }

        return (new ParsedFile(IntakeFileFormat.Csv, rows), null);
    }

    private static FileError? MapHeader(IReadOnlyList<string> header, out List<string> columnMap)
    {
        columnMap = new List<string>(header.Count);
        var unknown = new List<string>();
        var duplicate = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in header)
        {
            var name = IntakeTemplate.Canonical(raw);
            if (name is null)
            {
                unknown.Add(SafeHeader(raw));
                columnMap.Add(string.Empty);
                continue;
            }

            if (!seen.Add(name))
            {
                duplicate.Add(name);
            }

            columnMap.Add(name);
        }

        if (duplicate.Count > 0)
        {
            return new FileError(IntakeErrorCodes.DuplicateColumn, "The header contains duplicate columns.", duplicate.Distinct().ToList());
        }

        var missing = IntakeTemplate.RequiredNames.Where(r => !seen.Contains(r)).ToList();
        if (missing.Count > 0)
        {
            return new FileError(IntakeErrorCodes.MissingColumn, "The header is missing required columns.", missing);
        }

        if (unknown.Count > 0)
        {
            return new FileError(IntakeErrorCodes.UnknownColumn, "The header contains columns that are not in the template.", unknown.Distinct().Take(20).ToList());
        }

        return null;
    }

    /// <summary>ชื่อ header ที่ไม่รู้จัก: ตัดให้สั้น + เหลือเฉพาะตัวอักษร/ตัวเลข/_-. เพื่อรายงานได้โดยไม่สะท้อนเนื้อหาอันตราย.</summary>
    private static string SafeHeader(string? raw)
    {
        var s = new string((raw ?? string.Empty).Trim().Where(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or ' ').ToArray());
        if (s.Length == 0)
        {
            return "(blank)";
        }

        return s.Length > MaxReportedHeaderLength ? s[..MaxReportedHeaderLength] : s;
    }

    /// <summary>บันทึก CSV หนึ่งรายการ (ระเบียน) — อาจกินหลายบรรทัดถ้ามี newline ใน quote.</summary>
    public sealed record CsvRecord(IReadOnlyList<string> Fields);

    /// <summary>
    /// RFC 4180 parser: comma, double-quote escape (<c>""</c>), CRLF/LF, newline ใน quote ได้.
    /// หยุดเมื่อเกิน <paramref name="maxRecords"/> (คืนรายการที่อ่านได้ +1 เพื่อให้ผู้เรียกรู้ว่าเกิน).
    /// </summary>
    public static (IReadOnlyList<CsvRecord>? Records, FileError? Error) ParseCsv(string text, int maxRecords)
    {
        var records = new List<CsvRecord>();
        var fields = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        var cellWasQuoted = false;
        var i = 0;

        void EndCell()
        {
            fields.Add(cellWasQuoted ? cell.ToString() : cell.ToString().Trim());
            cell.Clear();
            cellWasQuoted = false;
        }

        // บรรทัดว่างล้วนไม่นับเป็น record (นับจำนวนแถวได้ถูกต้องเมื่อหยุดอ่านก่อนจบไฟล์)
        void EndRecord()
        {
            if (fields.Any(f => f.Length > 0))
            {
                records.Add(new CsvRecord(fields));
            }

            fields = [];
        }

        while (i < text.Length)
        {
            var ch = text[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i += 2;
                        continue;
                    }

                    inQuotes = false;
                    i++;
                    continue;
                }

                cell.Append(ch);
                i++;
            }
            else if (ch == '"')
            {
                if (cell.ToString().Trim().Length > 0)
                {
                    return (null, new FileError(IntakeErrorCodes.InvalidStructure, $"Malformed quoted value in record {records.Count + 1}."));
                }

                cell.Clear();
                inQuotes = true;
                cellWasQuoted = true;
                i++;
            }
            else if (ch == ',')
            {
                EndCell();
                i++;
            }
            else if (ch is '\r' or '\n')
            {
                EndCell();
                EndRecord();
                i += ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            }
            else
            {
                if (cellWasQuoted)
                {
                    // หลังปิด quote อนุญาตเฉพาะช่องว่างก่อน delimiter (ไม่นำมารวมในค่า)
                    if (!char.IsWhiteSpace(ch))
                    {
                        return (null, new FileError(IntakeErrorCodes.InvalidStructure, $"Malformed quoted value in record {records.Count + 1}."));
                    }
                }
                else
                {
                    cell.Append(ch);
                }

                i++;
            }

            if (cell.Length > MaxCellLength)
            {
                return (null, new FileError(IntakeErrorCodes.InvalidStructure, $"A value in record {records.Count + 1} exceeds {MaxCellLength} characters."));
            }

            if (fields.Count > MaxColumns)
            {
                return (null, new FileError(IntakeErrorCodes.InvalidStructure, $"Record {records.Count + 1} exceeds {MaxColumns} columns."));
            }

            if (records.Count > maxRecords)
            {
                return (records, null);
            }
        }

        if (inQuotes)
        {
            return (null, new FileError(IntakeErrorCodes.InvalidStructure, "The file ends inside a quoted value."));
        }

        if (cell.Length > 0 || fields.Count > 0 || cellWasQuoted)
        {
            EndCell();
            EndRecord();
        }

        return (records, null);
    }

    // ----------------------------------------------------------------- JSON

    private static (ParsedFile?, FileError?) ReadJson(string text, int maxRows)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = MaxJsonDepth, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException)
        {
            return Fail(IntakeErrorCodes.InvalidStructure, "The file is not valid JSON.");
        }

        using (doc)
        {
            var root = doc.RootElement;
            JsonElement array;
            if (root.ValueKind == JsonValueKind.Array)
            {
                array = root;
            }
            else if (root.ValueKind == JsonValueKind.Object
                     && TryGetPropertyIgnoreCase(root, "findings", out var findings)
                     && findings.ValueKind == JsonValueKind.Array)
            {
                array = findings;
            }
            else
            {
                return Fail(IntakeErrorCodes.InvalidStructure, "JSON must be an array of findings or an object with a 'findings' array.");
            }

            var count = array.GetArrayLength();
            if (count == 0)
            {
                return Fail(IntakeErrorCodes.FileEmpty, "The file has no data rows.");
            }

            if (count > maxRows)
            {
                return Fail(IntakeErrorCodes.TooManyRows, $"The file exceeds the maximum of {maxRows} data rows.");
            }

            var rows = new List<ParsedRow>(count);
            var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var index = 0;
            foreach (var item in array.EnumerateArray())
            {
                index++;
                if (item.ValueKind != JsonValueKind.Object)
                {
                    return Fail(IntakeErrorCodes.InvalidStructure, $"Item {index} is not a JSON object.");
                }

                var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in item.EnumerateObject())
                {
                    var name = IntakeTemplate.Canonical(prop.Name);
                    if (name is null)
                    {
                        unknown.Add(SafeHeader(prop.Name));
                        continue;
                    }

                    if (values.ContainsKey(name))
                    {
                        return (null, new FileError(IntakeErrorCodes.DuplicateColumn, $"Item {index} contains duplicate properties.", [name]));
                    }

                    var (value, ok) = ScalarToString(prop.Value);
                    if (!ok)
                    {
                        return (null, new FileError(IntakeErrorCodes.InvalidStructure, $"Item {index} has a non-scalar value.", [name]));
                    }

                    if (value is { Length: > MaxCellLength })
                    {
                        return (null, new FileError(IntakeErrorCodes.InvalidStructure, $"A value in item {index} exceeds {MaxCellLength} characters.", [name]));
                    }

                    values[name] = value;
                }

                rows.Add(new ParsedRow(index, ToRow(values)));
            }

            // EX-14: property ที่ไม่อยู่ใน template = โครงสร้างไม่ตรง (ไม่สร้างข้อมูลบางส่วน)
            if (unknown.Count > 0)
            {
                return (null, new FileError(IntakeErrorCodes.UnknownColumn, "The file contains properties that are not in the template.", unknown.Take(20).ToList()));
            }

            return (new ParsedFile(IntakeFileFormat.Json, rows), null);
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static (string? Value, bool Ok) ScalarToString(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => (e.GetString(), true),
        JsonValueKind.Number => (e.GetRawText(), true),
        JsonValueKind.True => ("true", true),
        JsonValueKind.False => ("false", true),
        JsonValueKind.Null => (null, true),
        _ => (null, false),
    };

    // ---------------------------------------------------------------- common

    private static RawFindingRow ToRow(IReadOnlyDictionary<string, string?> v)
    {
        string? Get(string key) => v.TryGetValue(key, out var s) ? s : null;

        return new RawFindingRow(
            Get(IntakeTemplate.AssetCode),
            Get(IntakeTemplate.SourceSystem),
            Get(IntakeTemplate.SourceRecordId),
            Get(IntakeTemplate.FindingName),
            Get(IntakeTemplate.Severity),
            Get(IntakeTemplate.CveId),
            Get(IntakeTemplate.CweId),
            Get(IntakeTemplate.CvssVersion),
            Get(IntakeTemplate.CvssBaseScore),
            Get(IntakeTemplate.CvssVector),
            Get(IntakeTemplate.Location),
            Get(IntakeTemplate.Port),
            Get(IntakeTemplate.Component),
            Get(IntakeTemplate.DetectedAt));
    }

    /// <summary>binary sniff: NUL byte ใน 8 KB แรก หรือ magic number ของ zip/xlsx/pdf/exe/gzip/ole.</summary>
    private static bool LooksBinary(ReadOnlySpan<byte> content)
    {
        ReadOnlySpan<byte> zip = [0x50, 0x4B, 0x03, 0x04];
        ReadOnlySpan<byte> pdf = "%PDF"u8;
        ReadOnlySpan<byte> exe = "MZ"u8;
        ReadOnlySpan<byte> gzip = [0x1F, 0x8B];
        ReadOnlySpan<byte> ole = [0xD0, 0xCF, 0x11, 0xE0];

        if (content.StartsWith(zip) || content.StartsWith(pdf) || content.StartsWith(exe)
            || content.StartsWith(gzip) || content.StartsWith(ole))
        {
            return true;
        }

        var probe = content[..Math.Min(content.Length, 8192)];
        return probe.Contains((byte)0);
    }

    private static (ParsedFile?, FileError?) Fail(string code, string message) => (null, new FileError(code, message));
}
