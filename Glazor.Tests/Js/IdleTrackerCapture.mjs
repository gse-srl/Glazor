// Drives IdleTracker.razor.js on a fake window and clock. Activity that a grid's handler stops from propagating still counts,
// as the tracker listens in the capture phase; activity before the timeout keeps the user active; a second start on the same
// element adds no listener; cleanup removes every listener and stops the timer.
// Usage: node IdleTrackerCapture.mjs <path to IdleTracker.razor.js copied as .mjs>
import { pathToFileURL } from "node:url";

// A listener is removed only with the capture flag it was added with, as in the DOM
const capture = options => typeof options === "boolean" ? options : !!options?.capture;
const listeners = [];
globalThis.window = {
    addEventListener(type, fn, options) { listeners.push({ type, fn, capture: capture(options) }); },
    removeEventListener(type, fn, options) {
        const i = listeners.findIndex(l => l.type === type && l.fn === fn && l.capture === capture(options));
        if (i >= 0)
            listeners.splice(i, 1);
    },
};
// An event on an element inside the window: capture listeners first, then the element's handler, then the bubble listeners
// unless the handler stopped it
const dispatch = (type, stoppedByTarget) => {
    for (const l of listeners.filter(l => l.type === type && l.capture))
        l.fn({ type });
    if (!stoppedByTarget)
        for (const l of listeners.filter(l => l.type === type && !l.capture))
            l.fn({ type });
};

let now = 0, nextId = 1;
const timers = new Map();
globalThis.setTimeout = (fn, ms) => { timers.set(nextId, { at: now + ms, fn }); return nextId++; };
globalThis.clearTimeout = id => { timers.delete(id); };
const advance = ms => {
    const until = now + ms;
    for (;;) {
        const due = [...timers.entries()].filter(([, t]) => t.at <= until).sort((a, b) => a[1].at - b[1].at)[0];
        if (!due)
            break;
        timers.delete(due[0]);
        now = due[1].at;
        due[1].fn();
    }
    now = until;
};

const { startTracker, cleanup } = await import(pathToFileURL(process.argv[2]).href);
const calls = [];
const dotNet = { invokeMethodAsync(name) { calls.push(name); return Promise.resolve(); } };
const span = {};

startTracker(span, dotNet, 1000);
const registered = listeners.map(l => `${l.type}:${l.capture}`);
advance(600);
dispatch("mousemove", false);
advance(600);
const activeBeforeTimeout = [...calls];
advance(400);
const idleAfterTimeout = [...calls];
dispatch("keydown", true);   // the grid's key handler stops it
const activeOnStoppedKey = [...calls];
startTracker(span, dotNet, 1000);   // a later render starts it again
const listenersAfterRestart = listeners.length;
cleanup(span);
const listenersLeft = listeners.length;
advance(5000);
const callsAfterCleanup = calls.length - activeOnStoppedKey.length;

console.log(JSON.stringify({
    registered, activeBeforeTimeout, idleAfterTimeout, activeOnStoppedKey, listenersAfterRestart, listenersLeft, callsAfterCleanup,
}));
