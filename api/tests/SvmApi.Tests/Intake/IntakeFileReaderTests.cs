using System.Text;
using SvmApi.Application.Intake;

namespace SvmApi.Tests.Intake;

/// <summary>อ่านไฟล์ import อย่างปลอดภัย (EX-11..14) + template (US-009) + กัน CSV formula injection.</summary>
public class IntakeFileReaderTests
{
    private const long MaxBytes = 1024 * 1024;
    private const int MaxRows = 10;

    private const string Header = "assetCode,sourceSystem,findingName,severity,port";

    private static (ParsedFile? File, FileError? Error) ReadText(string name, string text, int maxRows = MaxRows)
        => IntakeFileReader.Read(name, Encoding.UTF8.GetBytes(text), MaxBytes, maxRows);

    [Fact]
    public void Csv_Valid_ParsesRowsAndQuotedValues()
    {
        var csv = Header + "\r\n"
                  + "SVM-APP-001,Pentest,\"XSS, reflected \"\"q\"\" param\",High,443\r\n"
                  + "\r\n"
                  + "SVM-APP-002,Pentest,\"multi\nline\",low,\n";

        var (file, error) = ReadText("scan.CSV", csv);

        Assert.Null(error);
        Assert.Equal(IntakeFileFormat.Csv, file!.Format);
        Assert.Equal(2, file.Rows.Count);
        Assert.Equal(1, file.Rows[0].Row);
        Assert.Equal("XSS, reflected \"q\" param", file.Rows[0].Data.FindingName);
        Assert.Equal("443", file.Rows[0].Data.Port);
        Assert.Equal("multi\nline", file.Rows[1].Data.FindingName);
        Assert.Equal(string.Empty, file.Rows[1].Data.Port);
    }

    [Fact]
    public void Csv_HeaderIsCaseInsensitive_AndBomIsIgnored()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("ASSETCODE, SourceSystem ,findingname,Severity\nA,B,C,High\n")).ToArray();
        var (file, error) = IntakeFileReader.Read("x.csv", bytes, MaxBytes, MaxRows);

        Assert.Null(error);
        Assert.Equal("A", Assert.Single(file!.Rows).Data.AssetCode);
    }

    [Fact]
    public void Csv_MissingRequiredColumn_EX14()
    {
        var (_, error) = ReadText("x.csv", "assetCode,sourceSystem,findingName\nA,B,C\n");

        Assert.Equal(IntakeErrorCodes.MissingColumn, error!.Code);
        Assert.Equal(new[] { "severity" }, error.Fields);
    }

    [Fact]
    public void Csv_UnknownColumn_EX14_DoesNotEchoRawHeader()
    {
        var (_, error) = ReadText("x.csv", Header + ",=cmd|' /C calc'!A0\nA,B,C,High,1,x\n");

        Assert.Equal(IntakeErrorCodes.UnknownColumn, error!.Code);
        var reported = Assert.Single(error.Fields);
        Assert.DoesNotContain("=", reported, StringComparison.Ordinal);
        Assert.DoesNotContain("|", reported, StringComparison.Ordinal);
    }

    [Fact]
    public void Csv_DuplicateColumn_EX14()
    {
        var (_, error) = ReadText("x.csv", Header + ",port\nA,B,C,High,1,2\n");
        Assert.Equal(IntakeErrorCodes.DuplicateColumn, error!.Code);
    }

    [Fact]
    public void Csv_ExtraCells_AreStructureError()
    {
        var (_, error) = ReadText("x.csv", Header + "\nA,B,C,High,1,EXTRA\n");
        Assert.Equal(IntakeErrorCodes.InvalidStructure, error!.Code);
    }

    [Fact]
    public void Csv_UnterminatedQuote_IsStructureError()
    {
        var (_, error) = ReadText("x.csv", Header + "\nA,B,\"open,High,1\n");
        Assert.Equal(IntakeErrorCodes.InvalidStructure, error!.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n  ")]
    public void EmptyFile_EX13(string text)
    {
        var (_, error) = ReadText("x.csv", text);
        Assert.Equal(IntakeErrorCodes.FileEmpty, error!.Code);
    }

    [Fact]
    public void Csv_HeaderOnly_EX13()
    {
        var (_, error) = ReadText("x.csv", Header + "\r\n");
        Assert.Equal(IntakeErrorCodes.FileEmpty, error!.Code);
    }

    [Fact]
    public void TooManyRows_IsRejected_EvenWithBlankLines()
    {
        var sb = new StringBuilder(Header).Append('\n');
        for (var i = 0; i < 3; i++)
        {
            sb.Append("A,B,C,High,1\n\n\n");
        }

        var (_, error) = ReadText("x.csv", sb.ToString(), maxRows: 2);
        Assert.Equal(IntakeErrorCodes.TooManyRows, error!.Code);
    }

    [Fact]
    public void TooLarge_EX12()
    {
        var error = IntakeFileReader.Read("x.csv", new byte[100], maxBytes: 10, MaxRows).Error;
        Assert.Equal(IntakeErrorCodes.FileTooLarge, error!.Code);
    }

    [Theory]
    [InlineData("scan.xlsx")]
    [InlineData("scan.csv.exe")]
    [InlineData("scan")]
    [InlineData(null)]
    public void UnsupportedExtension_EX11(string? name)
    {
        var error = IntakeFileReader.Read(name, Encoding.UTF8.GetBytes(Header + "\nA,B,C,High,1\n"), MaxBytes, MaxRows).Error;
        Assert.Equal(IntakeErrorCodes.UnsupportedFormat, error!.Code);
    }

    [Fact]
    public void ContentMustMatchExtension_EX11()
    {
        Assert.Equal(IntakeErrorCodes.UnsupportedFormat, ReadText("x.csv", "[{\"assetCode\":\"A\"}]").Error!.Code);
        Assert.Equal(IntakeErrorCodes.UnsupportedFormat, ReadText("x.json", Header + "\nA,B,C,High,1\n").Error!.Code);
    }

    [Fact]
    public void BinaryOrNonUtf8_EX11()
    {
        byte[] zip = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00];
        byte[] withNul = [.. Encoding.UTF8.GetBytes("assetCode"), 0x00, 0x41];
        byte[] invalidUtf8 = [0x61, 0xC3, 0x28, 0x62];

        Assert.Equal(IntakeErrorCodes.UnsupportedFormat, IntakeFileReader.Read("x.csv", zip, MaxBytes, MaxRows).Error!.Code);
        Assert.Equal(IntakeErrorCodes.UnsupportedFormat, IntakeFileReader.Read("x.csv", withNul, MaxBytes, MaxRows).Error!.Code);
        Assert.Equal(IntakeErrorCodes.UnsupportedFormat, IntakeFileReader.Read("x.csv", invalidUtf8, MaxBytes, MaxRows).Error!.Code);
    }

    [Fact]
    public void Json_ArrayAndWrapper_AreAccepted()
    {
        const string item = "{\"AssetCode\":\"A\",\"sourceSystem\":\"S\",\"findingName\":\"N\",\"severity\":\"High\",\"port\":443,\"cvssBaseScore\":9.8,\"cveId\":null}";

        var array = ReadText("x.json", "[" + item + "]");
        var wrapper = ReadText("x.json", "{\"findings\":[" + item + "," + item + "]}");

        Assert.Null(array.Error);
        var row = Assert.Single(array.File!.Rows).Data;
        Assert.Equal("A", row.AssetCode);
        Assert.Equal("443", row.Port);
        Assert.Equal("9.8", row.CvssBaseScore);
        Assert.Null(row.CveId);
        Assert.Equal(2, wrapper.File!.Rows.Count);
        Assert.Equal(2, wrapper.File.Rows[1].Row);
    }

    [Theory]
    [InlineData("{\"x\":1}", IntakeErrorCodes.InvalidStructure)]
    [InlineData("[1,2]", IntakeErrorCodes.InvalidStructure)]
    [InlineData("[{\"assetCode\":{\"nested\":1}}]", IntakeErrorCodes.InvalidStructure)]
    [InlineData("[{\"assetCode\":\"A\",\"evil\":\"=1+1\"}]", IntakeErrorCodes.UnknownColumn)]
    [InlineData("[]", IntakeErrorCodes.FileEmpty)]
    [InlineData("[{\"assetCode\":\"A\"", IntakeErrorCodes.InvalidStructure)]
    public void Json_InvalidStructure_EX14(string json, string code)
        => Assert.Equal(code, ReadText("x.json", json).Error!.Code);

    [Fact]
    public void Json_DeepNesting_IsRejected()
    {
        var deep = "[{\"assetCode\":" + new string('[', 50) + new string(']', 50) + "}]";
        Assert.Equal(IntakeErrorCodes.InvalidStructure, ReadText("x.json", deep).Error!.Code);
    }

    [Fact]
    public void ErrorMessages_DoNotEchoFileContent()
    {
        const string secret = "TOPSECRET-VALUE";
        var (_, error) = ReadText("x.csv", Header + "\n\"" + secret + "\"x,B,C,High,1\n");

        Assert.NotNull(error);
        Assert.DoesNotContain(secret, error!.Message, StringComparison.Ordinal);
        Assert.All(error.Fields, f => Assert.DoesNotContain(secret, f, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("../../etc/passwd.csv", "passwd.csv")]
    [InlineData("C:\\temp\\scan.csv", "scan.csv")]
    [InlineData("a\u0000b.csv", "ab.csv")]
    [InlineData("", "upload")]
    public void SanitizeFileName_StripsPathAndControlChars(string raw, string expected)
        => Assert.Equal(expected, IntakeFileReader.SanitizeFileName(raw));

    // ------------------------------------------------------------ template + CSV safety

    [Fact]
    public void CsvTemplate_HeaderMatchesTemplateColumns_AndExampleValidates()
    {
        var bytes = IntakeTemplate.BuildCsv();
        var (file, error) = IntakeFileReader.Read("template.csv", bytes, MaxBytes, MaxRows);

        Assert.Null(error);
        var row = Assert.Single(file!.Rows).Data;
        var validation = FindingValidator.Validate(row, new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc));
        Assert.True(validation.IsValid, string.Join(";", validation.Errors.Select(e => e.Reason)));

        var headerLine = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Split("\r\n")[0];
        Assert.Equal(IntakeTemplate.Columns.Select(c => c.Name), headerLine.Split(','));
    }

    [Fact]
    public void Template_RequiredColumns_MatchManualMandatoryFields()
        => Assert.Equal(
            new[] { "assetCode", "sourceSystem", "findingName", "severity" },
            IntakeTemplate.RequiredNames);

    [Theory]
    [InlineData("=SUM(A1:A2)", "'=SUM(A1:A2)")]
    [InlineData("+1", "'+1")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("=HYPERLINK(\"x\",\"y\")", "\"'=HYPERLINK(\"\"x\"\",\"\"y\"\")\"")]
    [InlineData("plain", "plain")]
    [InlineData(null, "")]
    public void CsvSafety_EscapesFormulaAndQuotes(string? value, string expected)
        => Assert.Equal(expected, CsvSafety.EscapeCell(value));
}
