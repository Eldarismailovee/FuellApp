namespace FuelRecon.Infrastructure.Excel;

/// <summary>
/// Canonical header alias lists shared by column mapping and header-row detection.
/// </summary>
internal static class ExcelParserKnownHeaders
{
    internal static class BranchLitres
    {
        internal static readonly string[] Branch =
        [
            "Branch",
            "Location",
            "Depot",
            "Branch Name",
            "Site",
            "Store",
            "Outlet",
        ];

        internal static readonly string[] Date =
        [
            "Date",
            "Fuel Date",
            "Transaction Date",
            "Dt",
            "Fuel Dt",
            "Day",
            "Fueling Date",
            "Posting Date",
            "Invoice Date",
            "When",
        ];

        internal static readonly string[] Litres =
        [
            "Litres",
            "L",
            "Qty",
            "Quantity",
            "Fuel Litres",
            "Volume",
            "Fuel Qty",
            "Fuel",
            "Fuel Used",
            "Liters",
            "Quantity Litres",
        ];

        internal static readonly string[] RentalAgreement =
        [
            "RA",
            "Rental Agreement",
            "Rental Agreement Number",
            "Agreement",
            "Agreement No",
            "Contract",
        ];

        internal static readonly string[] Rego =
        [
            "Rego",
            "Registration",
            "Plate",
            "Vehicle Rego",
            "Licence Plate",
            "License Plate",
        ];

        internal static readonly string[] Note =
        [
            "Note",
            "Notes",
            "Reference",
            "Description",
            "Memo",
            "Comments",
            "Vehicle",
            "Driver",
            "Card",
            "Odometer",
        ];
    }

    internal static class CarsBilling
    {
        internal static readonly string[] Branch =
        [
            "Branch",
            "Location",
            "Depot",
            "Branch Name",
            "Site",
            "Store",
            "Outlet",
        ];

        internal static readonly string[] Date =
        [
            "Date",
            "Billing Date",
            "Transaction Date",
            "Period",
            "Invoice Date",
            "Inv Date",
        ];

        internal static readonly string[] RentalAgreement =
        [
            "RA",
            "Rental Agreement",
            "Rental Agreement Number",
            "Agreement",
            "Agreement No",
            "Contract",
        ];

        internal static readonly string[] Rego =
        [
            "Rego",
            "Registration",
            "Plate",
            "Vehicle Rego",
            "Licence Plate",
            "License Plate",
        ];

        internal static readonly string[] BilledLitres =
        [
            "Billed Litres",
            "Litres",
            "Qty",
            "Quantity",
            "Fuel Litres",
            "Volume",
        ];

        internal static readonly string[] BilledAmount =
        [
            "Amount",
            "Billed Amount",
            "Charge",
            "Total",
            "Inc GST",
            "Incl GST",
        ];

        internal static readonly string[] Status =
        [
            "Status",
            "Billing Status",
            "Invoice Status",
            "Bill Status",
        ];

    }
    internal static readonly HashSet<string> All =
        [
            ..BranchLitres.Branch.Select(ExcelColumnHeaderMatcher.Normalise),
            ..BranchLitres.Date.Select(ExcelColumnHeaderMatcher.Normalise),
            ..BranchLitres.Litres.Select(ExcelColumnHeaderMatcher.Normalise),
            ..BranchLitres.RentalAgreement.Select(ExcelColumnHeaderMatcher.Normalise),
            ..BranchLitres.Rego.Select(ExcelColumnHeaderMatcher.Normalise),
            ..BranchLitres.Note.Select(ExcelColumnHeaderMatcher.Normalise),
            ..CarsBilling.Branch.Select(ExcelColumnHeaderMatcher.Normalise),
            ..CarsBilling.Date.Select(ExcelColumnHeaderMatcher.Normalise),
            ..CarsBilling.RentalAgreement.Select(ExcelColumnHeaderMatcher.Normalise),
            ..CarsBilling.Rego.Select(ExcelColumnHeaderMatcher.Normalise),
            ..CarsBilling.BilledLitres.Select(ExcelColumnHeaderMatcher.Normalise),
            ..CarsBilling.BilledAmount.Select(ExcelColumnHeaderMatcher.Normalise),
            ..CarsBilling.Status.Select(ExcelColumnHeaderMatcher.Normalise),
        ];
}
