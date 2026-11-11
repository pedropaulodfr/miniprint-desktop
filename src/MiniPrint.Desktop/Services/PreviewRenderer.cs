using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiniPrint.Core.Models;
using MiniPrint.Imaging;

namespace MiniPrint.Desktop.Services;

/// <summary>Conversões entre bitmaps GDI+ e imagens WPF.</summary>
public static class PreviewRenderer
{
    /// <summary>
    /// Converte o bitmap de 1 bit em uma imagem WPF. A imagem é congelada para
    /// poder ser usada em outra thread sem custo de sincronização.
    /// </summary>
    public static BitmapSource ToBitmapSource(
        MonochromeBitmap bitmap,
        int scale = 1,
        System.Drawing.Color? ink = null,
        System.Drawing.Color? paper = null)
    {
        using var preview = ImageProcessor.ToPreview(bitmap, scale, ink, paper);
        return ToBitmapSource(preview);
    }

    public static BitmapSource ToBitmapSource(System.Drawing.Bitmap bitmap)
    {
        using var memory = new MemoryStream();
        bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
        memory.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        image.StreamSource = memory;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public static BitmapSource? TryLoadFile(string path)
    {
        try
        {
            using var bitmap = ImageProcessor.Load(path);
            return ToBitmapSource(bitmap);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Imagem "papel" para o fundo da pré-visualização.</summary>
    public static SolidColorBrush PaperBrush => new(System.Windows.Media.Color.FromRgb(0xFB, 0xFB, 0xF7));

    public static SolidColorBrush InkBrush => new(System.Windows.Media.Color.FromRgb(0x18, 0x18, 0x18));

    /// <summary>Codifica o bitmap como PNG para exportação.</summary>
    public static void SavePng(MonochromeBitmap bitmap, string path)
    {
        using var preview = ImageProcessor.ToPreview(bitmap);
        preview.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
