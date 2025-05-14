using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using QuickTechSystems.Application.Services.Interfaces;

namespace QuickTechSystems.Application.Services
{
    [SupportedOSPlatform("windows")]
    public class BarcodeService : IBarcodeService
    {
        public byte[] GenerateBarcode(string content, int width = 300, int height = 100)
        {
            var writer = new BarcodeWriter
            {
                Format = BarcodeFormat.CODE_128,
                Options = new EncodingOptions
                {
                    Width = width,
                    Height = height,
                    Margin = 2,
                    PureBarcode = false
                }
            };

            using var bitmap = writer.Write(content);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
    }
}