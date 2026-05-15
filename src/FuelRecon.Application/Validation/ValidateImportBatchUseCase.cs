using FuelRecon.Application.Excel;
using FuelRecon.Application.Pdf;
using FuelRecon.Domain;

namespace FuelRecon.Application.Validation;

public sealed class ValidateImportBatchUseCase(
    ISupplierPdfParser supplierPdfParser,
    IBranchLitresExcelParser branchLitresExcelParser,
    ICarsBillingExcelParser carsBillingExcelParser)
{
    public const string MissingMandatoryFileReasonCode = "MissingMandatoryFile";

    public ImportValidationResult Execute(ValidateImportBatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.BranchAliasResolver);

        var fileResults = new List<ImportFileValidationResult>();
        var supplierTransactions = new List<SupplierTransaction>();
        var branchLitresEntries = new List<BranchLitresEntry>();
        var carsBillingEntries = new List<CarsBillingEntry>();

        if (IsMissing(request.SupplierPdfPath))
        {
            fileResults.Add(MissingFile(InputSlot.SupplierStatement, request.SupplierPdfPath));
        }
        else
        {
            var supplierResult = supplierPdfParser.Parse(request.SupplierPdfPath!, request.Period, request.BranchAliasResolver);
            supplierTransactions.AddRange(supplierResult.Entries);
            fileResults.Add(MapSupplierResult(request.SupplierPdfPath, supplierResult));
        }

        if (IsMissing(request.BranchLitresExcelPath))
        {
            fileResults.Add(MissingFile(InputSlot.BranchLitres, request.BranchLitresExcelPath));
        }
        else
        {
            var branchResult = branchLitresExcelParser.Parse(request.BranchLitresExcelPath!, request.Period, request.BranchAliasResolver);
            branchLitresEntries.AddRange(branchResult.Entries);
            fileResults.Add(MapBranchLitresResult(request.BranchLitresExcelPath, branchResult));
        }

        if (IsMissing(request.CarsBillingExcelPath))
        {
            fileResults.Add(MissingFile(InputSlot.CarsBilling, request.CarsBillingExcelPath));
        }
        else
        {
            var carsResult = carsBillingExcelParser.Parse(request.CarsBillingExcelPath!, request.Period, request.BranchAliasResolver);
            carsBillingEntries.AddRange(carsResult.Entries);
            fileResults.Add(MapCarsBillingResult(request.CarsBillingExcelPath, carsResult));
        }

        var success = fileResults.All(file => file.Status == FileStatus.Valid || file.Status == FileStatus.Parsed);

        return new ImportValidationResult(
            success,
            fileResults,
            supplierTransactions,
            branchLitresEntries,
            carsBillingEntries);
    }

    private static bool IsMissing(string? filePath) => string.IsNullOrWhiteSpace(filePath);

    private static ImportFileValidationResult MissingFile(InputSlot inputSlot, string? filePath) =>
        new(
            inputSlot,
            filePath,
            FileStatus.Invalid,
            RowCount: 0,
            ValidRowCount: 0,
            SkippedRowCount: 0,
            [
                new ImportValidationIssue(
                    ValidationSeverity.Error,
                    MissingMandatoryFileReasonCode,
                    $"{inputSlot} file is mandatory.")
            ]);

    private static ImportFileValidationResult MapSupplierResult(string? filePath, SupplierPdfParseResult result)
    {
        var issues = result.Issues
            .Select(issue => new ImportValidationIssue(issue.Severity, issue.ReasonCode, issue.Message, issue.SourceReference))
            .ToArray();

        return new ImportFileValidationResult(
            InputSlot.SupplierStatement,
            filePath,
            DetermineStatus(result.Success, issues),
            result.CandidateRowCount,
            result.ValidRowCount,
            result.SkippedRowCount,
            issues);
    }

    private static ImportFileValidationResult MapBranchLitresResult(string? filePath, BranchLitresParseResult result)
    {
        var issues = result.Issues
            .Select(issue => new ImportValidationIssue(issue.Severity, issue.ReasonCode, issue.Message, issue.SourceReference))
            .ToArray();

        return new ImportFileValidationResult(
            InputSlot.BranchLitres,
            filePath,
            DetermineStatus(result.Success, issues),
            result.RowCount,
            result.ValidRowCount,
            result.SkippedRowCount,
            issues);
    }

    private static ImportFileValidationResult MapCarsBillingResult(string? filePath, CarsBillingParseResult result)
    {
        var issues = result.Issues
            .Select(issue => new ImportValidationIssue(issue.Severity, issue.ReasonCode, issue.Message, issue.SourceReference))
            .ToArray();

        return new ImportFileValidationResult(
            InputSlot.CarsBilling,
            filePath,
            DetermineStatus(result.Success, issues),
            result.RowCount,
            result.ValidRowCount,
            result.SkippedRowCount,
            issues);
    }

    private static FileStatus DetermineStatus(bool parserSuccess, IReadOnlyList<ImportValidationIssue> issues)
    {
        if (!parserSuccess || issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return FileStatus.Invalid;
        }

        return issues.Any(issue => issue.Severity == ValidationSeverity.Warning)
            ? FileStatus.Parsed
            : FileStatus.Valid;
    }
}
