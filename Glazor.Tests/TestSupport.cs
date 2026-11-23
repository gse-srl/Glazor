global using static Glazor.Tests.TestSupport;
using Bunit;
using Glazor.Misc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Glazor.Tests;

internal sealed class NoSpinner : ISpinner
{
    public bool Show() => false;
    public void Hide() { }
}

internal static class TestSupport
{
    /// <summary>
    /// The grid needs IConfiguration (ADataGrid reads Grigliaria:TouchScrollerEnabled from it) and logging,
    /// and every JS-interop child — KeyAction, MouseAction, Scroller, TouchScroller, ResizeObserver, Popup,
    /// DialogBox — needs interop to be a no-op rather than a strict expectation.
    /// </summary>
    internal static BunitContext NewContext()
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        ctx.Services.AddLogging();
        return ctx;
    }
}
