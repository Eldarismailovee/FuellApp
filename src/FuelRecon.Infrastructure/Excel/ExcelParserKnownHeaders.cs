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
}
