using FuelRecon.Domain;
using FuelRecon.Infrastructure.Excel;

namespace FuelRecon.Tests;

public class ExcelRealSampleParserSmokeTests
{
    [Theory]
    [InlineData("branch litres.xlsx")]
    [InlineData("cars+ statement.xlsx")]
    public void Parser_handles_client_sample_when_present_without_crashing(string fileName)
    {
        var path = Path.Combine("samples", "client-raw", fileName);
        if (!File.Exists(path))
        {
            return;
        }

        var reader = new ClosedXmlExcelWorkbookReader();
        var workbookRead = reader.ReadWorkbook(path);
        Assert.True(workbookRead.Success);
        Assert.NotNull(workbookRead.Workbook);
        Assert.NotEmpty(workbookRead.Workbook.Sheets);

        var period = new FuelPeriod(2026, 4);
        var resolver = CreateSampleBranchAliasResolver();

        if (fileName.Contains("branch", StringComparison.OrdinalIgnoreCase))
        {
            var result = new BranchLitresExcelParser(reader).Parse(path, period, resolver);
            Assert.True(result.RowCount > 0);
            Assert.True(result.Entries.Count > 0 || result.Issues.Count > 0);
        }
        else
        {
            var result = new CarsBillingExcelParser(reader).Parse(path, period, resolver);
            Assert.True(result.RowCount > 0);
            Assert.True(result.Entries.Count > 0 || result.Issues.Count > 0);
        }
    }

    private static BranchAliasResolver CreateSampleBranchAliasResolver()
    {
        var taupo = new BranchMaster(new CanonicalBranchId("TAUPO"), "Taupo");
        var kerikeri = new BranchMaster(new CanonicalBranchId("KERIKERI"), "Kerikeri");
        var whangarei = new BranchMaster(new CanonicalBranchId("WHANGAREI"), "Whangarei");

        return new BranchAliasResolver(
            [taupo, kerikeri, whangarei],
            [
                new BranchAlias("Taupo", taupo.Id),
                new BranchAlias("Mobil Taupo", taupo.Id),
                new BranchAlias("Mobile - Taupo", taupo.Id),
                new BranchAlias("Hertz Taupo", taupo.Id),
                new BranchAlias("Caltex Kerikeri", kerikeri.Id),
                new BranchAlias("Caltex Whangarei", whangarei.Id),
                new BranchAlias("Kerikeri", kerikeri.Id),
                new BranchAlias("Whangarei", whangarei.Id),
            ]);
    }
}
