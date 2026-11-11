using System.Windows;
using System.Windows.Input;
using MiniPrint.Core.Templates;
using MiniPrint.Desktop.ViewModels;

namespace MiniPrint.Desktop.Editing;

/// <summary>Modo de interação em andamento no canvas.</summary>
public enum InteractionMode
{
    None,
    Moving,
    Resizing,
    Marquee,
}

/// <summary>Alça de redimensionamento acionada.</summary>
public enum ResizeHandle
{
    None,
    NW,
    N,
    NE,
    W,
    E,
    SW,
    S,
    SE,
}

/// <summary>
/// Máquina de estados da interação com o canvas (mover, redimensionar e
/// selecionar por área). Fica fora do code-behind para a view só traduzir
/// eventos de mouse em chamadas.
/// </summary>
public sealed class CanvasInteractionController
{
    private readonly VisualEditorViewModel _viewModel;

    private InteractionMode _mode = InteractionMode.None;
    private LayoutElement? _element;
    private ResizeHandle _handle = ResizeHandle.None;
    private Point _start;
    private Point _last;
    private int _originX;
    private int _originY;
    private int _originWidth;
    private int _originHeight;
    private bool _changed;
    private Rect _marquee;

    public CanvasInteractionController(VisualEditorViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public InteractionMode Mode => _mode;

    public bool IsActive => _mode != InteractionMode.None;

    public Rect Marquee => _marquee;

    public string SizeHint { get; private set; } = string.Empty;

    public LayoutElement? Element => _element;

    public void BeginMove(LayoutElement element, Point paperPoint)
    {
        _mode = InteractionMode.Moving;
        _element = element;
        _start = paperPoint;
        _last = paperPoint;
        _originX = element.X;
        _originY = element.Y;
        _changed = false;
        SizeHint = $"{element.Width} × {element.Height} pt";
    }

    public void BeginResize(LayoutElement element, ResizeHandle handle, Point paperPoint)
    {
        _mode = InteractionMode.Resizing;
        _element = element;
        _handle = handle;
        _start = paperPoint;
        _last = paperPoint;
        _originX = element.X;
        _originY = element.Y;
        _originWidth = element.Width;
        _originHeight = element.Height;
        _changed = false;
        SizeHint = $"{element.Width} × {element.Height} pt";
    }

    public void BeginMarquee(Point paperPoint)
    {
        _mode = InteractionMode.Marquee;
        _element = null;
        _start = paperPoint;
        _last = paperPoint;
        _marquee = new Rect(paperPoint, paperPoint);
        _changed = false;
    }

    public bool Update(Point paperPoint, bool buttonPressed, ModifierKeys modifiers)
    {
        if (_mode == InteractionMode.None)
        {
            return false;
        }

        _last = paperPoint;

        if (_mode == InteractionMode.Marquee)
        {
            _marquee = new Rect(
                Math.Min(_start.X, paperPoint.X),
                Math.Min(_start.Y, paperPoint.Y),
                Math.Abs(paperPoint.X - _start.X),
                Math.Abs(paperPoint.Y - _start.Y));
            return true;
        }

        if (!buttonPressed)
        {
            End();
            return false;
        }

        if (_element is null)
        {
            return false;
        }

        if (_mode == InteractionMode.Moving)
        {
            UpdateMove(paperPoint, modifiers);
        }
        else if (_mode == InteractionMode.Resizing)
        {
            UpdateResize(paperPoint, modifiers);
        }

        return true;
    }

    private void UpdateMove(Point paperPoint, ModifierKeys modifiers)
    {
        var element = _element!;
        var dx = (int)Math.Round(paperPoint.X - _start.X);
        var dy = (int)Math.Round(paperPoint.Y - _start.Y);

        // Shift trava o movimento em um eixo só.
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (Math.Abs(dx) > Math.Abs(dy))
            {
                dy = 0;
            }
            else
            {
                dx = 0;
            }
        }

        var proposedX = Math.Max(0, _originX + dx);
        var proposedY = Math.Max(0, _originY + dy);

        if (!_changed && Math.Abs(dx) < 1 && Math.Abs(dy) < 1)
        {
            return;
        }

        if (!_changed)
        {
            _changed = true;
            _viewModel.BeginInteraction();
        }

        var snapped = SnapPosition(element, proposedX, proposedY);
        var deltaX = snapped.X - _originX;
        var deltaY = snapped.Y - _originY;

        // Aplica deslocamento relativo ao estado atual para mover o grupo inteiro.
        var appliedX = _originX + deltaX;
        var appliedY = _originY + deltaY;
        _viewModel.MoveSelectionTo(appliedX, appliedY, element);
        SizeHint = $"X {element.X} · Y {element.Y}";
    }

    private void UpdateResize(Point paperPoint, ModifierKeys modifiers)
    {
        var element = _element!;
        var dx = (int)Math.Round(paperPoint.X - _start.X);
        var dy = (int)Math.Round(paperPoint.Y - _start.Y);

        if (!_changed && Math.Abs(dx) < 1 && Math.Abs(dy) < 1)
        {
            return;
        }

        if (!_changed)
        {
            _changed = true;
            _viewModel.BeginInteraction();
        }

        var x = _originX;
        var y = _originY;
        var width = _originWidth;
        var height = _originHeight;

        if (_handle is ResizeHandle.E or ResizeHandle.NE or ResizeHandle.SE)
        {
            width = _originWidth + dx;
        }

        if (_handle is ResizeHandle.W or ResizeHandle.NW or ResizeHandle.SW)
        {
            width = _originWidth - dx;
            x = _originX + dx;
        }

        if (_handle is ResizeHandle.S or ResizeHandle.SE or ResizeHandle.SW)
        {
            height = _originHeight + dy;
        }

        if (_handle is ResizeHandle.N or ResizeHandle.NE or ResizeHandle.NW)
        {
            height = _originHeight - dy;
            y = _originY + dy;
        }

        // Shift preserva a proporção (a partir do tamanho original).
        if (modifiers.HasFlag(ModifierKeys.Shift) && _originHeight > 0)
        {
            var ratio = (double)_originWidth / _originHeight;
            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                height = (int)Math.Round(width / ratio);
                if (_handle is ResizeHandle.N or ResizeHandle.NE or ResizeHandle.NW)
                {
                    y = _originY + (_originHeight - height);
                }
            }
            else
            {
                width = (int)Math.Round(height * ratio);
                if (_handle is ResizeHandle.W or ResizeHandle.NW or ResizeHandle.SW)
                {
                    x = _originX + (_originWidth - width);
                }
            }
        }

        var edges = HandleEdges(_handle);
        var snapped = SnapResize(new SnapRect(x, y, width, height), edges);

        _viewModel.ResizeElement(_element!, snapped.X, snapped.Y, snapped.Width, snapped.Height);
        SizeHint = $"{element.Width} × {element.Height} pt";
    }

    private static string HandleEdges(ResizeHandle handle) => handle switch
    {
        ResizeHandle.N => "N",
        ResizeHandle.S => "S",
        ResizeHandle.E => "E",
        ResizeHandle.W => "W",
        ResizeHandle.NE => "NE",
        ResizeHandle.NW => "NW",
        ResizeHandle.SE => "SE",
        ResizeHandle.SW => "SW",
        _ => string.Empty,
    };

    private (int X, int Y, int Width, int Height) SnapResize(SnapRect rect, string edges)
    {
        var layout = _viewModel.Layout;
        var options = new SnapOptions(
            _viewModel.SnapToGrid,
            _viewModel.GridSize,
            _viewModel.SnapToObjects,
            Margins: true,
            PageCenter: false,
            _viewModel.SnapTolerance);

        var others = _viewModel.Elements
            .Where(e => e.Visible && !ReferenceEquals(e, _element))
            .Select(e => new SnapRect(e.X, e.Y, e.Width, e.Height))
            .ToList();

        var outcome = EditorSnapEngine.SnapResize(
            rect, edges, others, layout.Width, layout.EffectiveHeight,
            layout.MarginLeft, layout.Width - layout.MarginRight, layout.MarginTop, options);

        _viewModel.SetGuides(outcome.Guides);
        return (outcome.X, outcome.Y, outcome.Width, outcome.Height);
    }

    private (int X, int Y) SnapPosition(LayoutElement element, int x, int y)
    {
        var layout = _viewModel.Layout;
        var options = new SnapOptions(
            _viewModel.SnapToGrid,
            _viewModel.GridSize,
            _viewModel.SnapToObjects,
            Margins: true,
            PageCenter: true,
            _viewModel.SnapTolerance);

        var others = _viewModel.Elements
            .Where(e => e.Visible && !_viewModel.SelectedElements.Contains(e))
            .Select(e => new SnapRect(e.X, e.Y, e.Width, e.Height))
            .ToList();

        var outcome = EditorSnapEngine.SnapMove(
            new SnapRect(x, y, element.Width, element.Height),
            others,
            layout.Width,
            layout.EffectiveHeight,
            layout.MarginLeft,
            layout.Width - layout.MarginRight,
            layout.MarginTop,
            options);

        _viewModel.SetGuides(outcome.Guides);
        return (outcome.X, outcome.Y);
    }

    public void End()
    {
        if (_mode == InteractionMode.Marquee)
        {
            var rect = _marquee;
            _mode = InteractionMode.None;
            if (rect.Width >= 3 || rect.Height >= 3)
            {
                _viewModel.SelectInRect(rect.X, rect.Y, rect.Width, rect.Height);
            }
        }
        else
        {
            _mode = InteractionMode.None;
            _viewModel.SetGuides(null);
            if (_changed)
            {
                _viewModel.EndInteraction();
            }
        }

        _element = null;
        _handle = ResizeHandle.None;
        _changed = false;
        SizeHint = string.Empty;
    }

    public void Cancel()
    {
        if (_mode is InteractionMode.Moving or InteractionMode.Resizing && _element is { } element)
        {
            _viewModel.ResizeElement(element, _originX, _originY, _originWidth, _originHeight);
        }

        _mode = InteractionMode.None;
        _element = null;
        _handle = ResizeHandle.None;
        _changed = false;
        SizeHint = string.Empty;
        _viewModel.SetGuides(null);
        _viewModel.EndInteraction();
    }
}
