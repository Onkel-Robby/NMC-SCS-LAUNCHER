using System.Text;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed partial class ScsTruckPowertrainSnapshotReaderTests
{
    [Theory]
    [InlineData("player: .player", "player: \"PRIVATE_VALUE\"")]
    [InlineData("trucks[0]: .truck", "trucks[0]: \"PRIVATE_VALUE\"")]
    [InlineData("data_path: \"/def/vehicle/truck/test/engine/old.sii\"", "data_path: PRIVATE_VALUE")]
    public void TypedFieldFailuresDoNotExposePrivateUnitIdsOrValues(string before, string after)
    {
        var text = ValidSave().Replace(before, after, StringComparison.Ordinal)
            .Replace(".economy", ".PRIVATE_ECONOMY", StringComparison.Ordinal)
            .Replace(".player", ".PRIVATE_PLAYER", StringComparison.Ordinal)
            .Replace(".engine", ".PRIVATE_ENGINE", StringComparison.Ordinal);
        var error = Assert.Throws<ScsSaveEditException>(() => Parse(text));
        Assert.DoesNotContain("PRIVATE", error.ToString());
        Assert.Contains("Unit-Typ", error.Message);
    }

    [Fact]
    public void LargeRequiredUnitReportsOnlyTypeExactNormalizedSizesAndPhases()
    {
        var unit = "economy : .economy {\n player: .player\n ignored: \"" + new string('x', 9 * 1024 * 1024) + "ä\"\n}";
        var text = ValidSave().Replace("economy : .economy {\n player: .player\n}", unit, StringComparison.Ordinal);
        var snapshot = Parse(text.ReplaceLineEndings("\r\n"));
        Assert.NotNull(snapshot.UnitReadDiagnostics);
        Assert.Equal(2, snapshot.UnitReadDiagnostics.Count);
        Assert.All(snapshot.UnitReadDiagnostics, item =>
        {
            Assert.Equal("economy", item.UnitType);
            Assert.Equal(unit.Length, item.Characters);
            Assert.Equal(Encoding.UTF8.GetByteCount(unit), item.Utf8Bytes);
        });
        Assert.Equal(new[] { ScsSaveUnitReadPhase.StructureValidation, ScsSaveUnitReadPhase.FieldProjection },
            snapshot.UnitReadDiagnostics.Select(item => item.Phase));
    }

    [Theory]
    [InlineData(1_100_000)]
    [InlineData(5_000_000)]
    public void ExcessiveRequiredValuesFailWithSafeUnitSizeAndProjectionPhase(int length)
    {
        var text = ValidSave().Replace("/def/vehicle/truck/test/engine/old.sii",
            "/def/vehicle/truck/test/engine/PRIVATE_VALUE_" + new string('x', length) + ".sii", StringComparison.Ordinal)
            .Replace(".engine", ".private_unit_id", StringComparison.Ordinal);
        var error = Assert.Throws<ScsSaveEditException>(() => Parse(text));
        Assert.Contains("Unit-Typ vehicle_accessory", error.Message);
        Assert.Contains("UTF-8-Bytes", error.Message);
        Assert.Contains("FieldProjection", error.Message);
        Assert.DoesNotContain("PRIVATE_VALUE", error.ToString());
        Assert.DoesNotContain("private_unit_id", error.ToString());
    }

    [Theory]
    [InlineData(508)]
    [InlineData(509)]
    [InlineData(510)]
    [InlineData(511)]
    public void IgnoredStringsValidateUtf8AcrossBufferBoundaries(int prefix)
    {
        var padding = new string('x', prefix);
        var text = AddUnit(ValidSave(), "unrelated : .ignored {\n value: \"" + padding + "\\xf0\\x9f\\x9a\\x9aä\"\n}");
        Assert.Equal(".truck", Parse(text).TruckId);
        Assert.Throws<ScsSaveEditException>(() => Parse(text.Replace("\\x9aä", "ä", StringComparison.Ordinal)));
    }

    [Fact]
    public void ArrayValidationHasAnAggregateBitmapBudget()
    {
        var text = AddUnit(ValidSave(), "unrelated : .ignored {\n" + string.Join('\n',
            Enumerable.Range(0, 5).Select(index => $" family_{index}[1999999]: 0")) + "\n}");
        var error = Assert.Throws<ScsSaveEditException>(() => Parse(text));
        Assert.Contains("Speicherlimit", error.Message);
    }

    [Fact]
    public void FieldFamiliesHaveABoundedValidationBudget()
    {
        var text = AddUnit(ValidSave(), "unrelated : .ignored {\n" + string.Join('\n',
            Enumerable.Range(0, 4097).Select(index => $" field_{index}: 0")) + "\n}");
        Assert.Contains("Feldfamilienlimit", Assert.Throws<ScsSaveEditException>(() => Parse(text)).Message);
    }

    [Fact]
    public void MetadataDefinitionLimitRemainsUnchanged()
    {
        var text = "SiiNunit { accessory_engine_data : .engine { name: \"" + new string('x', 9 * 1024 * 1024) + "\" } }";
        Assert.Contains("Textlimit", Assert.Throws<InvalidDataException>(() => ScsDefinitionDocument.Parse(text)).Message);
    }

    [Fact]
    public async Task PhysicalSaveLimitIsCheckedBeforeCodecReads()
    {
        await using (var file = File.Create(_save.GameSiiPath)) file.SetLength(128L * 1024 * 1024 + 1);
        var codec = new CachedCodec();
        var error = await Assert.ThrowsAsync<ScsSaveEditException>(() => new ScsTruckPowertrainSnapshotReader(codec).ReadAsync(_save));
        Assert.Contains("128 MiB", error.Message);
        Assert.Empty(codec.ReadPaths);
    }

    [Fact]
    public void CancelledStructuralReadDoesNotReturnASnapshot()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ScsTruckPowertrainSnapshotReader.Parse(_save, Sha, ValidSave(), cancellation.Token));
    }

    [Theory]
    [InlineData("broken field")]
    [InlineData("value: 1\n value: 2")]
    [InlineData("value[0]: 1\n value[0]: 2")]
    [InlineData("value[1]: 1")]
    [InlineData("value: 3\n value[0]: 1\n value[1]: 2")]
    [InlineData("value[0]: 1\n value[]: 2")]
    [InlineData("value: (1, )")]
    [InlineData("value: \"\\xFF\"")]
    [InlineData("value: \"\\xC3\"")]
    [InlineData("value: \"\\q\"")]
    [InlineData("value: \"unterminated")]
    [InlineData("value: 1 other: 2")]
    [InlineData("value: @include")]
    [InlineData("value: /* comment */ 1")]
    [InlineData("value: { nested: 1 }")]
    public void IgnoredUnitFieldsMustStillBeCompletelyValidated(string body)
    {
        var text = AddUnit(ValidSave(), "unrelated : .private_unit_id {\n " + body + "\n}");
        var error = Assert.Throws<ScsSaveEditException>(() => Parse(text));
        Assert.DoesNotContain("private_unit_id", error.Message);
    }

    [Fact]
    public void MultipleAttributesOnWritableLineCannotLoseAdjacentState()
    {
        var original = ValidSave().Replace(" refund: 0\n", "", StringComparison.Ordinal)
            .Replace("engine/old.sii\"", "engine/old.sii\" refund: 123", StringComparison.Ordinal);
        Assert.Throws<ScsSaveEditException>(() => Parse(original));
    }

    [Fact]
    public void IgnoredOutOfOrderArraysAndValidEscapedUtf8RemainSupported()
    {
        var text = AddUnit(ValidSave(), "unrelated : .ignored {\n values: 3\n values[2]: \"\\xc3\\xa4\"\n values[0]: (1, 2)\n values[1]: null\n}");
        Assert.Equal(".truck", Parse(text).TruckId);
    }

    [Theory]
    [InlineData(10001)]
    [InlineData(2000001)]
    public void DeclaredReferenceCountsStayBounded(int count)
    {
        var text = ValidSave().Replace("trucks: 1", "trucks: " + count, StringComparison.Ordinal);
        Assert.Throws<ScsSaveEditException>(() => Parse(text));
    }

    [Fact]
    public void LargeOwnedReferenceListDetectsDuplicateReferencesAndMissingUnits()
    {
        var values = new StringBuilder(" trucks: 10000\n");
        for (var i = 0; i < 10000; i++) values.Append(" trucks[").Append(i).Append("]: ").Append(i == 9999 ? ".truck" : ".owned" + i).Append('\n');
        var text = ValidSave().Replace(" trucks: 1\n trucks[0]: .truck\n", values.ToString(), StringComparison.Ordinal);
        Assert.Throws<ScsSaveEditException>(() => Parse(text));
        Assert.Throws<ScsSaveEditException>(() => Parse(text.Replace(".owned9998", ".truck", StringComparison.Ordinal)));
    }

    [Fact]
    public void LargeIgnoredDataCannotHideDuplicateUnitIdsOrTruncation()
    {
        var text = AddUnit(ValidSave(), "unrelated : .large {\n payload: \"" + new string('x', 9 * 1024 * 1024) + "\"\n}");
        Assert.Throws<ScsSaveEditException>(() => Parse(AddUnit(text, "unrelated : .engine {\n value: 1\n}")));
        Assert.Throws<ScsSaveEditException>(() => Parse(text[..^4]));
    }
}
