using Glazor.Misc;
using Microsoft.Extensions.Logging;

namespace Glazor.Grds;

/// <summary>
/// Settles the page size a grid's vertical resizer computes when the page around the grid has two stable layouts, each giving
/// the grid the height that asks for the other's page size: a scrollbar that a page of more rows brings, narrowing the page so
/// that the text above the grid wraps and takes height from it. The size goes back to what it was twice in a row, each time
/// within a short time, at the heights it had: the smaller one is kept while the height keeps alternating between those two.
/// </summary>
internal sealed class PageSizeSettler
{
    private static readonly ILogger<PageSizeSettler> logger = LoggerSupport.Logger<PageSizeSettler>();

    internal const long FlipWindowMs = 2000;   // a flip follows the render before it; a user resizing back and forth is slower

    private (double Height, int Size, long At)? _previous;   // the size decided before the current one
    private (double Height, int Size, long At)? _current;
    private (double High, double Low, int Size)? _held;
    private int _flips;   // in a row: a user going back to a size flips once, a page that cannot settle keeps flipping

    /// <summary>The page size to use for <paramref name="size"/>, computed at <paramref name="height"/>, at <paramref name="nowMs"/>.</summary>
    public int Settle(double height, int size, long nowMs)
    {
        if (_held is (double high, double low, int held))
        {
            if (Same(height, high) || Same(height, low))
                return held;
            _held = null;   // resized for real
        }
        if (_current is { } current && size == current.Size)
            return size;
        // Back to the size it had at the height it had, through another height: rows changing height in one box are followed
        if (_current is { } last && _previous is { } before && size == before.Size && Same(height, before.Height)
                && !Same(height, last.Height) && nowMs - before.At <= FlipWindowMs)
        {
            if (++_flips >= 2)
            {
                _held = (Math.Max(height, last.Height), Math.Min(height, last.Height), Math.Min(size, last.Size));
                logger.LogWarning("The page around a grid alternates the grid's height between {High} and {Low} px, which ask for {HighSize} " +
                    "and {LowSize} rows: keeping {Kept}. A scrollbar that narrows the page so that text above the grid wraps does this; " +
                    "\"scrollbar-gutter: stable\" on the scroll container keeps the width, and the row.",
                    Math.Round(_held.Value.High), Math.Round(_held.Value.Low), Math.Max(size, last.Size), _held.Value.Size, _held.Value.Size);
                return _held.Value.Size;
            }
        }
        else
            _flips = 0;
        _previous = _current;
        _current = (height, size, nowMs);
        return size;
    }

    private static bool Same(double a, double b) => Math.Abs(a - b) < 1;
}
