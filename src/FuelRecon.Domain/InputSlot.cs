namespace FuelRecon.Domain;

/// <summary>
/// Mandatory import slot for a fuel period (supplier statement, branch workbook, Cars+ export).
/// </summary>
public enum InputSlot
{
    SupplierStatement = 0,
    BranchLitres = 1,
    CarsBilling = 2,
}
