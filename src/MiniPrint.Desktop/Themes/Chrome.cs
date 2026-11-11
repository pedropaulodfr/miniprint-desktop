using System.Windows;

namespace MiniPrint.Desktop.Themes;

/// <summary>
/// Propriedades anexadas que permitem à janela principal injetar conteúdo
/// (marca, cartão de estado) e ícones nos estilos de navegação, mantendo o
/// estilo da barra lateral genérico e reutilizável.
/// </summary>
public static class Chrome
{
    public static readonly DependencyProperty SidebarHeaderProperty =
        DependencyProperty.RegisterAttached("SidebarHeader", typeof(object), typeof(Chrome), new PropertyMetadata(null));

    public static readonly DependencyProperty SidebarFooterProperty =
        DependencyProperty.RegisterAttached("SidebarFooter", typeof(object), typeof(Chrome), new PropertyMetadata(null));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.RegisterAttached("Icon", typeof(string), typeof(Chrome), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.RegisterAttached("IsCompact", typeof(bool), typeof(Chrome), new PropertyMetadata(false));

    public static bool GetIsCompact(DependencyObject obj) => (bool)obj.GetValue(IsCompactProperty);
    public static void SetIsCompact(DependencyObject obj, bool value) => obj.SetValue(IsCompactProperty, value);

    public static object? GetSidebarHeader(DependencyObject obj) => obj.GetValue(SidebarHeaderProperty);
    public static void SetSidebarHeader(DependencyObject obj, object? value) => obj.SetValue(SidebarHeaderProperty, value);

    public static object? GetSidebarFooter(DependencyObject obj) => obj.GetValue(SidebarFooterProperty);
    public static void SetSidebarFooter(DependencyObject obj, object? value) => obj.SetValue(SidebarFooterProperty, value);

    public static string GetIcon(DependencyObject obj) => (string)obj.GetValue(IconProperty);
    public static void SetIcon(DependencyObject obj, string value) => obj.SetValue(IconProperty, value);
}
