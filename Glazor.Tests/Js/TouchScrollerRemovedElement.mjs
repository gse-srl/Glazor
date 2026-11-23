// Drives TouchScroller.razor.js with the reference Blazor gives for an element a host removed a render earlier: null. The
// component may register it, when the render that removed it is the first to pass it, and cleans it up when it is disposed.
// Prints whether those calls threw.
// Usage: node TouchScrollerRemovedElement.mjs <path to TouchScroller.razor.js copied as .mjs>
import { pathToFileURL } from "node:url";
const { registerScrollerCallback, cleanup } = await import(pathToFileURL(process.argv[2]).href);

function attempt(call) {
    try {
        call();
        return "ok";
    } catch (e) {
        return `${e.constructor.name}: ${e.message}`;
    }
}

const dotNet = { invokeMethodAsync: async () => { } };
console.log(JSON.stringify({
    registerOfRemoved: attempt(() => registerScrollerCallback(null, dotNet, "ProcessScrollBy", 10, 2, true)),
    cleanupOfRemoved: attempt(() => cleanup(null)),
}));
