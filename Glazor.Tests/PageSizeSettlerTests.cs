using Glazor.Grds;

namespace Glazor.Tests;

/// <summary>
/// The page size of a grid whose page has two stable layouts: with 10 rows a scrollbar narrows the page, the text above the grid
/// wraps and leaves it 511 px, room for 9; with 9 the scrollbar goes and it has 566 px, room for 10. Measured on a page laid out
/// so in a real browser, where the grid flipped between the two until reloaded.
/// </summary>
public class PageSizeSettlerTests
{
    [Test]
    public void AFlipBackAndForthKeepsTheSmallerSizeTest()
    {
        PageSizeSettler settler = new();
        Assert.Multiple(() =>
        {
            Assert.That(settler.Settle(566, 10, 0), Is.EqualTo(10));
            Assert.That(settler.Settle(511, 9, 50), Is.EqualTo(9));
            Assert.That(settler.Settle(566, 10, 100), Is.EqualTo(10), "back to where it was once: followed");
            Assert.That(settler.Settle(511, 9, 150), Is.EqualTo(9), "and again: a flip");
            Assert.That(settler.Settle(566, 10, 5000), Is.EqualTo(9), "kept while the height alternates");
        });
    }

    /// <summary>A user going back to a size, slower than a render, is followed.</summary>
    [Test]
    public void AResizeBackAndForthIsFollowedTest()
    {
        PageSizeSettler settler = new();
        Assert.Multiple(() =>
        {
            Assert.That(settler.Settle(900, 18, 0), Is.EqualTo(18));
            Assert.That(settler.Settle(511, 9, 1000), Is.EqualTo(9));
            Assert.That(settler.Settle(900, 18, 1000 + PageSizeSettler.FlipWindowMs), Is.EqualTo(18));
        });
    }

    /// <summary>A window maximized, restored and maximized again in a short time goes back to a size once, and is followed.</summary>
    [Test]
    public void AWindowMaximizedRestoredAndMaximizedIsFollowedTest()
    {
        PageSizeSettler settler = new();
        settler.Settle(511, 9, 0);
        Assert.Multiple(() =>
        {
            Assert.That(settler.Settle(900, 18, 60_000), Is.EqualTo(18), "maximized");
            Assert.That(settler.Settle(511, 9, 61_000), Is.EqualTo(9), "restored");
            Assert.That(settler.Settle(900, 18, 62_000), Is.EqualTo(18), "maximized again");
            Assert.That(settler.Settle(900, 18, 120_000), Is.EqualTo(18));
        });
    }

    /// <summary>Another height, a real resize, ends the hold.</summary>
    [Test]
    public void AnotherHeightEndsTheHoldTest()
    {
        PageSizeSettler settler = new();
        settler.Settle(566, 10, 0);
        settler.Settle(511, 9, 50);
        settler.Settle(566, 10, 100);
        Assert.That(settler.Settle(511, 9, 150), Is.EqualTo(9));
        Assert.That(settler.Settle(566, 10, 200), Is.EqualTo(9), "held");
        Assert.Multiple(() =>
        {
            Assert.That(settler.Settle(900, 18, 3000), Is.EqualTo(18));
            Assert.That(settler.Settle(566, 10, 6000), Is.EqualTo(10), "a height of the old flip, after it ended");
        });
    }

    /// <summary>Rows that change height in a box of one height, as text wraps and unwraps in them, are followed back and forth.</summary>
    [Test]
    public void RowsChangingHeightInOneBoxAreFollowedTest()
    {
        PageSizeSettler settler = new();
        Assert.Multiple(() =>
        {
            Assert.That(settler.Settle(340, 6, 0), Is.EqualTo(6));
            Assert.That(settler.Settle(340, 5, 20), Is.EqualTo(5));
            Assert.That(settler.Settle(340, 6, 40), Is.EqualTo(6));
        });
    }

    /// <summary>A size that only changes, a refinement at one height or a resize that does not come back, is followed.</summary>
    [Test]
    public void ChangesThatDoNotComeBackAreFollowedTest()
    {
        PageSizeSettler settler = new();
        Assert.Multiple(() =>
        {
            Assert.That(settler.Settle(566, 11, 0), Is.EqualTo(11));
            Assert.That(settler.Settle(566, 10, 20), Is.EqualTo(10), "refined once drawn");
            Assert.That(settler.Settle(566, 10, 40), Is.EqualTo(10));
            Assert.That(settler.Settle(700, 13, 60), Is.EqualTo(13));
            Assert.That(settler.Settle(800, 15, 80), Is.EqualTo(15));
        });
    }
}
