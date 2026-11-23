// Drives Scroller.razor.js against a fake DOM: a held left press on the down arrow, released normally; the same with a right
// press while it is held; and with a second left press whose first mouseup never came. A held press on the track, below and
// above the handle, 60 px down a 200 px track. Prints the scroll calls as JSON.
// Usage: node ScrollerRepeat.mjs <path to Scroller.razor.js copied as .mjs>
import { pathToFileURL } from "node:url";
globalThis.window = globalThis;
const docListeners = {};
globalThis.document = {
    onselectstart: null,
    addEventListener: (t, f) => { (docListeners[t] ||= []).push(f); },
    removeEventListener: (t, f) => { docListeners[t] = (docListeners[t] || []).filter(x => x !== f); },
    dispatch: (t, e) => { for (const f of [...(docListeners[t] || [])]) f(e); },
};
const { registerScrollerCallback, cleanup } = await import(pathToFileURL(process.argv[2]).href);

function El(cls, kids = [], h = 20) {
    const listeners = {};
    this.className = cls; this.style = {}; this.childNodes = kids; this.offsetHeight = h; this.offsetWidth = 20;
    kids.forEach(k => k.parentNode = this);
    this.addEventListener = (t, f) => { (listeners[t] ||= []).push(f); };
    this.removeEventListener = (t, f) => { listeners[t] = (listeners[t] || []).filter(x => x !== f); };
    this.dispatch = (t, e) => { for (const f of [...(listeners[t] || [])]) f(e); };
    this.getBoundingClientRect = () => ({ top: 0, left: 0 });
}

const sleep = ms => new Promise(r => setTimeout(r, ms));

async function run(scenario) {
    const down = new El("scrollBarDown");
    const bar = new El("vertScrollBar", [new El("scrollBarUp"), new El("scrollBarTrack", [new El("scrollBarHandle", [], 30)], 200), down], 240);
    bar.parentNode = { offsetHeight: 240 };
    const calls = [];
    registerScrollerCallback(bar, { invokeMethodAsync: async (m, idx) => calls.push(idx) }, "P", 1000, 10, 0, true);
    bar.dispatch("mousedown", { target: down, button: 0 });
    await sleep(500);   // past the hold delay: repeating
    let secondPressSteps = 0;
    if (scenario !== "single") {
        const before = calls.length;
        bar.dispatch("mousedown", { target: down, button: scenario === "rightPress" ? 2 : 0 });
        secondPressSteps = calls.length - before;
    }
    await sleep(50);
    document.dispatch("mouseup", {});
    if (scenario === "rightPress")
        document.dispatch("mouseup", {});   // the left release
    const atRelease = calls.length;
    await sleep(400);
    const afterRelease = calls.length - atRelease;
    cleanup(bar);
    await sleep(200);
    return { secondPressSteps, afterRelease, afterCleanup: calls.length - atRelease - afterRelease };
}

// 100 rows, a page of 10, a handle of 30 px: the handle covers the pointer from row 16 to row 31
async function track(startRow) {
    const t = new El("scrollBarTrack", [new El("scrollBarHandle", [], 30)], 200);
    const bar = new El("vertScrollBar", [new El("scrollBarUp"), t, new El("scrollBarDown")], 240);
    bar.parentNode = { offsetHeight: 240 };
    const calls = [];
    registerScrollerCallback(bar, { invokeMethodAsync: async (m, idx) => calls.push(idx) }, "P", 100, 10, startRow, true);
    bar.dispatch("mousedown", { target: t, button: 0, clientY: 60 });
    await sleep(1500);
    document.dispatch("mouseup", {});
    cleanup(bar);
    return { lastRow: calls.at(-1) };
}

console.log(JSON.stringify({ single: await run("single"), rightPress: await run("rightPress"), missedMouseup: await run("missedMouseup"),
    trackDown: await track(0), trackUp: await track(80) }));
process.exit(0);
