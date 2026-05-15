using System.Diagnostics.CodeAnalysis;
using System.Text;
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

        if (supplierName.Equals("Mobil", StringComparison.OrdinalIgnoreCase))
        {
            var mobilLayout = MobilDocumentLayout.Build(readResult.Document.Pages);
            foreach (var (candidate, page) in ExtractMobilCandidatesFromDocument(mobilLayout))
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
        else
        {
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
        }

        if (candidateRowCount == 0)
        {
            issuesList.AddRange(readResult.Document.Pages.Select(page => new SupplierPdfParseIssue(
                ValidationSeverity.Warning,
                SupplierRowNotParsedReasonCode,
                "No transaction-like rows were found on the recognised supplier statement.",
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
        string? mobilConcatenatedLitresText = null;
        Match litresMatch;

        if (supplierName.Equals("Mobil", StringComparison.OrdinalIgnoreCase)
            && TryParseMobilConcatenatedLitresBeforeProduct(line, out var concatenatedLitresMobil))
        {
            mobilConcatenatedLitresText = concatenatedLitresMobil;
            litresMatch = LitresPattern().Match(string.Empty);
        }
        else
        {
            litresMatch = LitresPattern().Match(line);
            if (!litresMatch.Success && supplierName.Equals("Mobil", StringComparison.OrdinalIgnoreCase))
            {
                litresMatch = MobilInlineQuantityPattern().Match(line);
            }
        }

        if (!dateMatch.Success || (!litresMatch.Success && mobilConcatenatedLitresText is null))
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

        var litresText = mobilConcatenatedLitresText ?? litresMatch.Groups["value"].Value;
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

        var reference = ExtractReference(line)
            ?? (supplierName.Equals("Mobil", StringComparison.OrdinalIgnoreCase)
                ? ExtractMobilConcatenatedVoucher(line)
                : null);
        var siteText = candidate.SiteText ?? ExtractSiteText(line, dateMatch.Value, litresText, supplierName);
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

    private static string? ExtractSiteText(string line, string dateText, string litresText, string supplierName)
    {
        var withoutDate = line.Replace(dateText, " ", StringComparison.OrdinalIgnoreCase);

        if (supplierName.Equals("Mobil", StringComparison.OrdinalIgnoreCase))
        {
            Match? lastMobilLitres = null;
            foreach (Match match in MobilLitresDigitDotDigitLBeforeLetterPattern().Matches(withoutDate))
            {
                lastMobilLitres = match;
            }

            if (lastMobilLitres is not null && lastMobilLitres.Success)
            {
                return TrimSupplierSiteNoise(withoutDate[..lastMobilLitres.Index]);
            }
        }

        var litresIndex = withoutDate.IndexOf(litresText, StringComparison.OrdinalIgnoreCase);
        var candidate = litresIndex >= 0 ? withoutDate[..litresIndex] : withoutDate;
        return TrimSupplierSiteNoise(candidate);
    }

    private static string? TrimSupplierSiteNoise(string candidate)
    {
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
        var candidates = ExtractFarmlandsCandidates(pageText);

        if (candidates.Count > 0)
        {
            return candidates;
        }

        return page.Lines
            .Where(IsCandidateLine)
            .Select(line => new SupplierPdfCandidate(line))
            .ToArray();
    }

    private static IReadOnlyList<SupplierPdfCandidate> ExtractFarmlandsCandidates(string pageText) =>
        ExtractSupplierDateAnchoredSegments(pageText).Select(segment => new SupplierPdfCandidate(segment)).ToArray();

    /// <summary>
    /// Tokenises concatenated supplier layouts by anchoring each chunk at supplier-supported transaction dates.
    /// </summary>
    private static IReadOnlyList<string> ExtractSupplierDateAnchoredSegments(string pageText)
    {
        var matches = SupplierDatePattern().Matches(pageText);
        var segments = new List<string>();

        for (var index = 0; index < matches.Count; index++)
        {
            var start = matches[index].Index;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : pageText.Length;
            var segment = pageText[start..end].Trim();

            if (IsCandidateLine(segment))
            {
                segments.Add(segment);
            }
        }

        return segments;
    }

    private sealed class MobilDocumentLayout
    {
        private MobilDocumentLayout(
            string combinedText,
            IReadOnlyList<(int Start, PdfPageModel Page)> spans,
            IReadOnlyList<PdfPageModel> pages)
        {
            CombinedText = combinedText;
            _spans = spans;
            Pages = pages;
        }

        public string CombinedText { get; }

        public IReadOnlyList<PdfPageModel> Pages { get; }

        private readonly IReadOnlyList<(int Start, PdfPageModel Page)> _spans;

        public PdfPageModel PageForIndex(int indexInCombined)
        {
            for (var i = _spans.Count - 1; i >= 0; i--)
            {
                if (indexInCombined >= _spans[i].Start)
                {
                    return _spans[i].Page;
                }
            }

            return _spans[0].Page;
        }

        public static MobilDocumentLayout Build(IReadOnlyList<PdfPageModel> pages)
        {
            var spans = new List<(int Start, PdfPageModel Page)>();
            var builder = new StringBuilder();

            for (var i = 0; i < pages.Count; i++)
            {
                spans.Add((builder.Length, pages[i]));
                if (i > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(
                    SanitizePdfControlCharacters(NormaliseMobilPageText(NormaliseText(pages[i].Text))));
            }

            return new MobilDocumentLayout(builder.ToString(), spans, pages);
        }
    }

    /// <summary>
    /// Mobil statements span multiple pages with "Continued from previous page" blocks that omit a repeated CARD header.
    /// Concatenate pages before splitting so continuation rows stay inside the correct cardholder section.
    /// </summary>
    private static IEnumerable<(SupplierPdfCandidate Candidate, PdfPageModel Page)> ExtractMobilCandidatesFromDocument(
        MobilDocumentLayout layout)
    {
        var combinedText = layout.CombinedText;
        var candidates = ExtractMobilCandidateEntries(combinedText);

        if (candidates.Count > 0)
        {
            foreach (var (candidate, startIndex) in candidates)
            {
                yield return (candidate, layout.PageForIndex(startIndex));
            }

            yield break;
        }

        foreach (var page in layout.Pages)
        {
            foreach (var line in page.Lines.Where(IsCandidateLine))
            {
                yield return (new SupplierPdfCandidate(line), page);
            }
        }
    }

    private static List<(SupplierPdfCandidate Candidate, int StartIndex)> ExtractMobilCandidateEntries(string combinedText)
    {
        var results = new List<(SupplierPdfCandidate Candidate, int StartIndex)>();
        var cardMatches = MobilCardholderPattern().Matches(combinedText).Cast<Match>().ToArray();

        void ProcessSection(int absoluteStart, int absoluteEnd, bool cardholderSection, string? siteText, string? cardholder)
        {
            if (absoluteEnd <= absoluteStart)
            {
                return;
            }

            var sectionText = combinedText[absoluteStart..absoluteEnd];
            foreach (var (runChunk, runOffsetInSection) in SplitMobilTransactionalRunsWithOffsets(sectionText))
            {
                foreach (var (segment, segmentOffsetInRun) in ExtractMobilDateAnchoredSegmentsWithOffsets(runChunk, cardholderSection))
                {
                    var absoluteIndex = absoluteStart + runOffsetInSection + segmentOffsetInRun;
                    results.Add((
                        new SupplierPdfCandidate(segment, SiteText: siteText, CardholderText: cardholder),
                        absoluteIndex));
                }
            }
        }

        if (cardMatches.Length == 0)
        {
            ProcessSection(0, combinedText.Length, cardholderSection: false, siteText: null, cardholder: null);
            return results;
        }

        var leaderLength = cardMatches[0].Index;
        if (leaderLength > 0)
        {
            ProcessSection(0, leaderLength, cardholderSection: false, siteText: null, cardholder: null);
        }

        for (var cardIndex = 0; cardIndex < cardMatches.Length; cardIndex++)
        {
            var cardMatch = cardMatches[cardIndex];
            var sectionStart = cardMatch.Index + cardMatch.Length;
            var sectionEnd = cardIndex + 1 < cardMatches.Length ? cardMatches[cardIndex + 1].Index : combinedText.Length;
            var cardholder = cardMatch.Groups["name"].Value.Trim();
            var siteText = MobilCardholderSuffixPattern().Replace(cardholder, string.Empty).Trim();

            ProcessSection(sectionStart, sectionEnd, cardholderSection: true, siteText, cardholder);
        }

        return results;
    }

    private static IEnumerable<(string Segment, int StartOffset)> ExtractMobilDateAnchoredSegmentsWithOffsets(
        string text,
        bool cardholderSection)
    {
        var matches = MobilTransactionDateAnchorPattern().Matches(text);
        for (var index = 0; index < matches.Count; index++)
        {
            var start = matches[index].Index;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : text.Length;
            var segment = text[start..end].Trim();
            if (!ShouldDiscardMobilNoiseSegment(segment, cardholderSection))
            {
                yield return (segment, start);
            }
        }
    }

    private static bool ShouldDiscardMobilNoiseSegment(string segment, bool cardholderSection)
    {
        var compact = WhitespacePattern().Replace(segment, " ");
        if (compact.Length < 14)
        {
            return true;
        }

        if (compact.Contains("Payment Advice", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (compact.Contains("Account Summary by Cardholder", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (compact.Contains("Closing balance", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (compact.Contains("Payment received", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (compact.Contains("Direct Debit", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("will be Direct Debited", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (compact.Contains("DETACH HERE", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (compact.Contains("Transaction activity (see attached)", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (compact.Contains("Unused Mobilcard", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (compact.Contains("Sub Totals", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (compact.Contains("Card No:", StringComparison.OrdinalIgnoreCase)
            && !compact.Contains("Synergy", StringComparison.OrdinalIgnoreCase)
            && !compact.Contains("Diesel", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (compact.StartsWith("N = National Price", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (compact.Contains("Continued from previous page", StringComparison.OrdinalIgnoreCase)
            && !MobilTransactionDateAnchorPattern().IsMatch(segment))
        {
            return true;
        }

        if (compact.Contains("Continued over page", StringComparison.OrdinalIgnoreCase)
            && !MobilTransactionDateAnchorPattern().IsMatch(segment))
        {
            return true;
        }

        if (compact.Contains("TOTAL CHARGES DUE", StringComparison.OrdinalIgnoreCase)
            && !MobilLitresDigitDotDigitLBeforeLetterPattern().IsMatch(segment)
            && !LitresPattern().IsMatch(segment))
        {
            return true;
        }

        if (TotalChargesSummaryPattern().IsMatch(compact))
        {
            return true;
        }

        var hasLitres = MobilLitresDigitDotDigitLBeforeLetterPattern().IsMatch(segment)
            || LitresPattern().IsMatch(segment)
            || MobilInlineQuantityPattern().IsMatch(segment);

        if (!hasLitres)
        {
            return true;
        }

        var hasFuelContext = MobilBrandWordPattern().IsMatch(compact)
            || compact.Contains("Synergy", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("Diesel", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("Unleaded", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("Petrol", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("Efficient", StringComparison.OrdinalIgnoreCase);

        if (!hasFuelContext && !cardholderSection)
        {
            return true;
        }

        return false;
    }

    private static string SanitizePdfControlCharacters(string text) =>
        PdfControlCharactersPattern().Replace(text, " ");

    /// <summary>
    /// Splits Mobil blobs where multiple DD/MM/YYYY rows were flattened into a single PDF text run without line breaks.
    /// </summary>
    private static IEnumerable<(string Chunk, int StartOffset)> SplitMobilTransactionalRunsWithOffsets(string text)
    {
        var matches = MobilBlobSegmentBoundaryPattern().Matches(text);
        if (matches.Count <= 1)
        {
            yield return (text, 0);
            yield break;
        }

        for (var index = 0; index < matches.Count; index++)
        {
            var start = matches[index].Index;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : text.Length;
            var chunk = text[start..end];
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                yield return (chunk, start);
            }
        }
    }

    private static DateOnly? NormaliseSupplierDate(string rawDate)
    {
        var trimmed = rawDate.Trim();

        var mergedSlash = MobilSlashTwoDigitYearWithTimePattern().Match(trimmed);
        if (mergedSlash.Success)
        {
            trimmed = mergedSlash.Groups["date"].Value;
        }

        var dateText = TwoDigitYearDatePattern().IsMatch(trimmed)
            ? TwoDigitYearDatePattern().Replace(trimmed, match => $"{match.Groups["day"].Value} {match.Groups["month"].Value} 20{match.Groups["year"].Value}")
            : ExpandDdMmYySlashToFourDigitYear(trimmed);

        var result = DateNormaliser.NormaliseText(dateText);
        return result.Success ? result.NormalisedValue : null;
    }

    /// <summary>
    /// Converts dd/MM/yy text (already stripped of HH:mm if present) to dd/MM/yyyy for date normaliser.
    /// </summary>
    private static string ExpandDdMmYySlashToFourDigitYear(string text)
    {
        var match = SlashDdMmYyOnlyPattern().Match(text.Trim());
        if (!match.Success)
        {
            return text;
        }

        return $"{match.Groups["d"].Value}/{match.Groups["m"].Value}/20{match.Groups["y"].Value}";
    }

    private static bool TryParseMobilConcatenatedLitresBeforeProduct(string line, [NotNullWhen(true)] out string? litresText)
    {
        litresText = null;
        Match? last = null;
        foreach (Match match in MobilLitresDigitDotDigitLBeforeLetterPattern().Matches(line))
        {
            last = match;
        }

        if (last is null || !last.Success)
        {
            return false;
        }

        var numericToken = last.Value.AsSpan(0, last.Length - 1);
        var dotIndex = numericToken.LastIndexOf('.');
        if (dotIndex <= 0 || dotIndex >= numericToken.Length - 1)
        {
            return false;
        }

        var intPart = numericToken[..dotIndex].ToString();
        var frac = numericToken[(dotIndex + 1)..].ToString();

        return TryResolveConcatenatedVoucherAndLitres(intPart, frac, out litresText);
    }

    /// <summary>
    /// Splits voucher digits glued to litres (e.g. 057878 + 7.74 → 0578787.74) using litre magnitude heuristics.
    /// </summary>
    private static bool TryResolveConcatenatedVoucherAndLitres(
        string intPart,
        string frac,
        [NotNullWhen(true)] out string? litresText)
    {
        litresText = null;
        var combined = $"{intPart}.{frac}";
        if (intPart.Length < 7)
        {
            var simple = LitresNormaliser.Normalise(combined);
            if (simple.Success && simple.NormalisedValue is not null)
            {
                var v = simple.NormalisedValue.Value.Value;
                if (v is > 0 and < 5000m)
                {
                    litresText = combined;
                    return true;
                }
            }
        }

        for (var voucherLen = intPart.Length - 1; voucherLen >= 0; voucherLen--)
        {
            var litresWhole = intPart[voucherLen..];
            if (litresWhole.Length == 0)
            {
                continue;
            }

            var candidate = $"{litresWhole}.{frac}";
            var result = LitresNormaliser.Normalise(candidate);
            if (!result.Success || result.NormalisedValue is null)
            {
                continue;
            }

            var value = result.NormalisedValue.Value.Value;
            if (value is <= 0 or >= 5000m)
            {
                continue;
            }

            litresText = candidate;
            return true;
        }

        return false;
    }

    private static string? ExtractMobilConcatenatedVoucher(string line)
    {
        Match? last = null;
        foreach (Match match in MobilLitresDigitDotDigitLBeforeLetterPattern().Matches(line))
        {
            last = match;
        }

        if (last is null || !last.Success)
        {
            return null;
        }

        var numericToken = last.Value.AsSpan(0, last.Length - 1);
        var dotIndex = numericToken.LastIndexOf('.');
        if (dotIndex <= 0)
        {
            return null;
        }

        var intPart = numericToken[..dotIndex].ToString();
        var frac = numericToken[(dotIndex + 1)..].ToString();

        if (!TryResolveConcatenatedVoucherAndLitres(intPart, frac, out var litresText))
        {
            return null;
        }

        var litresWhole = litresText[..litresText.IndexOf('.')];
        if (litresWhole.Length == 0 || litresWhole.Length > intPart.Length)
        {
            return null;
        }

        if (!intPart.EndsWith(litresWhole, StringComparison.Ordinal))
        {
            return null;
        }

        var voucher = intPart[..(intPart.Length - litresWhole.Length)];
        return voucher.Length >= 4 ? voucher : null;
    }

    private static string NormaliseMobilPageText(string pageText) =>
        ExpandMobilBlobSpacing(NormaliseText(pageText));

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

    private static string ExpandMobilBlobSpacing(string text) =>
        text
            .Replace("LSynergy", "L Synergy", StringComparison.Ordinal)
            .Replace("LMobil", "L Mobil", StringComparison.Ordinal)
            .Replace("ExtraUnleaded", "Extra Unleaded", StringComparison.Ordinal)
            .Replace("Unleaded3.", "Unleaded 3.", StringComparison.Ordinal)
            .Replace("Efficient3.", "Efficient 3.", StringComparison.Ordinal)
            .Replace("R3.", "R 3.", StringComparison.Ordinal);

    /// <summary>
    /// dd/MM/yy optional HH:mm (Mobil merged PDF tokens).
    /// </summary>
    [GeneratedRegex(@"^(?<date>\d{1,2}/\d{1,2}/\d{2})\d{2}:\d{2}$")]
    private static partial Regex MobilSlashTwoDigitYearWithTimePattern();

    [GeneratedRegex(@"^(?<d>\d{1,2})/(?<m>\d{1,2})/(?<y>\d{2})$")]
    private static partial Regex SlashDdMmYyOnlyPattern();

    [GeneratedRegex(@"\d+\.\d+L(?=\s*[A-Za-z])")]
    private static partial Regex MobilLitresDigitDotDigitLBeforeLetterPattern();

    /// <summary>
    /// Mobil transaction boundaries (amount totals often end with a digit immediately before the next dd/MM/yyHH:mm).
    /// </summary>
    [GeneratedRegex(@"\d{1,2}/\d{1,2}/\d{2}\d{2}:\d{2}(?!\d)|\d{1,2}/\d{1,2}/\d{4}\b(?!:\d{2}\b)|\d{1,2}/\d{1,2}/\d{2}(?!\d{2}:)", RegexOptions.None)]
    private static partial Regex MobilBlobSegmentBoundaryPattern();

    [GeneratedRegex(@"\bMobil\b", RegexOptions.IgnoreCase)]
    private static partial Regex MobilBrandWordPattern();

    [GeneratedRegex(@"\d{1,2}/\d{1,2}/\d{2}\d{2}:\d{2}(?!\d)|\d{1,2}[/-]\d{1,2}[/-]\d{4}\b(?!:\d{2}\b)|\d{1,2}[/-]\d{1,2}[/-]\d{2}(?!\d{2}:)", RegexOptions.IgnoreCase)]
    private static partial Regex MobilTransactionDateAnchorPattern();

    [GeneratedRegex(@"Total\$\d", RegexOptions.IgnoreCase)]
    private static partial Regex TotalChargesSummaryPattern();

    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\u007F]", RegexOptions.None)]
    private static partial Regex PdfControlCharactersPattern();

    [GeneratedRegex(@"\b\d{1,2}/\d{1,2}/\d{2}\d{2}:\d{2}(?!\d)|\b\d{1,2}[/-]\d{1,2}[/-]\d{4}\b(?!:\d{2}\b)|\b\d{1,2}[/-]\d{1,2}[/-]\d{2}(?!\d{2}:)|\b\d{1,2}\s+[A-Za-z]{3,9}\s+\d{2,4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex SupplierDatePattern();

    [GeneratedRegex(@"\b(?<day>\d{1,2})\s+(?<month>[A-Za-z]{3,9})\s+(?<year>\d{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex TwoDigitYearDatePattern();

    [GeneratedRegex(@"(?<value>-?\d+(?:[.,]\d+)?)\s*(?:L|LT|LTR|LITRE|LITRES)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LitresPattern();

    [GeneratedRegex(@"\b(?<value>-?\d+(?:[.,]\d+)?)\s+(?=(?:Diesel|Petrol|(?:91|95)\s+UNLEADED|UNLEADED)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex MobilInlineQuantityPattern();

    [GeneratedRegex(@"\$?\s*-?\d+(?:[.,]\d{2})\b")]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"\$\s*-?\d+(?:[.,]\d{2})\b")]
    private static partial Regex CurrencyAmountPattern();

    [GeneratedRegex(@"\b(?:INV|INVOICE|VOUCHER|REF|CRD)[:#\s-]*[A-Za-z0-9-]+\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReferencePattern();

    [GeneratedRegex(@"\b(?:DIESEL|PETROL|FUEL|UNLEADED|LITRES?|LTR|LT)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ProductWordsPattern();

    [GeneratedRegex(@"\b(?:DIESEL|91\s+UNLEADED|95\s+UNLEADED|UNLEADED|PETROL|Synergy\s+Extra\s+Unleaded|Synergy)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ProductNamePattern();

    [GeneratedRegex(@"CARD\s+(?:NUMBER|NO\.?)\s*:\s*\S+\s+NAME:\s*(?<name>.*?)(?=\s+\d{1,2}[/-]\d{1,2}[/-]\d{4}\b(?!:\d{2}\b)|\s+\d{1,2}/\d{1,2}/\d{2}\d{2}:\d{2}(?!\d)|\s+\d{1,2}\s+[A-Za-z]{3,9}\s+\d{2,4}\b|$)", RegexOptions.IgnoreCase)]
    private static partial Regex MobilCardholderPattern();

    [GeneratedRegex(@"\s+(?:\d+/\d+|\d+)$")]
    private static partial Regex MobilCardholderSuffixPattern();

    [GeneratedRegex(@"\b\d{5,}\b")]
    private static partial Regex CardNumberPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}

internal sealed record SupplierPdfCandidate(string Text, string? SiteText = null, string? CardholderText = null);
