using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using MiniPrint.Core.Configuration;
using MiniPrint.Core.Models;
using MiniPrint.Protocol;

namespace MiniPrint.Imaging;

/// <summary>
/// Padrões de teste gerados localmente. Servem para os testes físicos de
/// largura, alinhamento, densidade e meio-tom — e também para validar o
/// pipeline sem imprimir nada.
/// </summary>
public static class TestPatterns
{
    /// <summary>
    /// Página de diagnóstico: régua de 384 pontos, texto, bloco de cinzas,
    /// xadrez de 1 pixel e barra de alinhamento nas bordas.
    /// </summary>
    public static MonochromeBitmap DiagnosticPage(int paperWidth, DitheringMode dithering)
    {
        const int height = 360;
        using var bitmap = new Bitmap(paperWidth, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            graphics.SmoothingMode = SmoothingMode.None;

            using var titleFont = new Font("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Point);
            using var bodyFont = new Font("Segoe UI", 7, FontStyle.Regular, GraphicsUnit.Point);

            graphics.DrawString("MiniPrint Desktop", titleFont, Brushes.Black, 6, 4);
            graphics.DrawString(
                $"Teste de impressão - {paperWidth} pontos por linha",
                bodyFont,
                Brushes.Black,
                6,
                26);

            // Régua: marcações a cada 32 pontos, numeradas a cada 64.
            var rulerY = 44;
            graphics.DrawLine(Pens.Black, 0, rulerY, paperWidth - 1, rulerY);
            for (var x = 0; x < paperWidth; x += 32)
            {
                var tall = (x % 64) == 0;
                graphics.DrawLine(Pens.Black, x, rulerY, x, rulerY + (tall ? 10 : 5));
                if (tall)
                {
                    graphics.DrawString((x).ToString(), bodyFont, Brushes.Black, x + 1, rulerY + 10);
                }
            }

            // Bordas: 2 pontos em cada extremidade para conferir a largura real.
            graphics.FillRectangle(Brushes.Black, 0, 70, 2, 40);
            graphics.FillRectangle(Brushes.Black, paperWidth - 2, 70, 2, 40);

            // Bloco de cinzas em degraus.
            var grayY = 70;
            for (var step = 0; step < 8; step++)
            {
                var level = 255 - (step * 32);
                using var brush = new SolidBrush(Color.FromArgb(level, level, level));
                graphics.FillRectangle(brush, 12 + (step * 32), grayY, 30, 40);
            }

            // Xadrez de 1 pixel (verifica orientação dos bits).
            var checkerY = 120;
            for (var y = 0; y < 32; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    if (((x + y) & 1) == 0)
                    {
                        bitmap.SetPixel(12 + x, checkerY + y, Color.Black);
                    }
                }
            }

            // Linhas de espessura crescente.
            for (var i = 0; i < 5; i++)
            {
                graphics.FillRectangle(Brushes.Black, 120, 120 + (i * 8), 200, i + 1);
            }

            graphics.DrawString(
                "Texto de referência: 0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdefghijklmnopqrstuvwxyz",
                bodyFont,
                Brushes.Black,
                new RectangleF(6, 190, paperWidth - 12, 60));

            // Degradê contínuo para avaliar dithering.
            var gradient = new Rectangle(12, 260, paperWidth - 24, 80);
            using (var brush = new LinearGradientBrush(gradient, Color.White, Color.Black, 0f))
            {
                graphics.FillRectangle(brush, gradient);
            }

            graphics.DrawString(
                $"Meio-tom: {dithering}",
                bodyFont,
                Brushes.Black,
                12,
                342);
        }

        var gray = Grayscale.FromBitmap(bitmap);
        var binary = Dithering.Apply(gray, paperWidth, height, dithering, 128);
        return CatPrinterRaster.PackPixels(paperWidth, height, binary);
    }

    /// <summary>Barra sólida de largura total — útil para conferir a alimentação final.</summary>
    public static MonochromeBitmap FeedRuler(int paperWidth)
    {
        const int height = 60;
        var pixels = new byte[paperWidth * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < paperWidth; x++)
            {
                pixels[(y * paperWidth) + x] =
                    (y < 2 || y >= height - 2 || x < 2 || x >= paperWidth - 2) ? (byte)1 : (byte)0;
            }
        }

        return CatPrinterRaster.PackPixels(paperWidth, height, pixels);
    }
}
