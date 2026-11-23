using System.Diagnostics;
using System.Text.Json;

namespace Glazor.Tests;

/// <summary>
/// The JS modules run in Node against a fake DOM, since bUnit cannot execute them. Skipped where Node is not installed.
/// </summary>
public class JsModuleTests
{
    /// <summary>Runs a script from Glazor.Tests/Js against a module under Glazor, copied as .mjs so Node treats it as ES module.</summary>
    private static JsonElement RunProbe(string script, string module)
    {
        string? node = Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator)
            .SelectMany(dir => new[] { "node.exe", "node" }.Select(name => Path.Combine(dir, name)))
            .FirstOrDefault(File.Exists);
        Assume.That(node, Is.Not.Null, "Node is not installed");
        string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            string moduleCopy = Path.Combine(dir, Path.GetFileNameWithoutExtension(module) + ".mjs");
            File.Copy(Path.Combine(RepoRoot.Dir, "Glazor", module), moduleCopy);
            ProcessStartInfo psi = new(node!, [Path.Combine(RepoRoot.Dir, "Glazor.Tests", "Js", script), moduleCopy])
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            using Process process = Process.Start(psi)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();   // both pipes at once, so neither fills and blocks the probe
            Task<string> errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60_000))
            {
                process.Kill(true);
                Assert.Fail($"{script} did not end within a minute");
            }
            Assert.That(process.ExitCode, Is.Zero, errors.Result);
            return JsonDocument.Parse(output.Result).RootElement;
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// A stroke that is registered but disabled must let the event through untouched: in row-cursor mode the data cells carry a
    /// disabled double-click stroke, and the row's own double-click must still fire from them.
    /// </summary>
    [Test]
    public void DisabledMouseStrokeLetsTheEventThroughTest()
    {
        JsonElement result = RunProbe("MouseActionStrokes.mjs", Path.Combine("Misc", "MouseAction.razor.js"));
        JsonElement dataCell = result.GetProperty("dataCell");
        JsonElement headingCell = result.GetProperty("headingCell");
        Assert.Multiple(() =>
        {
            Assert.That(dataCell.GetProperty("stoppedAt").GetString(), Is.EqualTo("TR"), "the disabled cell stroke lets it through; the row's stroke consumes it");
            Assert.That(dataCell.GetProperty("calls").EnumerateArray().Select(c => c.GetString()), Is.EqualTo(new[] { "row" }));
            Assert.That(headingCell.GetProperty("stoppedAt").GetString(), Is.EqualTo("TR"));
            Assert.That(headingCell.GetProperty("calls").EnumerateArray().Select(c => c.GetString()), Is.EqualTo(new[] { "row" }));
        });
    }

    /// <summary>
    /// A host that re-creates its observed element passes the component a reference one render late, since @ref is set after
    /// the render: the element it names may be gone, and reaches the module as null, to be registered or cleaned up. Neither
    /// may throw, or the new element is never observed and the circuit ends.
    /// </summary>
    [Test]
    public void ReplacedObservedElementIsCleanedUpAndTheNewOneObservedTest()
    {
        JsonElement result = RunProbe("ResizeObserverReplacedElement.mjs", Path.Combine("Misc", "ResizeObserver.razor.js"));
        Assert.Multiple(() =>
        {
            Assert.That(result.GetProperty("registerOfRemoved").GetString(), Is.EqualTo("ok"));
            Assert.That(result.GetProperty("cleanupOfRemoved").GetString(), Is.EqualTo("ok"));
            Assert.That(result.GetProperty("observed").EnumerateArray().Select(e => e.GetString()), Does.Contain("second"));
            Assert.That(result.GetProperty("resized").EnumerateArray().Select(e => e.GetString()), Is.EqualTo(new[] { "second:220" }),
                "the removed element's 0x0 does not reach the host");
        });
    }

    /// <summary>
    /// A host's scrollable element removed a render before the touch scroller registers it, or is disposed, reaches the module
    /// as null: neither call may throw, as for the resize observer.
    /// </summary>
    [Test]
    public void TouchScrollerToleratesARemovedElementTest()
    {
        JsonElement result = RunProbe("TouchScrollerRemovedElement.mjs", Path.Combine("Grds", "TouchScroller.razor.js"));
        Assert.Multiple(() =>
        {
            Assert.That(result.GetProperty("registerOfRemoved").GetString(), Is.EqualTo("ok"));
            Assert.That(result.GetProperty("cleanupOfRemoved").GetString(), Is.EqualTo("ok"));
        });
    }

    /// <summary>
    /// A single clamped line overflows horizontally, a line-clamped box vertically: the module must notice the ellipsis in both
    /// modes, and only there.
    /// </summary>
    [Test]
    public void ClamperNoticesTheEllipsisInBothModesTest()
    {
        JsonElement result = RunProbe("ClamperModes.mjs", Path.Combine("Misc", "Clamper.razor.js"));
        static string[] Calls(JsonElement e) => e.GetProperty("calls").EnumerateArray().Select(c => c.GetString()!).ToArray();
        JsonElement none = result.GetProperty("notClamped");
        Assert.Multiple(() =>
        {
            foreach (string mode in new[] { "singleLine", "twoLines" })
            {
                JsonElement clamped = result.GetProperty(mode);
                Assert.That(Calls(clamped.GetProperty("overEllipsis")), Is.EqualTo(new[] { "ProcessOver:true" }), mode);
                Assert.That(clamped.GetProperty("overEllipsis").GetProperty("highlighted").GetBoolean(), Is.True, mode);
                Assert.That(Calls(clamped.GetProperty("click")), Is.EqualTo(new[] { "ProcessClick" }), mode);
                Assert.That(Calls(clamped.GetProperty("elsewhere")), Is.EqualTo(new[] { "ProcessOver:false" }), mode);
            }
            Assert.That(Calls(none.GetProperty("move")), Is.Empty, "nothing is clamped");
            Assert.That(Calls(none.GetProperty("click")), Is.Empty);
        });
    }

    /// <summary>
    /// The touch scroller is registered again after every render, and a scroll step makes one: a swipe in progress must survive
    /// that, unless the scroller comes back disabled, when the rest of the gesture belongs to the browser.
    /// </summary>
    [Test]
    public void TouchSwipeSurvivesTheRenderItCausesTest()
    {
        JsonElement result = RunProbe("TouchScrollerGesture.mjs", Path.Combine("Grds", "TouchScroller.razor.js"));
        static int[] Scrolls(JsonElement e) => e.GetProperty("scrolls").EnumerateArray().Select(s => s.GetInt32()).ToArray();
        static bool[] Prevented(JsonElement e) => e.GetProperty("prevented").EnumerateArray().Select(p => p.GetBoolean()).ToArray();
        JsonElement enabled = result.GetProperty("enabled"), disabled = result.GetProperty("disabledMidGesture");
        Assert.Multiple(() =>
        {
            Assert.That(Scrolls(enabled), Has.Length.EqualTo(10).And.All.EqualTo(3), "one step per move, as each moves a row");
            Assert.That(Prevented(enabled), Is.All.True, "the grid keeps the gesture");
            Assert.That(Scrolls(disabled), Has.Length.EqualTo(1), "only the step before the scroller was disabled");
            Assert.That(Prevented(disabled).Skip(1), Is.All.False, "then the browser has it");
        });
    }

    /// <summary>
    /// Alt and Meta combinations belong to the browser, Alt+ArrowLeft being Back and Cmd+arrows navigating on macOS, but for a
    /// stroke registered with Alt, as the grid's Alt+Enter for the row dialog: that one runs, and plain Enter keeps its own. The
    /// plain keys, and the Ctrl ones registered as such, still reach their handlers.
    /// </summary>
    [Test]
    public void AltAndMetaCombinationsAreLeftToTheBrowserTest()
    {
        JsonElement result = RunProbe("KeyActionModifiers.mjs", Path.Combine("Misc", "KeyAction.razor.js"));
        static (bool, bool) Outcome(JsonElement r, string key) =>
            (r.GetProperty(key).GetProperty("handled").GetBoolean(), r.GetProperty(key).GetProperty("prevented").GetBoolean());
        static string? By(JsonElement r, string key) => r.GetProperty(key).GetProperty("by").GetString();
        Assert.Multiple(() =>
        {
            Assert.That(Outcome(result, "arrowLeft"), Is.EqualTo((true, true)));
            Assert.That(Outcome(result, "ctrlSpace"), Is.EqualTo((true, true)));
            Assert.That(Outcome(result, "altArrowLeft"), Is.EqualTo((false, false)), "Alt+ArrowLeft");
            Assert.That(Outcome(result, "metaHome"), Is.EqualTo((false, false)), "Meta+Home");
            Assert.That(Outcome(result, "ctrlAltSpace"), Is.EqualTo((false, false)), "Ctrl+Alt+Space, AltGr on Windows");
            Assert.That((Outcome(result, "enter"), By(result, "enter")), Is.EqualTo(((true, true), "enter")));
            Assert.That((Outcome(result, "altEnter"), By(result, "altEnter")), Is.EqualTo(((true, true), "altEnter")), "Alt+Enter, its own stroke");
            Assert.That(Outcome(result, "ctrlAltEnter"), Is.EqualTo((false, false)), "Ctrl+Alt+Enter");
        });
    }

    /// <summary>
    /// Opening the search popup registers the grid's keys again while a character typed meanwhile waits to be sent: the
    /// re-registration sends it rather than dropping it. A key disposed drops it, and a batch already sent is not sent twice.
    /// </summary>
    [Test]
    public void ACharacterWaitingIsSentWhenTheKeysRegisterAgainTest()
    {
        JsonElement result = RunProbe("KeyActionPendingOnCleanup.mjs", Path.Combine("Misc", "KeyAction.razor.js"));
        static string?[] Sent(JsonElement r, string step) => r.GetProperty(step).EnumerateArray().Select(c => c.GetString()).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(Sent(result, "reRegistered"), Is.EqualTo(new[] { "a" }), "sent at once");
            Assert.That(Sent(result, "disposed"), Is.Empty, "dropped");
            Assert.That(Sent(result, "afterTimer"), Is.EqualTo(new[] { "c" }), "once, by its timer");
        });
    }

    /// <summary>
    /// A cell template may hold a field: a character typed there is the field's, not the grid's type-ahead. On the grid itself,
    /// and on a checkbox clicked in it, the type-ahead still collects it.
    /// </summary>
    [Test]
    public void ACharacterTypedInAFieldIsTheFieldsTest()
    {
        JsonElement result = RunProbe("KeyActionTextFields.mjs", Path.Combine("Misc", "KeyAction.razor.js"));
        JsonElement prevented = result.GetProperty("prevented");
        Assert.Multiple(() =>
        {
            foreach (string field in new[] { "text", "number", "textarea", "select", "editable" })
                Assert.That(prevented.GetProperty(field).GetBoolean(), Is.False, field);
            Assert.That(prevented.GetProperty("grid").GetBoolean(), Is.True);
            Assert.That(prevented.GetProperty("checkbox").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("collected").EnumerateArray().Select(c => c.GetString()), Is.EqualTo(new[] { "ab" }));
        });
    }

    /// <summary>
    /// A click leaves the focus on the button or link clicked, and the next Enter must still run the Enter action around it, a
    /// dialog's OK or the host's own. A button that keeps its own Enter, as the dialogs' Cancel does, carries an action for it.
    /// </summary>
    [Test]
    public void EnterOnAFocusedButtonRunsTheActionAroundItTest()
    {
        JsonElement result = RunProbe("KeyActionEnterOnButton.mjs", Path.Combine("Misc", "KeyAction.razor.js"));
        static string[] Calls(JsonElement r, string target) =>
            r.GetProperty(target).GetProperty("calls").EnumerateArray().Select(c => c.GetString()!).ToArray();
        static bool Prevented(JsonElement r, string target) => r.GetProperty(target).GetProperty("prevented").GetBoolean();
        Assert.Multiple(() =>
        {
            foreach (string target in new[] { "content", "input", "button", "link" })
            {
                Assert.That(Calls(result, target), Is.EqualTo(new[] { "content" }), target);
                Assert.That(Prevented(result, target), Is.True, target);
            }
            Assert.That(Calls(result, "ownEnter"), Is.EqualTo(new[] { "cancel" }), "a button with its own Enter action");
            Assert.That(Prevented(result, "ownEnter"), Is.True);
        });
    }

    /// <summary>
    /// A key pressed in a modal dialog is the dialog's: a stroke bound around the dialog, a host panel's, does not run, while
    /// those on the dialog and inside it do. Outside the dialog, or in a dialog open but not modal, the panel's strokes run.
    /// </summary>
    [Test]
    public void AModalDialogKeepsItsKeysTest()
    {
        JsonElement result = RunProbe("KeyActionModalDialog.mjs", Path.Combine("Misc", "KeyAction.razor.js"));
        static string[] Calls(JsonElement r, string press) =>
            r.GetProperty(press).GetProperty("calls").EnumerateArray().Select(c => c.GetString()!).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(Calls(result, "f2InContent"), Is.EqualTo(new[] { "contentF2" }));
            Assert.That(Calls(result, "f2OnDialog"), Is.EqualTo(new[] { "dialogF2" }));
            Assert.That(Calls(result, "enterInDialog"), Is.Empty, "the panel's Enter does not see it");
            Assert.That(result.GetProperty("enterInDialog").GetProperty("prevented").GetBoolean(), Is.False, "the browser keeps it");
            Assert.That(Calls(result, "f2OnPanel"), Is.EqualTo(new[] { "panelF2" }));
            Assert.That(Calls(result, "enterOnPanel"), Is.EqualTo(new[] { "panelEnter" }));
            Assert.That(Calls(result, "f2InPopup"), Is.EqualTo(new[] { "panelF2" }), "a dialog that is not modal");
        });
    }

    /// <summary>
    /// A key cancelled on keydown keeps the field from committing, so a stroke with CommitFocused fires change on the field first:
    /// an input bound on change reaches the server before the action. A button gets none, nor does a stroke without the flag.
    /// </summary>
    [Test]
    public void CommitFocusedFiresChangeBeforeTheActionTest()
    {
        JsonElement result = RunProbe("KeyActionCommitFocused.mjs", Path.Combine("Misc", "KeyAction.razor.js"));
        static string[] Order(JsonElement r, string press) => r.GetProperty(press).EnumerateArray().Select(c => c.GetString()!).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(Order(result, "enterOnInput"), Is.EqualTo(new[] { "change", "post" }));
            Assert.That(Order(result, "enterOnSelect"), Is.EqualTo(new[] { "change", "post" }));
            Assert.That(Order(result, "enterOnButton"), Is.EqualTo(new[] { "post" }));
            Assert.That(Order(result, "escapeOnInput"), Is.EqualTo(new[] { "cancel" }));
        });
    }

    /// <summary>
    /// With ExclusiveClick a double-click whose clicks are 300 ms apart, well inside the usual OS interval, must run the
    /// double-click alone; a single click still runs, once, when the window has passed. A click held back while its component
    /// is cleaned up or disabled does not run.
    /// </summary>
    [Test]
    public void ExclusiveClickKeepsASlowDoubleClickWholeTest()
    {
        JsonElement result = RunProbe("MouseActionExclusiveClick.mjs", Path.Combine("Misc", "MouseAction.razor.js"));
        static string[] Calls(JsonElement r, string name) => r.GetProperty(name).EnumerateArray().Select(c => c.GetString()!).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(Calls(result, "slowDoubleClick"), Is.EqualTo(new[] { "dblclick" }));
            Assert.That(Calls(result, "singleClick"), Is.EqualTo(new[] { "click" }));
            Assert.That(Calls(result, "afterCleanup"), Is.Empty, "the component was cleaned up while the click was held");
            Assert.That(Calls(result, "afterDisable"), Is.Empty, "the component was disabled while the click was held");
        });
    }

    /// <summary>
    /// A slow swipe moves less than a row per touchmove: the distance must add up until it makes a step, not be discarded on
    /// every move.
    /// </summary>
    [Test]
    public void SlowTouchSwipeAddsUpTest()
    {
        JsonElement slow = RunProbe("TouchScrollerGesture.mjs", Path.Combine("Grds", "TouchScroller.razor.js")).GetProperty("slow");
        int[] scrolls = slow.GetProperty("scrolls").EnumerateArray().Select(s => s.GetInt32()).ToArray();
        Assert.That(scrolls, Has.Length.EqualTo(5).And.All.EqualTo(3), "ten quarter-row moves: a step every half row");
    }

    /// <summary>
    /// The .NET side registers again after every render so that a parameter change reaches the JS side; before, the finger count
    /// was read by the constructor only, so a Fingers set after the first render changed nothing.
    /// </summary>
    [Test]
    public void TouchFingersChangedAfterTheFirstRegistrationApplyTest()
    {
        JsonElement changed = RunProbe("TouchScrollerGesture.mjs", Path.Combine("Grds", "TouchScroller.razor.js")).GetProperty("fingersChanged");
        int[] scrolls = changed.GetProperty("scrolls").EnumerateArray().Select(s => s.GetInt32()).ToArray();
        Assert.That(scrolls, Has.Length.EqualTo(10).And.All.EqualTo(3), "a one-finger swipe scrolls once the registration asks for one finger");
    }

    /// <summary>
    /// A cancelled touchstart gives no click, so the start of a gesture is left to the browser and a tap still selects the row;
    /// the moves are cancelled, which is what keeps the page from scrolling under the grid.
    /// </summary>
    /// <summary>
    /// A grid with the touch scroller off, the default, adds no listener that can cancel a move, which would make the browser wait
    /// for it before every scroll of the page over the grid; a later render that turns it on adds it, and a swipe then scrolls.
    /// </summary>
    [Test]
    public void TouchScrollerOffAddsNoBlockingListenerTest()
    {
        JsonElement later = RunProbe("TouchScrollerGesture.mjs", Path.Combine("Grds", "TouchScroller.razor.js")).GetProperty("enabledLater");
        Assert.Multiple(() =>
        {
            Assert.That(later.GetProperty("disabledListens").GetBoolean(), Is.False, "off: no touchmove or touchend listener");
            Assert.That(later.GetProperty("movePassive").GetBoolean(), Is.False, "on: the move's listener may cancel");
            Assert.That(later.GetProperty("movePrevented").GetBoolean(), Is.True);
            Assert.That(later.GetProperty("scrolls").EnumerateArray().Select(s => s.GetInt32()), Is.Not.Empty);
        });
    }

    [TestCase("tapOne")]
    [TestCase("tapTwo")]
    public void TouchStartIsLeftToTheBrowserTest(string fingers)
    {
        JsonElement tap = RunProbe("TouchScrollerGesture.mjs", Path.Combine("Grds", "TouchScroller.razor.js")).GetProperty(fingers);
        Assert.Multiple(() =>
        {
            Assert.That(tap.GetProperty("startPrevented").GetBoolean(), Is.False, "the tap goes through to the click");
            Assert.That(tap.GetProperty("movePrevented").GetBoolean(), Is.True, "the move is the grid's");
            Assert.That(tap.GetProperty("startPassive").GetBoolean(), Is.True, "and its listener says so, so the browser does not wait for it");
            Assert.That(tap.GetProperty("movePassive").GetBoolean(), Is.False, "the move's may cancel, so it is not passive");
        });
    }

    /// <summary>
    /// A MouseAction installs a listener for the event type of its stroke only, so the cells and rows, which register clicks,
    /// carry no non-passive wheel listener; the listener for a type leaves with the last stroke of that type.
    /// </summary>
    [Test]
    public void MouseListenersFollowTheStrokeTypesRegisteredTest()
    {
        JsonElement listeners = RunProbe("MouseActionStrokes.mjs", Path.Combine("Misc", "MouseAction.razor.js")).GetProperty("listeners");
        static string[] Of(JsonElement e, string name) => e.GetProperty(name).EnumerateArray().Select(t => t.GetString()!).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(Of(listeners, "row"), Is.EqualTo(new[] { "dblclick" }), "no wheel listener on a row");
            Assert.That(Of(listeners, "cell"), Is.EqualTo(new[] { "dblclick" }), "nor on a cell");
            Assert.That(Of(listeners, "afterRegister"), Is.EqualTo(new[] { "click", "wheel" }), "one listener per type, two wheel strokes share it");
            Assert.That(Of(listeners, "afterOneWheelLeft"), Is.EqualTo(new[] { "click", "wheel" }), "a wheel stroke is still there");
            Assert.That(Of(listeners, "afterBothWheelLeft"), Is.EqualTo(new[] { "click" }), "the wheel listener leaves with its last stroke");
            Assert.That(Of(listeners, "afterAllLeft"), Is.Empty);
        });
    }

    // One run serves both scrollbar tests: the probe waits on real timers for several seconds.
    private static readonly Lazy<JsonElement> scrollerRepeat = new(() => RunProbe("ScrollerRepeat.mjs", Path.Combine("Grds", "Scroller.razor.js")));

    /// <summary>
    /// A held press on a scrollbar arrow repeats until its mouseup: a right press while it is held must not start a second
    /// repeat, and a press whose mouseup never came must not leave its repeat running under the next one.
    /// </summary>
    [Test]
    public void ScrollbarRepeatStopsAtTheReleaseTest()
    {
        JsonElement result = scrollerRepeat.Value;
        static int Of(JsonElement r, string scenario, string name) => r.GetProperty(scenario).GetProperty(name).GetInt32();
        Assert.Multiple(() =>
        {
            foreach (string scenario in new[] { "single", "rightPress", "missedMouseup" })
            {
                Assert.That(Of(result, scenario, "afterRelease"), Is.Zero, scenario);
                Assert.That(Of(result, scenario, "afterCleanup"), Is.Zero, scenario);
            }
            Assert.That(Of(result, "rightPress", "secondPressSteps"), Is.Zero, "the right button does not step");
            Assert.That(Of(result, "missedMouseup", "secondPressSteps"), Is.EqualTo(1), "a new left press steps as usual");
        });
    }

    /// <summary>A held press on the scrollbar track pages towards the pointer and stops once the handle is under it, as native scrollbars do.</summary>
    [Test]
    public void HeldTrackPressStopsAtThePointerTest()
    {
        JsonElement result = scrollerRepeat.Value;
        Assert.Multiple(() =>
        {
            Assert.That(result.GetProperty("trackDown").GetProperty("lastRow").GetInt32(), Is.EqualTo(20), "paging down");
            Assert.That(result.GetProperty("trackUp").GetProperty("lastRow").GetInt32(), Is.EqualTo(30), "paging up");
        });
    }

    /// <summary>
    /// One popup is open at a time: showing one closes the one open before it, but not itself. Shown at a point, a popup that
    /// would cross the right or bottom edge opens to the left of, or above, the point, and never past the top left corner.
    /// </summary>
    [Test]
    public void PopupClosesTheOtherAndStaysInTheViewportTest()
    {
        JsonElement result = RunProbe("PopupShow.mjs", Path.Combine("Misc", "Popup.razor.js"));
        Assert.Multiple(() =>
        {
            Assert.That(result.GetProperty("first").GetProperty("open").GetBoolean(), Is.False, "closed by the second");
            Assert.That(result.GetProperty("first").GetProperty("closes").GetInt32(), Is.EqualTo(1));
            Assert.That(result.GetProperty("second").GetProperty("open").GetBoolean(), Is.True, "shown again, still open");
            Assert.That(result.GetProperty("second").GetProperty("closes").GetInt32(), Is.Zero);
            Assert.That(result.GetProperty("inside").GetString(), Is.EqualTo("50px,40px"));
            Assert.That(result.GetProperty("rightEdge").GetString(), Is.EqualTo("650px,20px"), "800 wide, 100 wide at 750");
            Assert.That(result.GetProperty("bottomEdge").GetString(), Is.EqualTo("10px,530px"), "600 high, 50 high at 580");
            Assert.That(result.GetProperty("tallerThanTheViewport").GetString(), Is.EqualTo("30px,0px"));
            Assert.That(result.GetProperty("anchoredAfterAPoint").GetString(), Is.EqualTo(",,none->bottom span-right"),
                "no anchor area at the point; then the anchor places it, no corner left");
            Assert.That(result.GetProperty("styleCornerAfterAPoint").GetString(), Is.EqualTo("5px,,none->"), "the Style's own corner back");
            Assert.That(result.GetProperty("pointAgain").GetString(), Is.EqualTo("70px,50px"));
        });
    }

    /// <summary>
    /// Where the browser has no closedby on a dialog, a popup closes itself as its closedby asks: "any" on Esc and on a press
    /// outside, "closerequest" on Esc only, as DialogBox does for a non-modal dialog, and "none" never.
    /// </summary>
    [Test]
    public void PopupFollowsItsClosedByWhereTheBrowserDoesNotTest()
    {
        JsonElement result = RunProbe("PopupClosedByFallback.mjs", Path.Combine("Misc", "Popup.razor.js"));
        bool Closed(string by, string how) => result.GetProperty(by).GetProperty(how).GetBoolean();
        Assert.Multiple(() =>
        {
            Assert.That((Closed("any", "escape"), Closed("any", "pressOutside")), Is.EqualTo((true, true)), "any");
            Assert.That((Closed("closerequest", "escape"), Closed("closerequest", "pressOutside")), Is.EqualTo((true, false)), "closerequest");
            Assert.That((Closed("none", "escape"), Closed("none", "pressOutside")), Is.EqualTo((false, false)), "none");
            Assert.That(result.GetProperty("listenersLeft").GetInt32(), Is.Zero, "every listener removed on close");
        });
    }

    /// <summary>
    /// Where the browser has no closedby on a dialog, a DialogBox follows its closedby: shown, Esc closes it for "any" and
    /// "closerequest" and a press outside for "any"; shown modal, Esc does not close it for "none" and a press on the backdrop
    /// closes it for "any". It leaves no listener once closed, and shown again while open it adds none.
    /// </summary>
    [Test]
    public void DialogBoxFollowsItsClosedByWhereTheBrowserDoesNotTest()
    {
        JsonElement result = RunProbe("DialogBoxClosedByFallback.mjs", Path.Combine("Misc", "DialogBox.razor.js"));
        (bool, bool, bool) Closed(string mode, string by, string far)
        {
            JsonElement r = result.GetProperty(mode).GetProperty(by);
            return (r.GetProperty("escape").GetBoolean(), r.GetProperty("pressInside").GetBoolean(), r.GetProperty(far).GetBoolean());
        }
        Assert.Multiple(() =>
        {
            Assert.That(Closed("nonModal", "any", "pressOutside"), Is.EqualTo((true, false, true)), "shown, any");
            Assert.That(Closed("nonModal", "closerequest", "pressOutside"), Is.EqualTo((true, false, false)), "shown, closerequest");
            Assert.That(Closed("nonModal", "none", "pressOutside"), Is.EqualTo((false, false, false)), "shown, none");
            Assert.That(Closed("modal", "any", "pressBackdrop"), Is.EqualTo((true, false, true)), "modal, any");
            Assert.That(Closed("modal", "closerequest", "pressBackdrop"), Is.EqualTo((true, false, false)), "modal, closerequest");
            Assert.That(Closed("modal", "none", "pressBackdrop"), Is.EqualTo((false, false, false)), "modal, none: the spinner's case");
            Assert.That(result.GetProperty("nonModal").GetProperty("any").GetProperty("listeners").GetInt32(), Is.EqualTo(3),
                "shown twice, listening once");
            foreach (string mode in new[] { "nonModal", "modal" })
                foreach (string by in new[] { "any", "closerequest", "none" })
                    Assert.That(result.GetProperty(mode).GetProperty(by).GetProperty("left").GetInt32(), Is.Zero, $"{mode}, {by}: no listener left");
        });
    }

    /// <summary>
    /// The idle tracker listens in the capture phase, so a key the grid's handler stops from propagating still makes the user
    /// active; activity before the timeout keeps the user active; a second start adds no listener, and cleanup removes them all
    /// and stops the timer.
    /// </summary>
    [Test]
    public void IdleTrackerSeesActivityTheGridStopsTest()
    {
        JsonElement result = RunProbe("IdleTrackerCapture.mjs", Path.Combine("Misc", "IdleTracker.razor.js"));
        static string[] Strings(JsonElement e) => e.EnumerateArray().Select(s => s.GetString()!).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(Strings(result.GetProperty("registered")), Is.All.EndsWith(":true"), "every listener in the capture phase");
            Assert.That(Strings(result.GetProperty("activeBeforeTimeout")), Is.Empty, "activity resets the timer");
            Assert.That(Strings(result.GetProperty("idleAfterTimeout")), Is.EqualTo(new[] { "UserIsIdle" }));
            Assert.That(Strings(result.GetProperty("activeOnStoppedKey")), Is.EqualTo(new[] { "UserIsIdle", "UserIsActive" }),
                "a key the grid stops still counts");
            Assert.That(result.GetProperty("listenersAfterRestart").GetInt32(), Is.EqualTo(7), "started again, listening once");
            Assert.That(result.GetProperty("listenersLeft").GetInt32(), Is.Zero, "every listener removed");
            Assert.That(result.GetProperty("callsAfterCleanup").GetInt32(), Is.Zero, "the timer stopped");
        });
    }
}
