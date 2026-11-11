namespace MiniPrint.Desktop.Editing;

/// <summary>Origem de uma linha-guia mostrada durante o arraste.</summary>
public enum SnapSource
{
    Grid,
    Margin,
    PageCenter,
    ObjectEdge,
    ObjectCenter,
}

/// <summary>Linha-guia desenhada sobre o papel (coordenadas em pontos).</summary>
public readonly record struct SnapGuide(double X1, double Y1, double X2, double Y2, SnapSource Source)
{
    public bool IsVertical => Math.Abs(X1 - X2) < 0.01;
}

/// <summary>Retângulo de trabalho, em pontos de impressão.</summary>
public readonly record struct SnapRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public int CenterX => X + Width / 2;

    public int CenterY => Y + Height / 2;
}

/// <summary>Configuração do encaixe.</summary>
public sealed record SnapOptions(
    bool Grid = true,
    int GridSize = 8,
    bool Objects = true,
    bool Margins = true,
    bool PageCenter = true,
    int Tolerance = 4);

/// <summary>Resultado do encaixe: retângulo ajustado e guias a exibir.</summary>
public readonly record struct SnapOutcome(int X, int Y, int Width, int Height, IReadOnlyList<SnapGuide> Guides);

/// <summary>
/// Encaixe inteligente do editor visual: grade, margens, centro do papel e
/// bordas/centros dos outros elementos.
///
/// É lógica pura (sem WPF), para poder ser testada e reutilizada tanto no
/// arraste quanto no redimensionamento. As guias devolvidas são apenas visuais
/// e nunca entram no documento.
/// </summary>
public static class EditorSnapEngine
{
    private const int MinTolerance = 1;

    /// <summary>Encaixa a posição de um elemento que está sendo arrastado.</summary>
    public static SnapOutcome SnapMove(
        SnapRect moving,
        IReadOnlyList<SnapRect> others,
        int pageWidth,
        int pageHeight,
        int contentLeft,
        int contentRight,
        int contentTop,
        SnapOptions options)
    {
        var guides = new List<SnapGuide>();

        var xCandidates = BuildXCandidates(others, contentLeft, contentRight, pageWidth, moving, options);
        var yCandidates = BuildYCandidates(others, contentTop, pageHeight, moving, options);

        var (dx, xGuide) = FindSnapX(moving.X, moving.CenterX, moving.Right, xCandidates, options.Tolerance);
        var (dy, yGuide) = FindSnapY(moving.Y, moving.CenterY, moving.Bottom, yCandidates, options.Tolerance);

        if (xGuide is { } guide)
        {
            guides.Add(guide);
        }

        if (yGuide is { } guideY)
        {
            guides.Add(guideY);
        }

        return new SnapOutcome(moving.X + dx, moving.Y + dy, moving.Width, moving.Height, guides);
    }

    /// <summary>
    /// Encaixa apenas as bordas que o usuário está movendo ao redimensionar.
    /// <paramref name="edges"/> indica quais lados mudam (ex.: "SE" muda direita e base).
    /// </summary>
    public static SnapOutcome SnapResize(
        SnapRect moving,
        string edges,
        IReadOnlyList<SnapRect> others,
        int pageWidth,
        int pageHeight,
        int contentLeft,
        int contentRight,
        int contentTop,
        SnapOptions options)
    {
        var guides = new List<SnapGuide>();
        var x = moving.X;
        var y = moving.Y;
        var width = moving.Width;
        var height = moving.Height;

        var xCandidates = BuildXCandidates(others, contentLeft, contentRight, pageWidth, moving, options);
        var yCandidates = BuildYCandidates(others, contentTop, pageHeight, moving, options);
        var tolerance = Math.Max(MinTolerance, options.Tolerance);

        foreach (var (position, source, from, to) in xCandidates)
        {
            if (edges.Contains('E') && Math.Abs(moving.Right - position) <= tolerance)
            {
                width = Math.Max(4, position - x);
                guides.Add(new SnapGuide(position, from, position, to, source));
            }
            else if (edges.Contains('W') && Math.Abs(x - position) <= tolerance)
            {
                var newX = Math.Min(position, moving.Right - 4);
                width = moving.Right - newX;
                x = newX;
                guides.Add(new SnapGuide(position, from, position, to, source));
            }
        }

        foreach (var (position, source, from, to) in yCandidates)
        {
            if (edges.Contains('S') && Math.Abs(moving.Bottom - position) <= tolerance)
            {
                height = Math.Max(2, position - y);
                guides.Add(new SnapGuide(from, position, to, position, source));
            }
            else if (edges.Contains('N') && Math.Abs(y - position) <= tolerance)
            {
                var newY = Math.Min(position, moving.Bottom - 2);
                height = moving.Bottom - newY;
                y = newY;
                guides.Add(new SnapGuide(from, position, to, position, source));
            }
        }

        return new SnapOutcome(x, y, width, height, guides);
    }

    private readonly record struct Candidate(int Position, SnapSource Source, double From, double To);

    private static List<Candidate> BuildXCandidates(
        IReadOnlyList<SnapRect> others,
        int contentLeft,
        int contentRight,
        int pageWidth,
        SnapRect moving,
        SnapOptions options)
    {
        var candidates = new List<Candidate>();
        var top = moving.Y;
        var bottom = moving.Bottom;

        if (options.Objects)
        {
            foreach (var other in others)
            {
                var from = Math.Min(top, other.Y) - 4;
                var to = Math.Max(bottom, other.Bottom) + 4;
                candidates.Add(new Candidate(other.X, SnapSource.ObjectEdge, from, to));
                candidates.Add(new Candidate(other.Right, SnapSource.ObjectEdge, from, to));
                candidates.Add(new Candidate(other.CenterX, SnapSource.ObjectCenter, from, to));
            }
        }

        if (options.Margins)
        {
            candidates.Add(new Candidate(contentLeft, SnapSource.Margin, 0, 100000));
            candidates.Add(new Candidate(contentRight, SnapSource.Margin, 0, 100000));
        }

        if (options.PageCenter)
        {
            candidates.Add(new Candidate(pageWidth / 2, SnapSource.PageCenter, 0, 100000));
        }

        if (options.Grid && options.GridSize >= 2)
        {
            candidates.Add(new Candidate(RoundToGrid(moving.X, options.GridSize), SnapSource.Grid, top, bottom));
            candidates.Add(new Candidate(RoundToGrid(moving.CenterX, options.GridSize), SnapSource.Grid, top, bottom));
            candidates.Add(new Candidate(RoundToGrid(moving.Right, options.GridSize), SnapSource.Grid, top, bottom));
        }

        return candidates;
    }

    private static List<Candidate> BuildYCandidates(
        IReadOnlyList<SnapRect> others,
        int contentTop,
        int pageHeight,
        SnapRect moving,
        SnapOptions options)
    {
        var candidates = new List<Candidate>();
        var left = moving.X;
        var right = moving.Right;

        if (options.Objects)
        {
            foreach (var other in others)
            {
                var from = Math.Min(left, other.X) - 4;
                var to = Math.Max(right, other.Right) + 4;
                candidates.Add(new Candidate(other.Y, SnapSource.ObjectEdge, from, to));
                candidates.Add(new Candidate(other.Bottom, SnapSource.ObjectEdge, from, to));
                candidates.Add(new Candidate(other.CenterY, SnapSource.ObjectCenter, from, to));
            }
        }

        if (options.Margins)
        {
            candidates.Add(new Candidate(contentTop, SnapSource.Margin, 0, 100000));
        }

        if (options.Grid && options.GridSize >= 2)
        {
            candidates.Add(new Candidate(RoundToGrid(moving.Y, options.GridSize), SnapSource.Grid, left, right));
            candidates.Add(new Candidate(RoundToGrid(moving.CenterY, options.GridSize), SnapSource.Grid, left, right));
            candidates.Add(new Candidate(RoundToGrid(moving.Bottom, options.GridSize), SnapSource.Grid, left, right));
        }

        return candidates;
    }

    private static (int Delta, SnapGuide? Guide) FindSnapX(
        int left,
        int center,
        int right,
        List<Candidate> candidates,
        int tolerance)
    {
        tolerance = Math.Max(MinTolerance, tolerance);
        var best = int.MaxValue;
        var bestDelta = 0;
        Candidate bestCandidate = default;

        foreach (var candidate in candidates)
        {
            foreach (var anchor in new[] { left, center, right })
            {
                var delta = candidate.Position - anchor;
                var distance = Math.Abs(delta);
                if (distance > tolerance || distance >= best)
                {
                    continue;
                }

                best = distance;
                bestDelta = delta;
                bestCandidate = candidate;
            }
        }

        if (best == int.MaxValue)
        {
            return (0, null);
        }

        var guide = new SnapGuide(
            bestCandidate.Position,
            bestCandidate.From,
            bestCandidate.Position,
            bestCandidate.To,
            bestCandidate.Source);

        return (bestDelta, guide);
    }

    private static (int Delta, SnapGuide? Guide) FindSnapY(
        int top,
        int center,
        int bottom,
        List<Candidate> candidates,
        int tolerance)
    {
        tolerance = Math.Max(MinTolerance, tolerance);
        var best = int.MaxValue;
        var bestDelta = 0;
        Candidate bestCandidate = default;

        foreach (var candidate in candidates)
        {
            foreach (var anchor in new[] { top, center, bottom })
            {
                var delta = candidate.Position - anchor;
                var distance = Math.Abs(delta);
                if (distance > tolerance || distance >= best)
                {
                    continue;
                }

                best = distance;
                bestDelta = delta;
                bestCandidate = candidate;
            }
        }

        if (best == int.MaxValue)
        {
            return (0, null);
        }

        var guide = new SnapGuide(
            bestCandidate.From,
            bestCandidate.Position,
            bestCandidate.To,
            bestCandidate.Position,
            bestCandidate.Source);

        return (bestDelta, guide);
    }

    private static int RoundToGrid(int value, int gridSize) =>
        (int)Math.Round((double)value / gridSize) * gridSize;
}
