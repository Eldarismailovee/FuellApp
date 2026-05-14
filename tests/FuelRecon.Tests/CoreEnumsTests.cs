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
            Assert.True(enumType.IsEnum, $"{enumType.Name} should be an enum.");

            foreach (var raw in Enum.GetValues(enumType))
            {
                var value = (Enum)raw;
                var name = Enum.GetName(enumType, value);

                Assert.False(string.IsNullOrWhiteSpace(name));
                Assert.Equal(name, value.ToString());

                var tryParseResult = Enum.TryParse(enumType, name, ignoreCase: false, out var parsed);
                Assert.True(tryParseResult);
                Assert.Equal(value, parsed);

                var parsedViaParse = Enum.Parse(enumType, name!, ignoreCase: false);
                Assert.Equal(value, parsedViaParse);
            }
        }
    }

    [Fact]
    public void ReconciliationStatus_matches_documented_item_statuses()
    {
        Assert.Equal(
            [
                nameof(ReconciliationStatus.Matched),
                nameof(ReconciliationStatus.Unbilled),
                nameof(ReconciliationStatus.Variance),
                nameof(ReconciliationStatus.DuplicatePossible),
                nameof(ReconciliationStatus.MissingRA),
                nameof(ReconciliationStatus.RegoMismatch),
                nameof(ReconciliationStatus.SupplierOnly),
                nameof(ReconciliationStatus.CarsOnly),
                nameof(ReconciliationStatus.ReviewRequired),
            ],
            Enum.GetNames<ReconciliationStatus>());
    }

    [Fact]
    public void ValidationSeverity_includes_info_warning_error()
    {
        Assert.Equal(
            [
                nameof(ValidationSeverity.Info),
                nameof(ValidationSeverity.Warning),
                nameof(ValidationSeverity.Error),
            ],
            Enum.GetNames<ValidationSeverity>());
    }

    [Fact]
    public void MappingResultState_matches_mapping_template_states()
    {
        Assert.Equal(
            [
                nameof(MappingResultState.AutoMapped),
                nameof(MappingResultState.TemplateMapped),
                nameof(MappingResultState.ManualMapped),
                nameof(MappingResultState.IncompleteMapping),
            ],
            Enum.GetNames<MappingResultState>());
    }

    [Fact]
    public void InputSlot_has_three_slots_for_period_imports()
    {
        Assert.Equal(3, Enum.GetValues<InputSlot>().Length);
    }
}