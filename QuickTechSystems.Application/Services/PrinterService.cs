// PrinterService.cs
using System.Drawing;
using System.Drawing.Printing;
using QuickTechSystems.Application.Services.Interfaces;

namespace QuickTechSystems.Application.Services
{
    public class PrinterService : IPrinterService
    {
        private string _selectedPrinter;

        public PrinterService()
        {
            _selectedPrinter = GetDefaultPrinter();
        }

        public void PrintBarcode(byte[] barcodeImage, string productName, string price)
        {
            using var printDocument = new PrintDocument();
            printDocument.PrinterSettings.PrinterName = _selectedPrinter;

            // Set custom paper size for thermal printer (adjust measurements as needed)
            var paperSize = new PaperSize("Custom", 400, 200); // Width: 40mm, Height: 20mm
            printDocument.DefaultPageSettings.PaperSize = paperSize;

            // Create bitmap from byte array
            using var ms = new MemoryStream(barcodeImage);
            using var barcodeImg = Image.FromStream(ms);

            printDocument.PrintPage += (sender, e) =>
            {
                // Calculate positions
                int startX = 10;
                int startY = 10;

                // Print product name
                using (var font = new Font("Arial", 8))
                {
                    e.Graphics.DrawString(productName, font, Brushes.Black, startX, startY);
                }

                // Print barcode image
                e.Graphics.DrawImage(barcodeImg, startX, startY + 20, 180, 60);

                // Print price
                using (var font = new Font("Arial", 8, FontStyle.Bold))
                {
                    e.Graphics.DrawString(price, font, Brushes.Black, startX, startY + 85);
                }
            };

            try
            {
                printDocument.Print();
            }
            catch (Exception ex)
            {
                throw new Exception($"Printing failed: {ex.Message}");
            }
        }

        public List<string> GetInstalledPrinters()
        {
            return PrinterSettings.InstalledPrinters.Cast<string>().ToList();
        }

        public void SetPrinter(string printerName)
        {
            if (GetInstalledPrinters().Contains(printerName))
            {
                _selectedPrinter = printerName;
            }
            else
            {
                throw new ArgumentException($"Printer '{printerName}' not found.");
            }
        }

        private string GetDefaultPrinter()
        {
            return new PrinterSettings().PrinterName;
        }
    }
}