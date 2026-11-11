using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MiniPrint.Desktop.Converters;

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool flag && !flag;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool flag && !flag;
}

public sealed class BooleanToVisibilityInverseConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool flag && flag ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility visibility && visibility != Visibility.Visible;
}

public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visível quando a coleção está vazia (contagem zero).</summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int i => i,
            System.Collections.ICollection collection => collection.Count,
            _ => 0,
        };

        return count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Converte um valor em pixels para largura de coluna (GridLength).</summary>
public sealed class DoubleToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var width = value switch
        {
            double d => d,
            int i => i,
            _ => 0.0,
        };

        if (width <= 0)
        {
            return new GridLength(0);
        }

        return new GridLength(width, GridUnitType.Pixel);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is GridLength length ? length.Value : 0.0;
}

/// <summary>Verdadeiro quando o número é diferente de zero.</summary>
public sealed class NonZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int i => i,
            double d => (int)d,
            long l => (int)l,
            System.Collections.ICollection collection => collection.Count,
            _ => 0,
        };

        return count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Converte pontos de impressão (1/72") em DIPs do WPF (1/96").</summary>
public sealed class PointsToDipConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var points = value switch
        {
            double d => d,
            int i => i,
            _ => 0.0,
        };

        return points * 96.0 / 72.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var dip = value is double d ? d : 0.0;
        return dip * 72.0 / 96.0;
    }
}

/// <summary>Converte o alinhamento do domínio para o alinhamento do WPF.</summary>
public sealed class AlignmentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            Core.Configuration.TextAlignment.Centro => System.Windows.TextAlignment.Center,
            Core.Configuration.TextAlignment.Direita => System.Windows.TextAlignment.Right,
            _ => System.Windows.TextAlignment.Left,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is System.Windows.TextAlignment.Center
            ? Core.Configuration.TextAlignment.Centro
            : value is System.Windows.TextAlignment.Right
                ? Core.Configuration.TextAlignment.Direita
                : Core.Configuration.TextAlignment.Esquerda;
}

/// <summary>Transforma a imagem embutida (base64) em fonte para o canvas.</summary>
public sealed class Base64ImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || text.Length == 0)
        {
            return null;
        }

        try
        {
            var bytes = System.Convert.FromBase64String(text);
            using var stream = new System.IO.MemoryStream(bytes);
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visível quando o texto está vazio (para dicas).</summary>
public sealed class EmptyTextToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Converte uma fração 0-1 em largura de barra de progresso.</summary>
public sealed class FractionToWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fraction = value is double d ? Math.Clamp(d, 0, 1) : 0;
        var total = parameter is string text && double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 100;
        return fraction * total;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Texto amigável para os modos de ordenação da lista de modelos.</summary>
public sealed class SortModeTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            ViewModels.TemplateSortMode.NomeAscendente => "Nome (A–Z)",
            ViewModels.TemplateSortMode.AtualizacaoRecente => "Atualizados recentemente",
            ViewModels.TemplateSortMode.CriacaoRecente => "Criados recentemente",
            _ => value?.ToString() ?? string.Empty,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
