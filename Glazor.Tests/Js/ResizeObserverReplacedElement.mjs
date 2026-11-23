// Drives ResizeObserver.razor.js through a host that re-creates its observed element: Blazor resolves a reference to the
// removed element to null. The component may register it, when the render that removed it is the first to pass it, and
// later cleans it up and registers the new one. Prints whether those calls threw, which elements are observed, and which
// one reports a resize.
// Usage: node ResizeObserverReplacedElement.mjs <path to ResizeObserver.razor.js copied as .mjs>
import { pathToFileURL } from "node:url";

const observers = [];
globalThis.ResizeObserver = class {
    constructor(callback) { this.callback = callback; this.targets = new Set(); observers.push(this); }
    observe(target) { this.targets.add(target); }
    unobserve(target) { this.targets.delete(target); }
    disconnect() { this.targets.clear(); }
};
const { registerResizeObserverCallback, cleanup } = await import(pathToFileURL(process.argv[2]).href);

const resized = [];
const dotNet = name => ({ invokeMethodAsync: async (method, width) => { resized.push(`${name}:${width}`); } });
const first = { name: "first", isConnected: true }, second = { name: "second", isConnected: true };

function attempt(call) {
    try {
        call();
        return "ok";
    } catch (e) {
        return `${e.constructor.name}: ${e.message}`;
    }
}

registerResizeObserverCallback(first, dotNet("first"), "ProcessResize", true);
const registerOfRemoved = attempt(() => registerResizeObserverCallback(null, dotNet("removed"), "ProcessResize", true));
const cleanupOfRemoved = attempt(() => cleanup(null));
registerResizeObserverCallback(second, dotNet("second"), "ProcessResize", true);
first.isConnected = false;   // the browser reports a removed element at 0x0, to an observer cleanup could not reach
for (const o of observers)
    for (const target of o.targets)
        o.callback([{ target, contentRect: { width: target === second ? 220 : 0, height: target === second ? 40 : 0 } }]);
await new Promise(resolve => setTimeout(resolve, 0));

console.log(JSON.stringify({
    registerOfRemoved,
    cleanupOfRemoved,
    observed: observers.flatMap(o => [...o.targets].map(t => t.name)),
    resized,
}));
