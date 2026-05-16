using System.Globalization;
using System.Text;
using FuelRecon.Application.Pdf.Export;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace FuelRecon.Infrastructure.Pdf;

/// <summary>
/// Branch report PDF layout using PDFsharp (TASK-11.02); totals are taken from <see cref="BranchReportPdfDocumentContent.Totals"/>.
/// </summary>
public sealed class PdfSharpBranchReportPdfRenderer : IBranchReportPdfRenderer
{
    private const string EmbeddedFontResourceName = "FuelRecon.Infrastructure.Fonts.DejaVuSans.ttf";

    private static readonly object FontInitLock = new();

    private static bool _fontResolverRegistered;

    private static void EnsureFontResolverRegistered()
    {
        lock (FontInitLock)
        {
            if (_fontResolverRegistered)
            {
                return;
            }

            if (GlobalFontSettings.FontResolver is not null)
            {
                _fontResolverRegistered = true;
                return;
            }

            var assembly = typeof(PdfSharpBranchReportPdfRenderer).Assembly;
            using var stream = assembly.GetManifestResourceStream(EmbeddedFontResourceName);
            if (stream is null)
            {
                var known = string.Join(", ", assembly.GetManifestResourceNames());
                throw new InvalidOperationException(
                    $"Missing embedded font resource '{EmbeddedFontResourceName}'. Known resources: {known}");
            }

            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            GlobalFontSettings.FontResolver = new BranchReportPdfFontResolver(copy.ToArray());
            _fontResolverRegistered = true;
        }
    }

    private const double Margin = 40;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public void Render(BranchReportPdfDocumentContent content, Stream output)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(output);

        EnsureFontResolverRegistered();

        var doc = new PdfDocument();
        doc.Info.Title = "Fuel reconciliation branch report";

        var layout = new LayoutContext(doc, Margin);

        layout.DrawTitle("Fuel reconciliation — branch report");

        var report = content.ReportVersion;
        var totals = content.Totals;
        var template = content.Template;

        layout.DrawLines(
            $"Branch: {report.BranchId.Value}",
            $"Period: {report.Period.Year}-{report.Period.Month:D2}",
            $"Run ID: {report.RunId:D}",
            $"Report version: {report.VersionNumber}",
            $"Report status: {report.Status}",
            $"PDF template: {template.TemplateName} (v{template.TemplateVersion})");

        layout.SectionGap();

        layout.DrawSubtitle("Totals (branch report)");
        layout.DrawLines(
            $"Supplier litres (L): {totals.SupplierLitres.Value.ToString("N2", Invariant)}",
            $"Branch litres (L): {totals.BranchLitres.Value.ToString("N2", Invariant)}",
            $"Billed litres (L): {totals.BilledLitres.Value.ToString("N2", Invariant)}",
            $"Unbilled litres (L): {totals.UnbilledLitres.Value.ToString("N2", Invariant)}",
            $"Estimated recovery: {totals.EstimatedRecovery.Value.ToString("N2", Invariant)}",
            $"Review item count: {totals.ReviewCount}");

        layout.SectionGap();

        layout.DrawSubtitle("Prepared / approved");
        var signing = content.Signing;
        layout.DrawLines($"Prepared at (UTC): {signing.PreparedAtUtc:O}");
        layout.DrawLines($"Prepared by: {signing.PreparedBy}");

        if (signing.ApprovedAtUtc is { } approvedAt && !string.IsNullOrWhiteSpace(signing.ApprovedBy))
        {
            layout.DrawLines(
                $"Approved at (UTC): {approvedAt:O}",
                $"Approved by: {signing.ApprovedBy.Trim()}");

            if (!string.IsNullOrWhiteSpace(signing.ApprovalNote))
            {
                layout.DrawLines($"Approval note: {Truncate(signing.ApprovalNote.Trim(), 512)}");
            }
        }
        else
        {
            layout.DrawLines("Approved: not recorded for this report version.");
        }

        layout.SectionGap();

        var readModel = content.ReadModel;
        layout.DrawSubtitle($"Exception items ({readModel.ExceptionItems.Count})");
        if (readModel.ExceptionItems.Count == 0)
        {
            layout.DrawLines("No exception-class items for this branch in this run.");
        }
        else
        {
            layout.DrawTableHeader("Item ID", "System status", "Resolution", "Reasons", "Notes");
            foreach (var item in readModel.ExceptionItems)
            {
                var reasons = item.ReasonCodes.Count == 0 ? "—" : string.Join(", ", item.ReasonCodes);
                layout.DrawTableRow(
                    item.Id.ToString("D"),
                    item.SystemStatus.ToString(),
                    item.ResolutionStatus.ToString(),
                    Truncate(reasons, 120),
                    Truncate(item.HumanReadableReason ?? "—", 160));
            }
        }

        layout.SectionGap();

        layout.DrawSubtitle($"Unresolved / in-review items ({readModel.UnresolvedItems.Count})");
        layout.DrawLines(
            readModel.UnresolvedItems.Count == 0
                ? "None."
                : string.Join(
                    "; ",
                    readModel.UnresolvedItems.Select(i => $"{i.Id:D}:{i.SystemStatus}/{i.ResolutionStatus}")));

        layout.FinishPage();
        doc.Save(output, closeStream: false);
    }

    private static string Truncate(string value, int maxChars)
    {
        if (value.Length <= maxChars)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, maxChars - 1), "…");
    }

    private sealed class LayoutContext
    {
        private const double LineHeight = 14;

        private const double TitleSize = 16;

        private const double SubtitleSize = 12;

        private const double BodySize = 10;

        private const double TableHeaderSize = 9;

        private readonly PdfDocument _document;

        private readonly double _margin;

        private PdfPage _page = null!;

        private XGraphics _gfx = null!;

        private XFont _titleFont = null!;

        private XFont _subtitleFont = null!;

        private XFont _bodyFont = null!;

        private XFont _tableFont = null!;

        private double _y;

        public LayoutContext(PdfDocument document, double margin)
        {
            _document = document;
            _margin = margin;
            StartNewPage();
            _titleFont = new XFont("Helvetica", TitleSize, XFontStyleEx.Bold);
            _subtitleFont = new XFont("Helvetica", SubtitleSize, XFontStyleEx.Bold);
            _bodyFont = new XFont("Helvetica", BodySize, XFontStyleEx.Regular);
            _tableFont = new XFont("Helvetica", TableHeaderSize, XFontStyleEx.Regular);
        }

        public void DrawTitle(string text)
        {
            EnsureVerticalSpace(LineHeight * 2);
            Draw(text, _titleFont, LineHeight * 1.2);
        }

        public void DrawSubtitle(string text)
        {
            EnsureVerticalSpace(LineHeight * 1.5);
            Draw(text, _subtitleFont, LineHeight * 1.3);
        }

        public void DrawLines(params string[] lines)
        {
            foreach (var line in lines)
            {
                DrawWrapped(line, _bodyFont, LineHeight);
            }
        }

        public void DrawTableHeader(params string[] columns)
        {
            EnsureVerticalSpace(LineHeight * 1.2);
            var row = FormatRow(columns);
            Draw(row, _tableFont, LineHeight * 1.1);
            Draw(new string('─', Math.Min(160, row.Length)), _tableFont, LineHeight);
        }

        public void DrawTableRow(params string[] columns)
        {
            var row = FormatRow(columns);
            DrawWrapped(row, _tableFont, LineHeight * 0.95);
        }

        public void SectionGap() => _y += LineHeight * 0.8;

        public void FinishPage()
        {
            _gfx.Dispose();
        }

        private static string FormatRow(string[] columns)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < columns.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(" | ");
                }

                builder.Append(columns[i].Replace('\r', ' ').Replace('\n', ' '));
            }

            return builder.ToString();
        }

        private void DrawWrapped(string text, XFont font, double linePitch)
        {
            const int approxCharsPerLine = 96;
            var normalised = text.Replace('\r', ' ').Replace('\n', ' ');
            foreach (var chunk in ChunkByLength(normalised, approxCharsPerLine))
            {
                EnsureVerticalSpace(linePitch);
                Draw(chunk, font, linePitch);
            }
        }

        private static IEnumerable<string> ChunkByLength(string text, int size)
        {
            if (text.Length == 0)
            {
                yield return string.Empty;
                yield break;
            }

            for (var i = 0; i < text.Length; i += size)
            {
                yield return text.Substring(i, Math.Min(size, text.Length - i));
            }
        }

        private void Draw(string text, XFont font, double linePitch)
        {
            var rect = new XRect(_margin, _y, _page.Width.Point - 2 * _margin, linePitch * 1.2);
            _gfx.DrawString(text, font, XBrushes.Black, rect, XStringFormats.TopLeft);
            _y += linePitch;
        }

        private void EnsureVerticalSpace(double needed)
        {
            var bottom = _page.Height.Point - _margin;
            if (_y + needed <= bottom)
            {
                return;
            }

            _gfx.Dispose();
            StartNewPage();
        }

        private void StartNewPage()
        {
            _page = _document.AddPage();
            _page.Width = XUnit.FromPoint(595);
            _page.Height = XUnit.FromPoint(842);
            _gfx = XGraphics.FromPdfPage(_page);
            _y = _margin;
        }
    }
}
