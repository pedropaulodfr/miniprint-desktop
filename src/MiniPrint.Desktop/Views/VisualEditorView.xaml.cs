using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MiniPrint.Core.Templates;
using MiniPrint.Desktop.Editing;
using MiniPrint.Desktop.ViewModels;
using MiniPrint.Desktop.Windows;

namespace MiniPrint.Desktop.Views;

/// <summary>
/// Camada de cola entre o WPF e o editor. A lógica de interação fica no
/// <see cref="CanvasInteractionController"/> e no ViewModel; aqui só traduzimos
/// eventos, posicionamos sobreposições e cuidamos do layout da tela.
/// </summary>
public partial class VisualEditorView : UserControl
{
    private const string PaletteDragFormat = "MiniPrint.PaletteItem";
    private const string LayerDragFormat = "MiniPrint.LayerIndex";

    private CanvasInteractionController? _interaction;
    private VisualEditorViewModel? _viewModel;
    private FrameworkElement? _captureTarget;

    private string? _paletteDragId;
    private Point _paletteDragStart;
    private DateTime _lastDropAt = DateTime.MinValue;

    private Point _layerDragStart;
    private int _layerDragIndex = -1;
    private bool _syncingLayers;

    public VisualEditorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += (_, _) => Attach();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Attach();
        Focus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();

    private void Attach()
    {
        Detach();

        if (DataContext is not VisualEditorViewModel viewModel)
        {
            return;
        }

        _viewModel = viewModel;
        _interaction = new CanvasInteractionController(viewModel);
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        viewModel.FieldEditorRequested += OnFieldEditorRequested;
        viewModel.FindReplaceRequested += OnFindReplaceRequested;
        viewModel.Confirm = (title, message) =>
        {
            var owner = Window.GetWindow(this);
            var result = owner is null
                ? MiniPrint.Desktop.Windows.MessageDialog.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
                : MiniPrint.Desktop.Windows.MessageDialog.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        };

        UpdatePanelColumns();
        RedrawRuler();
        UpdateInlineEditor();
    }

    private void Detach()
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.FieldEditorRequested -= OnFieldEditorRequested;
        _viewModel.FindReplaceRequested -= OnFindReplaceRequested;
        _viewModel.Confirm = null;
        _viewModel = null;
        _interaction = null;
        _captureTarget = null;
    }

    private void OnFieldEditorRequested(object? sender, TemplateField? field) => OpenFieldEditor(field);

    private void OnFindReplaceRequested(object? sender, EventArgs e) => OpenFindReplace();

    private void OpenFieldEditor(TemplateField? field)
    {
        if (_viewModel is null)
        {
            return;
        }

        FieldEditorWindow.Show(Window.GetWindow(this), _viewModel, field);
        UpdateInlineEditor();
    }

    private void OpenFindReplace()
    {
        if (_viewModel is null)
        {
            return;
        }

        var window = new FindReplaceWindow(_viewModel);
        var owner = Window.GetWindow(this);
        if (owner is not null)
        {
            window.Owner = owner;
        }

        window.ShowDialog();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(VisualEditorViewModel.Zoom):
            case nameof(VisualEditorViewModel.PaperWidth):
            case nameof(VisualEditorViewModel.ShowRulers):
                RedrawRuler();
                UpdateInlineEditorBounds();
                break;
            case nameof(VisualEditorViewModel.LeftPanelWidth):
            case nameof(VisualEditorViewModel.RightPanelWidth):
            case nameof(VisualEditorViewModel.IsLeftPanelCollapsed):
            case nameof(VisualEditorViewModel.IsRightPanelCollapsed):
                UpdatePanelColumns();
                break;
            case nameof(VisualEditorViewModel.IsInlineEditing):
                UpdateInlineEditor();
                break;
            case nameof(VisualEditorViewModel.SelectedElement):
                UpdateInlineEditorBounds();
                break;
            case nameof(VisualEditorViewModel.IsPrintPreview):
                if (_viewModel?.IsPrintPreview == true)
                {
                    _viewModel.RefreshPreviewCommand.Execute(null);
                }

                break;
        }
    }

    // --------------------------------------------------------------- painéis

    private void UpdatePanelColumns()
    {
        if (_viewModel is null)
        {
            return;
        }

        var left = _viewModel.IsLeftPanelCollapsed ? 0 : _viewModel.LeftPanelWidth;
        var right = _viewModel.IsRightPanelCollapsed ? 0 : _viewModel.RightPanelWidth;

        LeftColumn.Width = new GridLength(left);
        LeftSplitterColumn.Width = new GridLength(_viewModel.IsLeftPanelCollapsed ? 0 : 6);
        RightColumn.Width = new GridLength(right);
        RightSplitterColumn.Width = new GridLength(_viewModel.IsRightPanelCollapsed ? 0 : 6);
    }

    private void OnLeftSplitterDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (_viewModel is null || LeftColumn.ActualWidth <= 0)
        {
            return;
        }

        _viewModel.LeftPanelWidth = LeftColumn.ActualWidth;
    }

    private void OnRightSplitterDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (_viewModel is null || RightColumn.ActualWidth <= 0)
        {
            return;
        }

        _viewModel.RightPanelWidth = RightColumn.ActualWidth;
    }

    // ---------------------------------------------------------------- régua

    private void RedrawRuler()
    {
        if (TopRuler is null || _viewModel is null)
        {
            return;
        }

        TopRuler.Children.Clear();
        TopRuler.Width = _viewModel.PaperWidth;

        if (!_viewModel.ShowRulers)
        {
            return;
        }

        var tickBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
        var labelBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));

        for (var x = 0; x <= _viewModel.PaperWidth; x += 8)
        {
            var major = x % 64 == 0;
            var half = x % 32 == 0;

            var line = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = major ? 5 : half ? 9 : 12,
                Y2 = 16,
                Stroke = tickBrush,
                StrokeThickness = 1,
            };

            TopRuler.Children.Add(line);

            if (major)
            {
                var label = new TextBlock
                {
                    Text = x.ToString(),
                    FontSize = 7.5,
                    Foreground = labelBrush,
                    IsHitTestVisible = false,
                };

                Canvas.SetLeft(label, x + 2);
                Canvas.SetTop(label, -1);
                TopRuler.Children.Add(label);
            }
        }
    }

    // ----------------------------------------------------------- rolagem/zoom

    private void OnFitToWidth(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.FitCanvasToWidth(CanvasScroll.ActualWidth);
        RedrawRuler();
    }

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_viewModel is null || Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        var before = e.GetPosition(CanvasScroll);
        var extentX = Math.Max(1, CanvasScroll.ExtentWidth);
        var extentY = Math.Max(1, CanvasScroll.ExtentHeight);
        var relativeX = (CanvasScroll.HorizontalOffset + before.X) / extentX;
        var relativeY = (CanvasScroll.VerticalOffset + before.Y) / extentY;

        var step = e.Delta > 0 ? 0.1 : -0.1;
        _viewModel.Zoom = Math.Clamp(_viewModel.Zoom + step, 0.4, 3.0);

        CanvasScroll.UpdateLayout();
        CanvasScroll.ScrollToHorizontalOffset(relativeX * CanvasScroll.ExtentWidth - before.X);
        CanvasScroll.ScrollToVerticalOffset(relativeY * CanvasScroll.ExtentHeight - before.Y);

        e.Handled = true;
    }

    // ---------------------------------------------------------------- paleta

    private void OnPaletteItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PaletteItem item })
        {
            _paletteDragId = item.Id;
            _paletteDragStart = e.GetPosition(this);
        }
    }

    private void OnPaletteItemMouseMove(object sender, MouseEventArgs e)
    {
        if (_paletteDragId is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = e.GetPosition(this) - _paletteDragStart;
        if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6)
        {
            return;
        }

        var id = _paletteDragId;
        _paletteDragId = null;

        var data = new DataObject(PaletteDragFormat, id);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
        _lastDropAt = DateTime.Now;
    }

    private void OnPaperDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(PaletteDragFormat) || e.Data.GetDataPresent(LayerDragFormat)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnPaperDrop(object sender, DragEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (e.Data.GetData(PaletteDragFormat) is string toolId)
        {
            var point = e.GetPosition(Paper);
            _viewModel.InsertTool(toolId, (int)Math.Round(point.X), (int)Math.Round(point.Y));
            e.Handled = true;
        }
    }

    // --------------------------------------------------------------- canvas

    private void OnPaperMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is null || _interaction is null)
        {
            return;
        }

        // Clique na área vazia do papel: começa seleção por área.
        _viewModel.SelectedElement = null;
        _interaction.BeginMarquee(e.GetPosition(Paper));
        Paper.CaptureMouse();
        _captureTarget = Paper;
        Focus();
        e.Handled = true;
    }

    private void OnPaperMouseMove(object sender, MouseEventArgs e)
    {
        if (_interaction is null || !_interaction.IsActive)
        {
            return;
        }

        _interaction.Update(e.GetPosition(Paper), e.LeftButton == MouseButtonState.Pressed, Keyboard.Modifiers);

        if (_interaction.Mode == InteractionMode.Marquee)
        {
            var rect = _interaction.Marquee;
            MarqueeRectangle.Visibility = Visibility.Visible;
            MarqueeRectangle.Width = rect.Width;
            MarqueeRectangle.Height = rect.Height;
            MarqueeRectangle.Margin = new Thickness(rect.X, rect.Y, 0, 0);
            MarqueeRectangle.HorizontalAlignment = HorizontalAlignment.Left;
            MarqueeRectangle.VerticalAlignment = VerticalAlignment.Top;
        }

        e.Handled = true;
    }

    private void OnPaperMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_interaction is null || !_interaction.IsActive)
        {
            return;
        }

        _interaction.End();
        MarqueeRectangle.Visibility = Visibility.Collapsed;
        ReleaseCapture();
    }
    private void OnElementMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is null || _interaction is null
            || sender is not FrameworkElement { DataContext: LayoutElement element })
        {
            return;
        }

        var additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var toggle = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (e.ClickCount == 2)
        {
            OnElementDoubleClick(sender, e);
            return;
        }

        _viewModel.SelectElement(element, additive, toggle);

        if (!element.Locked)
        {
            _interaction.BeginMove(element, e.GetPosition(Paper));
            Capture(sender as FrameworkElement);
        }

        Focus();
        e.Handled = true;
    }

    private void OnElementMouseMove(object sender, MouseEventArgs e)
    {
        if (_interaction is null || _viewModel is null || !_interaction.IsActive)
        {
            return;
        }

        _interaction.Update(e.GetPosition(Paper), e.LeftButton == MouseButtonState.Pressed, Keyboard.Modifiers);

        if (_interaction.Mode == InteractionMode.Marquee)
        {
            var rect = _interaction.Marquee;
            MarqueeRectangle.Visibility = Visibility.Visible;
            MarqueeRectangle.Width = rect.Width;
            MarqueeRectangle.Height = rect.Height;
            MarqueeRectangle.Margin = new Thickness(rect.X, rect.Y, 0, 0);
            MarqueeRectangle.HorizontalAlignment = HorizontalAlignment.Left;
            MarqueeRectangle.VerticalAlignment = VerticalAlignment.Top;
        }

        e.Handled = true;
    }

    private void OnHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is null || _interaction is null
            || sender is not FrameworkElement { DataContext: LayoutElement element } handle)
        {
            return;
        }

        var parsed = Enum.TryParse<ResizeHandle>(handle.Tag as string, out var resizeHandle)
            ? resizeHandle
            : ResizeHandle.SE;

        _viewModel.SelectElement(element);
        _interaction.BeginResize(element, parsed, e.GetPosition(Paper));
        Capture(handle);
        Focus();
        e.Handled = true;
    }

    private void OnHandleMouseMove(object sender, MouseEventArgs e)
    {
        if (_interaction is null || _viewModel is null || !_interaction.IsActive)
        {
            return;
        }

        _interaction.Update(e.GetPosition(Paper), e.LeftButton == MouseButtonState.Pressed, Keyboard.Modifiers);
        e.Handled = true;
    }

    private void OnElementMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_interaction is null)
        {
            return;
        }

        if (_interaction.IsActive)
        {
            _interaction.End();
            MarqueeRectangle.Visibility = Visibility.Collapsed;
            ReleaseCapture();
            e.Handled = true;
        }
    }

    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_interaction is null || !_interaction.IsActive)
        {
            return;
        }

        _interaction.End();
        MarqueeRectangle.Visibility = Visibility.Collapsed;
        ReleaseCapture();
    }

    private void Capture(FrameworkElement? target)
    {
        ReleaseCapture();
        if (target is null)
        {
            return;
        }

        if (target.CaptureMouse())
        {
            _captureTarget = target;
        }
    }

    private void ReleaseCapture()
    {
        var target = _captureTarget;
        _captureTarget = null;

        if (target is null)
        {
            return;
        }

        if (target.IsMouseCaptured)
        {
            target.ReleaseMouseCapture();
        }
    }

    // --------------------------------------------------------- menu/edição

    private void OnElementRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is null || sender is not FrameworkElement { DataContext: LayoutElement element })
        {
            return;
        }

        if (!_viewModel.SelectedElements.Contains(element))
        {
            _viewModel.SelectElement(element);
        }

        var menu = BuildElementMenu(element);
        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private ContextMenu BuildElementMenu(LayoutElement element)
    {
        var viewModel = _viewModel!;
        var menu = new ContextMenu();

        if (element.Kind is LayoutElementKind.Texto or LayoutElementKind.Campo)
        {
            var editText = element.Kind == LayoutElementKind.Campo ? "Editar campo..." : "Editar texto";
            menu.Items.Add(MenuItemFor(editText, () =>
            {
                if (element.Kind == LayoutElementKind.Campo)
                {
                    OpenFieldEditor(viewModel.SelectedField);
                }
                else
                {
                    viewModel.SelectedElement = element;
                    viewModel.BeginInlineEdit();
                    UpdateInlineEditor();
                }
            }));
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(MenuItemFor("Recortar", () => viewModel.CutSelection(), viewModel.HasSelection));
        menu.Items.Add(MenuItemFor("Copiar", () => viewModel.CopySelection(), viewModel.HasSelection));
        menu.Items.Add(MenuItemFor("Colar", () => viewModel.Paste(), viewModel.CanPaste));
        menu.Items.Add(MenuItemFor("Duplicar", () => viewModel.DuplicateSelection(), viewModel.HasSelection));
        menu.Items.Add(MenuItemFor("Excluir", () => viewModel.DeleteSelection(), viewModel.HasSelection));
        menu.Items.Add(new Separator());

        menu.Items.Add(MenuItemFor("Copiar estilo", () => viewModel.CopyStyle(), viewModel.HasSelection));
        menu.Items.Add(MenuItemFor("Aplicar estilo", () => viewModel.ApplyStyle(), viewModel.HasCopiedStyle && viewModel.HasSelection));
        menu.Items.Add(new Separator());

        menu.Items.Add(MenuItemFor(
            element.Locked ? "Desbloquear" : "Bloquear",
            () => viewModel.ToggleLockElementCommand.Execute(element)));
        menu.Items.Add(MenuItemFor(
            element.Visible ? "Ocultar" : "Mostrar",
            () => viewModel.ToggleVisibilityCommand.Execute(element)));
        menu.Items.Add(new Separator());

        menu.Items.Add(MenuItemFor("Trazer para frente", () => viewModel.BringForwardCommand.Execute(null), viewModel.HasSelection));
        menu.Items.Add(MenuItemFor("Enviar para trás", () => viewModel.SendBackwardCommand.Execute(null), viewModel.HasSelection));
        menu.Items.Add(MenuItemFor("Agrupar", () => viewModel.GroupSelectionPublic(), viewModel.Selection.Count > 1));
        menu.Items.Add(MenuItemFor("Desagrupar", () => viewModel.UngroupSelectionPublic(), viewModel.HasSelection));
        menu.Items.Add(new Separator());

        var align = new MenuItem { Header = "Alinhar" };
        align.Items.Add(MenuItemFor("Esquerda", () => viewModel.AlignLeft(), viewModel.HasSelection));
        align.Items.Add(MenuItemFor("Centralizar", () => viewModel.AlignCenterHorizontal(), viewModel.HasSelection));
        align.Items.Add(MenuItemFor("Direita", () => viewModel.AlignRight(), viewModel.HasSelection));
        align.Items.Add(MenuItemFor("Ao topo", () => viewModel.AlignTop(), viewModel.HasSelection));
        align.Items.Add(MenuItemFor("Ao meio", () => viewModel.AlignCenterVertical(), viewModel.HasSelection));
        align.Items.Add(MenuItemFor("A base", () => viewModel.AlignBottom(), viewModel.HasSelection));
        menu.Items.Add(align);

        return menu;
    }

    private static MenuItem MenuItemFor(string header, Action action, bool enabled = true) =>
        new()
        {
            Header = header,
            IsEnabled = enabled,
            Command = new RelayCommand(action, () => enabled),
        };

    private void OnElementDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is null || sender is not FrameworkElement { DataContext: LayoutElement element })
        {
            return;
        }

        _viewModel.SelectElement(element);

        if (element.Kind == LayoutElementKind.Campo)
        {
            OpenFieldEditor(_viewModel.SelectedField);
        }
        else if (element.Kind == LayoutElementKind.Texto)
        {
            _viewModel.BeginInlineEdit();
            UpdateInlineEditor();
        }

        e.Handled = true;
    }

    private void UpdateInlineEditor()
    {
        if (_viewModel is null || InlineEditorLayer is null || InlineTextBox is null)
        {
            return;
        }

        InlineEditorLayer.Visibility = _viewModel.IsInlineEditing ? Visibility.Visible : Visibility.Collapsed;

        if (!_viewModel.IsInlineEditing)
        {
            return;
        }

        UpdateInlineEditorBounds();
        InlineTextBox.Focus();
        InlineTextBox.SelectAll();
    }

    private void UpdateInlineEditorBounds()
    {
        if (_viewModel is null || InlineTextBox is null || !_viewModel.IsInlineEditing)
        {
            return;
        }

        var element = _viewModel.SelectedElement;
        if (element is null)
        {
            return;
        }

        Canvas.SetLeft(InlineTextBox, element.X);
        Canvas.SetTop(InlineTextBox, element.Y);
        InlineTextBox.Width = Math.Max(40, element.Width);
        InlineTextBox.Height = Math.Max(16, element.Height + 6);
    }

    private void OnInlineTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (_viewModel is { IsInlineEditing: true })
        {
            _viewModel.CommitInlineEdit();
            UpdateInlineEditor();
        }
    }

    private void OnInlineTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            _viewModel.CancelInlineEdit();
            UpdateInlineEditor();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            _viewModel.CommitInlineEdit();
            UpdateInlineEditor();
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------- camadas

    private void OnLayersSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null || _syncingLayers || sender is not ListBox listBox)
        {
            return;
        }

        var selected = listBox.SelectedItems.OfType<LayoutElement>().ToList();
        if (selected.Count == 0)
        {
            return;
        }

        _syncingLayers = true;
        try
        {
            if (selected.Count == 1)
            {
                _viewModel.SelectedElement = selected[0];
            }
            else
            {
                _viewModel.SelectMany(selected);
            }
        }
        finally
        {
            _syncingLayers = false;
        }
    }

    private void OnLayerMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item is null)
        {
            return;
        }

        _layerDragStart = e.GetPosition(listBox);
        _layerDragIndex = listBox.Items.IndexOf(item.DataContext);
    }

    private void OnLayerMouseMove(object sender, MouseEventArgs e)
    {
        if (_layerDragIndex < 0 || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = e.GetPosition((IInputElement)sender) - _layerDragStart;
        if (Math.Abs(delta.Y) < 10)
        {
            return;
        }

        var index = _layerDragIndex;
        _layerDragIndex = -1;

        var data = new DataObject(LayerDragFormat, index);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
    }

    private void OnLayerDrop(object sender, DragEventArgs e)
    {
        if (_viewModel is null || sender is not ListBox listBox || e.Data.GetData(LayerDragFormat) is not int sourceIndex)
        {
            return;
        }

        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item is null)
        {
            return;
        }

        var targetIndex = listBox.Items.IndexOf(item.DataContext);
        if (targetIndex < 0 || targetIndex == sourceIndex)
        {
            return;
        }

        _viewModel.MoveLayer(sourceIndex, targetIndex);
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    // ---------------------------------------------------------------- menu

    private void OnAlignMenuClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var menu = new ContextMenu();

        menu.Items.Add(MenuItemFor("Alinhar à esquerda", () => _viewModel.AlignLeft()));
        menu.Items.Add(MenuItemFor("Centralizar horizontalmente", () => _viewModel.AlignCenterHorizontal()));
        menu.Items.Add(MenuItemFor("Alinhar à direita", () => _viewModel.AlignRight()));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItemFor("Alinhar ao topo", () => _viewModel.AlignTop()));
        menu.Items.Add(MenuItemFor("Centralizar verticalmente", () => _viewModel.AlignCenterVertical()));
        menu.Items.Add(MenuItemFor("Alinhar à base", () => _viewModel.AlignBottom()));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItemFor("Distribuir horizontalmente", () => _viewModel.DistributeEvenly(vertical: false)));
        menu.Items.Add(MenuItemFor("Distribuir verticalmente", () => _viewModel.DistributeEvenly(vertical: true)));

        menu.PlacementTarget = AlignButton;
        menu.IsOpen = true;
    }

    private void OnClosePreview(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.IsPrintPreview = false;
        }
    }

    // ---------------------------------------------------------------- teclado

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_viewModel is { IsPrintPreview: true })
            {
                _viewModel.IsPrintPreview = false;
                e.Handled = true;
                return;
            }

            if (_viewModel is { IsInlineEditing: true })
            {
                _viewModel.CancelInlineEdit();
                UpdateInlineEditor();
                e.Handled = true;
                return;
            }

            if (_interaction is { IsActive: true })
            {
                _interaction.Cancel();
                ReleaseCapture();
                MarqueeRectangle.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        // Não roubar teclas enquanto o usuário digita em um campo.
        if (Keyboard.FocusedElement is TextBox or ComboBox or System.Windows.Controls.DatePicker)
        {
            return;
        }

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        switch (e.Key)
        {
            case Key.Delete:
            case Key.Back:
                viewModel.DeleteCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Left:
                viewModel.MoveSelection(shift ? -8 : -1, 0);
                e.Handled = true;
                break;

            case Key.Right:
                viewModel.MoveSelection(shift ? 8 : 1, 0);
                e.Handled = true;
                break;

            case Key.Up:
                viewModel.MoveSelection(0, shift ? -8 : -1);
                e.Handled = true;
                break;

            case Key.Down:
                viewModel.MoveSelection(0, shift ? 8 : 1);
                e.Handled = true;
                break;

            case Key.Z when ctrl:
                viewModel.UndoCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Y when ctrl:
                viewModel.RedoCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.C when ctrl:
                viewModel.CopyCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.V when ctrl:
                viewModel.PasteCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.X when ctrl:
                viewModel.CutCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.D when ctrl:
                viewModel.DuplicateCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.A when ctrl:
                viewModel.SelectAll();
                e.Handled = true;
                break;

            case Key.S when ctrl:
                viewModel.SaveCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.G when ctrl:
                viewModel.GroupCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.B when ctrl:
                ToggleSelectedFlag(viewModel, bold: true);
                e.Handled = true;
                break;

            case Key.I when ctrl:
                ToggleSelectedFlag(viewModel, bold: false);
                e.Handled = true;
                break;

            case Key.F when ctrl:
                OpenFindReplace();
                e.Handled = true;
                break;

            case Key.P when ctrl:
                viewModel.IsPrintPreview = !viewModel.IsPrintPreview;
                e.Handled = true;
                break;

            case Key.D0 when ctrl:
                OnFitToWidth(this, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    private static void ToggleSelectedFlag(VisualEditorViewModel viewModel, bool bold)
    {
        if (viewModel.SelectedElement is not { } element)
        {
            return;
        }

        if (bold)
        {
            element.Bold = !element.Bold;
        }
        else
        {
            element.Italic = !element.Italic;
        }
    }
}

