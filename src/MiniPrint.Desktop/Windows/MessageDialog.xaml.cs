using System.Windows;
using System.Windows.Media;

namespace MiniPrint.Desktop.Windows;

/// <summary>
/// Caixa de mensagem no padrão visual do aplicativo. Substitui o
/// <see cref="MessageBox"/> do sistema mantendo a mesma assinatura e os
/// mesmos resultados (botões "Sim"/"Não"/"OK" em português).
/// </summary>
public partial class MessageDialog : Window
{
    private readonly MessageBoxButton _buttons;
    private MessageBoxResult _result;

    private MessageDialog(string text, string caption, MessageBoxButton buttons, MessageBoxImage image)
    {
        InitializeComponent();

        _buttons = buttons;
        Title = string.IsNullOrWhiteSpace(caption) ? "MiniPrint Desktop" : caption;
        MessageText.Text = text;
        ApplyImage(image);

        switch (buttons)
        {
            case MessageBoxButton.YesNo:
                PrimaryButton.Content = "Sim";
                SecondaryButton.Content = "Não";
                _result = MessageBoxResult.No;
                SecondaryButton.IsCancel = true;
                break;
            case MessageBoxButton.YesNoCancel:
                PrimaryButton.Content = "Sim";
                SecondaryButton.Content = "Não";
                _result = MessageBoxResult.Cancel;
                break;
            case MessageBoxButton.OKCancel:
                PrimaryButton.Content = "OK";
                SecondaryButton.Content = "Cancelar";
                _result = MessageBoxResult.Cancel;
                SecondaryButton.IsCancel = true;
                break;
            default:
                PrimaryButton.Content = "OK";
                SecondaryButton.Visibility = Visibility.Collapsed;
                PrimaryButton.IsCancel = true;
                _result = MessageBoxResult.OK;
                break;
        }
    }

    /// <summary>Exibe a caixa de forma modal e devolve o botão escolhido.</summary>
    public static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons, MessageBoxImage image) =>
        Show(Application.Current?.MainWindow, text, caption, buttons, image);

    /// <summary>Exibe a caixa sobre a janela informada.</summary>
    public static MessageBoxResult Show(
        Window? owner,
        string text,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image)
    {
        var dialog = new MessageDialog(text, caption, buttons, image);
        if (owner is { IsVisible: true } && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
        return dialog._result;
    }

    private void ApplyImage(MessageBoxImage image)
    {
        // Segoe Fluent Icons / MDL2: Info, Warning, Error, Help.
        var (glyph, foreground, background) = image switch
        {
            MessageBoxImage.Error => ("", "DangerBrush", "DangerSoftBrush"),
            MessageBoxImage.Warning => ("", "WarningBrush", "WarningSoftBrush"),
            MessageBoxImage.Question => ("", "AccentBrush", "AccentSoftBrush"),
            _ => ("", "InfoBrush", "InfoSoftBrush"),
        };

        IconGlyph.Text = glyph;
        IconGlyph.Foreground = (Brush)FindResource(foreground);
        IconBadge.Background = (Brush)FindResource(background);
    }

    private void OnPrimary(object sender, RoutedEventArgs e)
    {
        _result = _buttons switch
        {
            MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel => MessageBoxResult.Yes,
            _ => MessageBoxResult.OK,
        };
        Close();
    }

    private void OnSecondary(object sender, RoutedEventArgs e)
    {
        _result = _buttons switch
        {
            MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel => MessageBoxResult.No,
            _ => MessageBoxResult.Cancel,
        };
        Close();
    }
}
