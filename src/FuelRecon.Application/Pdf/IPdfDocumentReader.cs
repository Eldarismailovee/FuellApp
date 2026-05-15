namespace FuelRecon.Application.Pdf;

public interface IPdfDocumentReader
{
    PdfReadResult ReadDocument(string filePath);
}
