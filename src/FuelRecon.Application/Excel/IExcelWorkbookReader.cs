namespace FuelRecon.Application.Excel;

public interface IExcelWorkbookReader
{
    ExcelWorkbookReadResult ReadWorkbook(string filePath);
}
