using FluentAssertions;
using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class CoreEnumsTests
{
    private static readonly Type[] CoreEnumTypes =
    [
        typeof(InputSlot),
        typeof(FileStatus),
        typeof(ValidationSeverity),
        typeof(MappingResultState),
        typeof(ReconciliationStatus),
        typeof(ResolutionStatus),
        typeof(ConfidenceBucket),
        typeof(PeriodLifecycleStatus),
        typeof(AuditActionType),
        typeof(AuditEntityType),
        typeof(PdfExportStatus),
    ];

    [Fact]
    public void Core_enum_members_roundtrip_parse_and_ToString()
    {
        foreach (var enumType in CoreEnumTypes)
        {
            enumType.IsEnum.Should().BeTrue("{0} should be an enum", enumType.Name);

            foreach (var raw in Enum.GetValues(enumType))
            {
                var value = (Enum)raw;
                var name = Enum.GetName(enumType, value);
                name.Should().NotBeNullOrEmpty();

                value.ToString().Should().Be(name);

                Enum.TryParse(enumType, name, ignoreCase: false, out var parsed).Should().BeTrue();
                parsed.Should().Be(value);

                Enum.Parse(enumType, name!, ignoreCase: false).Should().Be(value);
            }
        }
    }

    [Fact]
    public void ReconciliationStatus_matches_documented_item_statuses()
    {
        Enum.GetNames<ReconciliationStatus>().Should().Equal(
            nameof(ReconciliationStatus.Matched),
            nameof(ReconciliationStatus.Unbilled),
            nameof(ReconciliationStatus.Variance),
            nameof(ReconciliationStatus.DuplicatePossible),
            nameof(ReconciliationStatus.MissingRA),
            nameof(ReconciliationStatus.RegoMismatch),
            nameof(ReconciliationStatus.SupplierOnly),
            nameof(ReconciliationStatus.CarsOnly),
            nameof(ReconciliationStatus.ReviewRequired));
    }

    [Fact]
    public void ValidationSeverity_includes_info_warning_error()
    {
        Enum.GetNames<ValidationSeverity>().Should().Equal(
            nameof(ValidationSeverity.Info),
            nameof(ValidationSeverity.Warning),
            nameof(ValidationSeverity.Error));
    }

    [Fact]
    public void MappingResultState_matches_mapping_template_states()
    {
        Enum.GetNames<MappingResultState>().Should().Equal(
            nameof(MappingResultState.AutoMapped),
            nameof(MappingResultState.TemplateMapped),
            nameof(MappingResultState.ManualMapped),
            nameof(MappingResultState.IncompleteMapping));
    }

    [Fact]
    public void InputSlot_has_three_slots_for_period_imports()
    {
        Enum.GetValues<InputSlot>().Should().HaveCount(3);
    }
}
