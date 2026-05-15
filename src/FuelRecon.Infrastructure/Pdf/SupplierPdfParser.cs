using System.Text.RegularExpressions;
using FuelRecon.Application.Pdf;
using FuelRecon.Domain;

namespace FuelRecon.Infrastructure.Pdf;

public sealed partial class SupplierPdfParser(IPdfDocumentReader documentReader) : ISupplierPdfParser
{
    public const string UnsupportedPdfLayoutReasonCode = "UnsupportedPdfLayout";

    public const string SupplierRowNotParsedReasonCode = "SupplierRowNotParsed";

    public SupplierPdfParseResult Parse(string filePath, FuelPeriod period, BranchAliasResolver branchAliasResolver)
    {
        ArgumentNullException.ThrowIfNull(branchAliasResolver);

        var readResult = documentReader.ReadDocument(filePath);
        if (!readResult.Success || readResult.Document is null)
        {
            return SupplierPdfParseResult.From(
                [],
                [
                    new SupplierPdfParseIssue(
                        ValidationSeverity.Error,
                        readResult.ReasonCode ?? "PdfReadFailed",
                        readResult.Message)
                ],
                pageCount: 0,
                candidateRowCount: 0,
                success: false);
        }

        var supplierName = DetectSupplierName(readResult.Document);
        if (supplierName is null)
        {
            var issues = readResult.Document.Pages
                .Select(page => new SupplierPdfParseIssue(
                    ValidationSeverity.Error,
                    UnsupportedPdfLayoutReasonCode,
                    "PDF layout is not recognised as an agreed MVP supplier statement.",
                    new SourceReference(page.SourceFile, pageNumber: page.PageNumber)))
                .ToArray();

            return SupplierPdfParseResult.From(
                [],
                issues,
                readResult.Document.Pages.Count,
                candidateRowCount: readResult.Document.Pages.Count,
                success: false);
        }

        var entries = new List<SupplierTransaction>();
        var issuesList = new List<SupplierPdfParseIssue>();
        var candidateRowCount = 0;

        foreach (var page in readResult.Document.Pages)
        {
            foreach (var line in page.Lines.Where(IsCandidateLine))
            {
                candidateRowCount++;
                ParseCandidateLine(line, page, period, supplierName, branchAliasResolver, entries, issuesList);
            }
        }

        if (candidateRowCount == 0)
        {
            issuesList.AddRange(readResult.Document.Pages.Select(page => new SupplierPdfParseIssue(
                ValidationSeverity.Warning,
                SupplierRowNotParsedReasonCode,
                "No transaction-like rows were found on the recognised supplier statement page.",
                new SourceReference(page.SourceFile, pageNumber: page.PageNumber))));
        }

        return SupplierPdfParseResult.From(
            entries,
            issuesList,
            readResult.Document.Pages.Count,
            candidateRowCount,
            success: entries.Count > 0 || issuesList.Count > 0);
    }

    private static void ParseCandidateLine(
        string line,
        PdfPageModel page,
        FuelPeriod period,
        string supplierName,
        BranchAliasResolver branchAliasResolver,
        ICollection<SupplierTransaction> entries,
        ICollection<SupplierPdfParseIssue> issues)
    {
        var sourceReference = new SourceReference(page.SourceFile, pageNumber: page.PageNumber, referenceText: line);
        var dateMatch = DatePattern().Match(line);
        var litresMatch = LitresPattern().Match(line);

        if (!dateMatch.Success || !litresMatch.Success)
        {
            issues.Add(new SupplierPdfParseIssue(
                ValidationSeverity.Warning,
                SupplierRowNotParsedReasonCode,
                "Candidate supplier row could not be parsed into required date and litres fields.",
                sourceReference));
            return;
        }

        var dateResult = DateNormaliser.NormaliseText(dateMatch.Value);
        if (!dateResult.Success || dateResult.NormalisedValue is null)
        {
            issues.Add(new SupplierPdfParseIssue(
                ValidationSeverity.Error,
                dateResult.ReasonCode ?? DateNormaliser.InvalidDateFormatReasonCode,
                $"Supplier transaction date '{dateMatch.Value}' could not be normalised.",
                sourceReference));
            return;
        }

        var litresResult = LitresNormaliser.Normalise(litresMatch.Groups["value"].Value);
        if (!litresResult.Success || litresResult.NormalisedValue is null)
        {
            issues.Add(new SupplierPdfParseIssue(
                ValidationSeverity.Error,
                litresResult.ReasonCode ?? LitresNormaliser.FailureReasonCode,
                $"Supplier transaction litres value '{litresMatch.Groups["value"].Value}' could not be normalised.",
                sourceReference));
            return;
        }

        var amount = TryParseAmount(line, sourceReference, issues);
        var siteText = ExtractSiteText(line, dateMatch.Value, litresMatch.Value);
        CanonicalBranchId? branchId = null;
        if (!string.IsNullOrWhiteSpace(siteText))
        {
            var branchResult = branchAliasResolver.Resolve(siteText);
            if (branchResult.Success && branchResult.BranchId is not null)
            {
                branchId = branchResult.BranchId.Value;
            }
            else
            {
                issues.Add(new SupplierPdfParseIssue(
                    ValidationSeverity.Warning,
                    branchResult.ReasonCode ?? BranchAliasResolver.BranchAliasNotFoundReasonCode,
                    $"Supplier row branch/site text '{siteText}' could not be resolved.",
                    sourceReference));
            }
        }

        entries.Add(new SupplierTransaction(
            Guid.NewGuid(),
            supplierName,
            period,
            dateResult.NormalisedValue.Value,
            litresResult.NormalisedValue.Value,
            sourceReference,
            branchId,
            rawSiteText: siteText,
            voucherOrInvoiceReference: ExtractReference(line),
            amount: amount));
    }

    private static MoneyAmount? TryParseAmount(
        string line,
        SourceReference sourceReference,
        ICollection<SupplierPdfParseIssue> issues)
    {
        var amountMatch = AmountPattern().Match(line);
        if (!amountMatch.Success)
        {
            return null;
        }

        var result = MoneyAmountNormaliser.Normalise(amountMatch.Value);
        if (result.Success && result.NormalisedValue is not null)
        {
            return result.NormalisedValue.Value;
        }

        issues.Add(new SupplierPdfParseIssue(
            ValidationSeverity.Warning,
            result.ReasonCode ?? MoneyAmountNormaliser.FailureReasonCode,
            $"Supplier transaction amount value '{amountMatch.Value}' could not be normalised.",
            sourceReference));
        return null;
    }

    private static string? DetectSupplierName(PdfDocumentModel document)
    {
        var combinedText = string.Join('\n', document.Pages.Select(page => page.Text));
        if (combinedText.Contains("Farmlands", StringComparison.OrdinalIgnoreCase)
            || combinedText.Contains("Caltex", StringComparison.OrdinalIgnoreCase))
        {
            return "Farmlands";
        }

        if (combinedText.Contains("Mobil", StringComparison.OrdinalIgnoreCase)
            || combinedText.Contains("Mobile", StringComparison.OrdinalIgnoreCase))
        {
            return "Mobil";
        }

        return null;
    }

    private static bool IsCandidateLine(string line) =>
        DatePattern().IsMatch(line)
        || LitresPattern().IsMatch(line)
        || line.Contains("litre", StringComparison.OrdinalIgnoreCase)
        || line.Contains("diesel", StringComparison.OrdinalIgnoreCase)
        || line.Contains("petrol", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractSiteText(string line, string dateText, string litresText)
    {
        var withoutDate = line.Replace(dateText, " ", StringComparison.OrdinalIgnoreCase);
        var litresIndex = withoutDate.IndexOf(litresText, StringComparison.OrdinalIgnoreCase);
        var candidate = litresIndex >= 0 ? withoutDate[..litresIndex] : withoutDate;
        candidate = ReferencePattern().Replace(candidate, " ");
        candidate = AmountPattern().Replace(candidate, " ");
        candidate = ProductWordsPattern().Replace(candidate, " ");
        candidate = WhitespacePattern().Replace(candidate, " ").Trim();
        return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
    }

    private static string? ExtractReference(string line)
    {
        var match = ReferencePattern().Match(line);
        return match.Success ? match.Value.Trim() : null;
    }

    [GeneratedRegex(@"\b\d{1,2}[/-]\d{1,2}[/-]\d{4}\b|\b\d{1,2}\s+[A-Za-z]{3,9}\s+\d{4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"(?<value>-?\d+(?:[.,]\d+)?)\s*(?:L|LT|LTR|LITRE|LITRES)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LitresPattern();

    [GeneratedRegex(@"\$?\s*-?\d+(?:[.,]\d{2})\b")]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"\b(?:INV|INVOICE|VOUCHER|REF)[:#\s-]*[A-Za-z0-9-]+\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReferencePattern();

    [GeneratedRegex(@"\b(?:DIESEL|PETROL|FUEL|UNLEADED|LITRES?|LTR|LT)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ProductWordsPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
