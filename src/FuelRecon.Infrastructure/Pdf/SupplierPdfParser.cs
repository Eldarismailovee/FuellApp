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

        var seenFarmlandsKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var page in readResult.Document.Pages)
        {
            foreach (var candidate in ExtractCandidateRows(page, supplierName))
            {
                candidateRowCount++;
                ParseCandidateLine(
                    candidate,
                    page,
                    period,
                    supplierName,
                    branchAliasResolver,
                    entries,
                    issuesList,
                    seenFarmlandsKeys);
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
        SupplierPdfCandidate candidate,
        PdfPageModel page,
        FuelPeriod period,
        string supplierName,
        BranchAliasResolver branchAliasResolver,
        ICollection<SupplierTransaction> entries,
        ICollection<SupplierPdfParseIssue> issues,
        ISet<string> seenFarmlandsKeys)
    {
        var line = candidate.Text;
        var sourceReference = new SourceReference(page.SourceFile, pageNumber: page.PageNumber, referenceText: line);
        var dateMatch = SupplierDatePattern().Match(line);
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

        var date = NormaliseSupplierDate(dateMatch.Value);
        if (date is null)
        {
            issues.Add(new SupplierPdfParseIssue(
                ValidationSeverity.Error,
                DateNormaliser.InvalidDateFormatReasonCode,
                $"Supplier transaction date '{dateMatch.Value}' could not be normalised.",
                sourceReference));
            return;
        }

        var litresText = litresMatch.Groups["value"].Value;
        if (litresText.TrimStart().StartsWith('-'))
        {
            issues.Add(new SupplierPdfParseIssue(
                ValidationSeverity.Warning,
                SupplierRowNotParsedReasonCode,
                "Credit/negative litre supplier row was detected and skipped for MVP supplier transactions.",
                sourceReference));
            return;
        }

        var litresResult = LitresNormaliser.Normalise(litresText);
        if (!litresResult.Success || litresResult.NormalisedValue is null)
        {
            issues.Add(new SupplierPdfParseIssue(
                ValidationSeverity.Error,
                litresResult.ReasonCode ?? LitresNormaliser.FailureReasonCode,
                $"Supplier transaction litres value '{litresText}' could not be normalised.",
                sourceReference));
            return;
        }

        var reference = ExtractReference(line);
        var siteText = candidate.SiteText ?? ExtractSiteText(line, dateMatch.Value, litresMatch.Value);
        var product = ExtractProduct(line);
        var amount = TryParseAmount(line, sourceReference, issues);

        if (supplierName.Equals("Farmlands", StringComparison.OrdinalIgnoreCase))
        {
            var dedupeKey = string.Join('|', reference, date.Value.ToString("yyyy-MM-dd"), siteText, litresResult.NormalisedValue.Value.Value);
            if (!seenFarmlandsKeys.Add(dedupeKey))
            {
                return;
            }
        }

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
            date.Value,
            litresResult.NormalisedValue.Value,
            sourceReference,
            branchId,
            cardholder: candidate.CardholderText,
            rawSiteText: siteText,
            voucherOrInvoiceReference: reference,
            product: product,
            amount: amount));
    }

    private static MoneyAmount? TryParseAmount(
        string line,
        SourceReference sourceReference,
        ICollection<SupplierPdfParseIssue> issues)
    {
        var amountMatch = CurrencyAmountPattern().Match(line);
        if (!amountMatch.Success)
        {
            amountMatch = AmountPattern().Match(line);
        }

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
        SupplierDatePattern().IsMatch(line)
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
        candidate = CardNumberPattern().Replace(candidate, " ");
        candidate = ProductWordsPattern().Replace(candidate, " ");
        candidate = WhitespacePattern().Replace(candidate, " ").Trim();
        return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
    }

    private static IReadOnlyList<SupplierPdfCandidate> ExtractCandidateRows(PdfPageModel page, string supplierName)
    {
        var pageText = NormaliseText(page.Text);
        var candidates = supplierName.Equals("Mobil", StringComparison.OrdinalIgnoreCase)
            ? ExtractMobilCandidates(pageText)
            : ExtractFarmlandsCandidates(pageText);

        if (candidates.Count > 0)
        {
            return candidates;
        }

        return page.Lines
            .Where(IsCandidateLine)
            .Select(line => new SupplierPdfCandidate(line))
            .ToArray();
    }

    private static IReadOnlyList<SupplierPdfCandidate> ExtractFarmlandsCandidates(string pageText)
    {
        var matches = SupplierDatePattern().Matches(pageText);
        var candidates = new List<SupplierPdfCandidate>();

        for (var index = 0; index < matches.Count; index++)
        {
            var start = matches[index].Index;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : pageText.Length;
            var segment = pageText[start..end].Trim();

            if (IsCandidateLine(segment))
            {
                candidates.Add(new SupplierPdfCandidate(segment));
            }
        }

        return candidates;
    }

    private static IReadOnlyList<SupplierPdfCandidate> ExtractMobilCandidates(string pageText)
    {
        var candidates = new List<SupplierPdfCandidate>();
        var cardMatches = MobilCardholderPattern().Matches(pageText);

        if (cardMatches.Count == 0)
        {
            return ExtractFarmlandsCandidates(pageText);
        }

        for (var cardIndex = 0; cardIndex < cardMatches.Count; cardIndex++)
        {
            var cardMatch = cardMatches[cardIndex];
            var sectionStart = cardMatch.Index + cardMatch.Length;
            var sectionEnd = cardIndex + 1 < cardMatches.Count ? cardMatches[cardIndex + 1].Index : pageText.Length;
            var section = pageText[sectionStart..sectionEnd];
            var cardholder = cardMatch.Groups["name"].Value.Trim();
            var siteText = MobilCardholderSuffixPattern().Replace(cardholder, string.Empty).Trim();
            var dateMatches = SupplierDatePattern().Matches(section);

            for (var index = 0; index < dateMatches.Count; index++)
            {
                var start = dateMatches[index].Index;
                var end = index + 1 < dateMatches.Count ? dateMatches[index + 1].Index : section.Length;
                var segment = section[start..end].Trim();

                if (IsCandidateLine(segment))
                {
                    candidates.Add(new SupplierPdfCandidate(segment, SiteText: siteText, CardholderText: cardholder));
                }
            }
        }

        return candidates;
    }

    private static DateOnly? NormaliseSupplierDate(string rawDate)
    {
        var dateText = TwoDigitYearDatePattern().IsMatch(rawDate)
            ? TwoDigitYearDatePattern().Replace(rawDate, match => $"{match.Groups["day"].Value} {match.Groups["month"].Value} 20{match.Groups["year"].Value}")
            : rawDate;

        var result = DateNormaliser.NormaliseText(dateText);
        return result.Success ? result.NormalisedValue : null;
    }

    private static string? ExtractProduct(string line)
    {
        var match = ProductNamePattern().Match(line);
        return match.Success ? match.Value.Trim() : null;
    }

    private static string NormaliseText(string text) =>
        WhitespacePattern().Replace(text.Replace('\u00a0', ' '), " ").Trim();

    private static string? ExtractReference(string line)
    {
        var match = ReferencePattern().Match(line);
        return match.Success ? match.Value.Trim() : null;
    }

    [GeneratedRegex(@"\b\d{1,2}[/-]\d{1,2}[/-]\d{4}\b|\b\d{1,2}\s+[A-Za-z]{3,9}\s+\d{2,4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex SupplierDatePattern();

    [GeneratedRegex(@"\b(?<day>\d{1,2})\s+(?<month>[A-Za-z]{3,9})\s+(?<year>\d{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex TwoDigitYearDatePattern();

    [GeneratedRegex(@"(?<value>-?\d+(?:[.,]\d+)?)\s*(?:L|LT|LTR|LITRE|LITRES)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LitresPattern();

    [GeneratedRegex(@"\$?\s*-?\d+(?:[.,]\d{2})\b")]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"\$\s*-?\d+(?:[.,]\d{2})\b")]
    private static partial Regex CurrencyAmountPattern();

    [GeneratedRegex(@"\b(?:INV|INVOICE|VOUCHER|REF|CRD)[:#\s-]*[A-Za-z0-9-]+\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReferencePattern();

    [GeneratedRegex(@"\b(?:DIESEL|PETROL|FUEL|UNLEADED|LITRES?|LTR|LT)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ProductWordsPattern();

    [GeneratedRegex(@"\b(?:DIESEL|91\s+UNLEADED|95\s+UNLEADED|UNLEADED|PETROL)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ProductNamePattern();

    [GeneratedRegex(@"CARD\s+NUMBER:\s*\S+\s+NAME:\s*(?<name>.*?)(?=\s+\d{1,2}[/-]\d{1,2}[/-]\d{4}\b|\s+\d{1,2}\s+[A-Za-z]{3,9}\s+\d{2,4}\b|$)", RegexOptions.IgnoreCase)]
    private static partial Regex MobilCardholderPattern();

    [GeneratedRegex(@"\s+\d+/\d+$")]
    private static partial Regex MobilCardholderSuffixPattern();

    [GeneratedRegex(@"\b\d{5,}\b")]
    private static partial Regex CardNumberPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}

internal sealed record SupplierPdfCandidate(string Text, string? SiteText = null, string? CardholderText = null);
