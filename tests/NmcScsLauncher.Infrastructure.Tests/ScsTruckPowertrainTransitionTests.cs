using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed partial class ScsTruckPowertrainRulesTests
{
    [Theory]
    [InlineData("type: diesel", "1.49.0.0")]
    [InlineData("auxiliary_brakes[]: (0.3, 0, 0) auxiliary_brakes[]: (0.6, 0, 0) auxiliary_brakes[]: (1, 0, 0)", "1.39.0.0")]
    [InlineData("cold_start_rpm: 549", "1.61.0.0")]
    [InlineData("rpm_idle: 700 cold_start_rpm: 600", "1.61.0.0")]
    [InlineData("rpm_limit_hysteresis: -1", "1.61.0.0")]
    [InlineData("rpm_limit_neutral_hysteresis: -1", "1.61.0.0")]
    public void DocumentedEngineVersionAndPhysicalConstraintsAreRequired(string attributes, string version)
    {
        using var source = Source();
        source.Evidence = source.Evidence with { GameVersion = version };
        source.Set(NewEngine, "accessory_engine_data", "new.test.engine", "torque: 2400 " + attributes);
        Assert.False(Assert.Single(Build(source).Engines, option => option.DataPath == NewEngine).DefinitionVerified);
    }

    [Theory]
    [InlineData("ratios_forward[]: 1 ratios_forward[]: 2 ratios_reverse[]: -3")]
    [InlineData("ratios_forward[]: 2 ratios_reverse[]: -1 ratios_reverse[]: -3")]
    public void GearRatiosMustDecreaseInReduction(string attributes)
    {
        using var source = Source();
        source.Set(NewGearbox, "accessory_transmission_data", "new.test.transmission", attributes);
        Assert.False(Assert.Single(Build(source).Transmissions, option => option.DataPath == NewGearbox).DefinitionVerified);
    }

    [Fact]
    public void NegativeColdStartMeansUnsetAndTransmissionAuxiliaryIsSupportedBefore140()
    {
        using var source = Source();
        source.Evidence = source.Evidence with { GameVersion = "1.61.0.0" };
        source.Set(NewEngine, "accessory_engine_data", "new.test.engine", "torque: 2400 cold_start_rpm: -1");
        Assert.True(Assert.Single(Build(source).Engines, option => option.DataPath == NewEngine).DefinitionVerified);
        source.Evidence = source.Evidence with { GameVersion = "1.39.0.0" };
        source.Set(NewGearbox, "accessory_transmission_data", "new.test.transmission", "ratios_forward[]: 2 ratios_reverse[]: -3 retarder: 1 auxiliary_brakes[]: (1, 0, 0)");
        Assert.True(Assert.Single(Build(source).Transmissions, option => option.DataPath == NewGearbox).DefinitionVerified);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    public void IncludeDirectivesMustStartAtColumnZero(string indentation)
    {
        using var source = Source();
        source.Files[NewEngine] = $"SiiNunit\n{{\naccessory_engine_data : new.test.engine {{\n{indentation}@include \"/def/common.sui\"\n}}\n}}";
        source.Files["/def/common.sui"] = "torque: 2400";
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Theory]
    [InlineData("UPPER.test.engine")]
    [InlineData("thirteenchars.test.engine")]
    public void AccessoryIdentifiersMustUseDocumentedGameTokens(string identifier)
    {
        using var source = Source();
        source.Set(NewEngine, "accessory_engine_data", identifier, "torque: 2400");
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void OwnerIdentifiersMustUseDocumentedGameTokens()
    {
        using var source = Source();
        source.Files[NewGearbox] = "SiiNunit { accessory_transmission_data : new.test.transmission { ratios_forward[]: 2 ratios_forward[]: 1 ratios_reverse[]: -3 transmission_names: .BAD } transmission_names : .BAD { forward[]: \"1\" forward[]: \"2\" reverse[]: \"R\" } }";
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Theory]
    [InlineData("engine_brake_positions: 4")]
    [InlineData("engine_brake_downshift: false")]
    [InlineData("engine_brake: 0")]
    [InlineData("engine_brake_positions: 3 auxiliary_brakes[]: (0.1, 0, 0) auxiliary_brakes[]: (0.5, 0, 0) auxiliary_brakes[]: (1, 0, 0)")]
    public void ChangedEngineControlDomainCannotReuseOpaqueSavedState(string attributes)
    {
        using var source = Source();
        source.Set(NewEngine, "accessory_engine_data", "new.test.engine", "torque: 2400 " + attributes);
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Theory]
    [InlineData("retarder: 3")]
    [InlineData("crawls: 1")]
    [InlineData("transmission_class: standard")]
    [InlineData("transmission_class: unknown_class")]
    [InlineData("stall_torque_ratio: 1.5")]
    [InlineData("torque_converter_lock_gear: 1")]
    [InlineData("torque_converter_crawl: true")]
    public void ChangedTransmissionControlDomainCannotReuseOpaqueSavedState(string attributes)
    {
        using var source = Source();
        source.Set(NewGearbox, "accessory_transmission_data", "new.test.transmission",
            "ratios_forward[]: 2 ratios_forward[]: 1 ratios_reverse[]: -3 " + attributes);
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void ForwardAndReverseCountsMustRemainUnchanged()
    {
        using var source = Source();
        source.Set(NewGearbox, "accessory_transmission_data", "new.test.transmission", "ratios_forward[]: 3 ratios_reverse[]: -4 ratios_reverse[]: -2");
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
        source.Set(NewGearbox, "accessory_transmission_data", "new.test.transmission", "ratios_forward[]: 4 ratios_forward[]: 2 ratios_forward[]: 1 ratios_reverse[]: -3");
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void PhysicalRatiosAndTorqueMayChangeWithTheSameDocumentedControlDomain()
    {
        using var source = Source();
        source.Set(Engine, "accessory_engine_data", "old.test.engine", "torque: 2000 engine_brake_positions: 3 engine_brake_downshift: true");
        source.Set(NewEngine, "accessory_engine_data", "new.test.engine", "torque: 2600 engine_brake: 1.5");
        source.Set(Gearbox, "accessory_transmission_data", "old.test.transmission", "ratios_forward[]: 3 ratios_reverse[]: -4 retarder: 0 transmission_class: heavy torque_converter_lock_gear: 2");
        source.Set(NewGearbox, "accessory_transmission_data", "new.test.transmission", "ratios_forward[]: 2 ratios_reverse[]: -3 differential_ratio: 3.75 shift_time: 0.8");
        Assert.True(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void AuxiliaryBrakeMappingMustRemainIdenticalEvenAtTheSameStageCount()
    {
        using var source = Source();
        const string baseValues = "ratios_forward[]: 2 ratios_reverse[]: -3 retarder: 1 ";
        source.Set(Gearbox, "accessory_transmission_data", "old.test.transmission", baseValues + "auxiliary_brakes[]: (1, 0, 0)");
        source.Set(NewGearbox, "accessory_transmission_data", "new.test.transmission", baseValues + "auxiliary_brakes[]: (1, 1, 0)");
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
        source.Set(NewGearbox, "accessory_transmission_data", "new.test.transmission", baseValues + "auxiliary_brakes[]: (1, 0, 0)");
        Assert.True(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }
}
