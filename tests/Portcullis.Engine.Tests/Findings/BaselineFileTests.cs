using Portcullis.Engine.Findings;
using Portcullis.Engine.Model;

namespace Portcullis.Engine.Tests.Findings;

/// <summary>
/// The baseline file: what is written is read back as the same accepted set, the file is
/// stable enough to review in a diff, and every way it can be unusable is an error that says
/// what to do — never a quietly empty baseline.
/// </summary>
public class BaselineFileTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("portcullis-baseline-test-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string PathOf(string name) => Path.Combine(_dir.FullName, name);

    private static Violation Finding(string file, int line, string fingerprint, string rule = "PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT") =>
        new(rule, file, line, $"a finding at {file}:{line}", "error", fingerprint);

    [Fact]
    public void Write_ThenRead_AcceptsExactlyTheWrittenFindings()
    {
        var path = PathOf("baseline.json");
        var written = BaselineFile.Write(path, [Finding("B.cs", 3, "bbbb"), Finding("A.cs", 7, "aaaa")]);

        var baseline = BaselineFile.Read(path);

        Assert.Equal(2, written);
        Assert.Equal(2, baseline.Count);
        Assert.Equal(path, baseline.Path);
        Assert.False(baseline.AcceptsEverything);
        Assert.True(baseline.Accepts("aaaa"));
        Assert.True(baseline.Accepts("bbbb"));
        Assert.False(baseline.Accepts("cccc"));
        Assert.False(baseline.Accepts(null));
    }

    [Fact]
    public void Write_ProducesAReviewableFile_SortedWithUnixNewlinesAndTheContextBesideEachFingerprint()
    {
        var path = PathOf("baseline.json");
        BaselineFile.Write(path, [
            Finding("B.cs", 3, "bbbb"),
            Finding("A.cs", 9, "a-nine"),
            Finding("A.cs", 2, "a-two"),
        ]);

        var text = File.ReadAllText(path);

        Assert.DoesNotContain("\r", text);
        Assert.EndsWith("}\n", text);
        Assert.Contains("\"schemaVersion\": \"1.0.0\"", text);
        Assert.Contains($"\"fingerprintVersion\": \"{FindingFingerprint.Version}\"", text);
        Assert.Contains("\"message\": \"a finding at A.cs:2\"", text);
        Assert.Contains("\"ruleId\": \"PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT\"", text);
        Assert.True(
            text.IndexOf("a-two", StringComparison.Ordinal) < text.IndexOf("a-nine", StringComparison.Ordinal)
            && text.IndexOf("a-nine", StringComparison.Ordinal) < text.IndexOf("bbbb", StringComparison.Ordinal),
            "entries are sorted by file, then line");
    }

    [Fact]
    public void Write_TheSameFindingsInAnyOrder_ProducesTheSameBytes()
    {
        var one = PathOf("one.json");
        var two = PathOf("two.json");
        BaselineFile.Write(one, [Finding("A.cs", 1, "x"), Finding("B.cs", 1, "y")]);
        BaselineFile.Write(two, [Finding("B.cs", 1, "y"), Finding("A.cs", 1, "x")]);

        Assert.Equal(File.ReadAllBytes(one), File.ReadAllBytes(two));
    }

    [Fact]
    public void Write_CreatesTheDirectoryIfItDoesNotExist()
    {
        var path = PathOf(Path.Combine("nested", "deeper", "baseline.json"));

        BaselineFile.Write(path, [Finding("A.cs", 1, "x")]);

        Assert.True(BaselineFile.Read(path).Accepts("x"));
    }

    [Fact]
    public void Write_RefusesAViolationWithoutAFingerprint()
    {
        // It could never be matched when read back, so writing it would record an
        // acceptance that does not work.
        var unfingerprinted = new Violation("PORTCULLIS_P10_CUSTOM_BASE_CLASS", "A.cs", 1, "m", "warning");

        Assert.Throws<ArgumentException>(() => BaselineFile.Write(PathOf("baseline.json"), [unfingerprinted]));
    }

    [Fact]
    public void Read_MissingFile_SaysHowToCreateOne()
    {
        var error = Assert.Throws<BaselineFileException>(() => BaselineFile.Read(PathOf("absent.json")));

        Assert.Contains("does not exist", error.Message);
        Assert.Contains("--write-baseline", error.Message);
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("null", "JSON null")]
    [InlineData("""{"schemaVersion":"2.0.0","fingerprintVersion":"portcullisFingerprint/v1","findings":[]}""", "schemaVersion '2.0.0'")]
    [InlineData("""{"fingerprintVersion":"portcullisFingerprint/v1","findings":[]}""", "schemaVersion ''")]
    [InlineData("""{"schemaVersion":"1.0.0","fingerprintVersion":"portcullisFingerprint/v0","findings":[]}""", "'portcullisFingerprint/v0' fingerprints")]
    [InlineData("""{"schemaVersion":"1.0.0","fingerprintVersion":"portcullisFingerprint/v1"}""", "no \"findings\" array")]
    [InlineData("""{"schemaVersion":"1.0.0","fingerprintVersion":"portcullisFingerprint/v1","findings":[{"fingerprint":"x"},{"ruleId":"R"}]}""", "findings[1]")]
    [InlineData("""{"schemaVersion":"1.0.0","fingerprintVersion":"portcullisFingerprint/v1","findings":[null]}""", "findings[0]")]
    public void Read_AnUnusableFile_IsAnErrorNamingWhatIsWrong(string content, string expected)
    {
        var path = PathOf("baseline.json");
        File.WriteAllText(path, content);

        var error = Assert.Throws<BaselineFileException>(() => BaselineFile.Read(path));

        Assert.Contains(expected, error.Message);
        Assert.Contains(path, error.Message);
    }

    [Fact]
    public void Read_AcceptsANewerMinorVersionOfTheFormat()
    {
        var path = PathOf("baseline.json");
        File.WriteAllText(path, """
            {"schemaVersion":"1.3.0","fingerprintVersion":"portcullisFingerprint/v1","findings":[{"fingerprint":"x","somethingNew":true}]}
            """);

        Assert.True(BaselineFile.Read(path).Accepts("x"));
    }

    [Fact]
    public void Everything_AcceptsAnyFindingAndHasNoCount()
    {
        var baseline = Baseline.Everything("written-later.json");

        Assert.True(baseline.AcceptsEverything);
        Assert.Null(baseline.Count);
        Assert.True(baseline.Accepts("anything"));
        Assert.Equal("written-later.json", baseline.Path);
    }
}
