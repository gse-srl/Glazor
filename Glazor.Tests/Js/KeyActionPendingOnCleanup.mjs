// Drives KeyAction.razor.js against a fake element carrying the grid's type-ahead (ANY_CHARS) and one named key, and cleans
// the named key up while a typed character waits to be sent: registered again it sends the character at once, disposed it
// drops it, and a batch already sent is not sent again.
// Usage: node KeyActionPendingOnCleanup.mjs <path to KeyAction.razor.js copied as .mjs>
import { pathToFileURL } from "node:url";
const { registerKeyboardHandler, cleanup } = await import(pathToFileURL(process.argv[2]).href);

function El(tag, parent) {
    this.tagName = tag;
    this.parentElement = parent || null;
    const listeners = {};
    this.addEventListener = (type, fn) => { (listeners[type] ||= []).push(fn); };
    this.removeEventListener = (type, fn) => { listeners[type] = (listeners[type] || []).filter(f => f !== fn); };
    this.dispatch = (type, evt) => { for (const fn of [...(listeners[type] || [])]) fn.call(this, evt); };
}

const sent = [];
const div = new El("DIV");
registerKeyboardHandler(new El("SPAN", div), { invokeMethodAsync: async (method, chars) => { sent.push(chars); } },
    "ProcessAsync", -1, false, false, true, "div");
const arrowSpan = new El("SPAN", div);
const arrow = () => registerKeyboardHandler(arrowSpan, { invokeMethodAsync: async () => { } }, "ProcessAsync", "ArrowLeft", false, false, true, "div");
const wait = ms => new Promise(r => setTimeout(r, ms));

function press(key) {
    div.dispatch("keydown", { key, shiftKey: false, ctrlKey: false, altKey: false, metaKey: false, isComposing: false,
        currentTarget: div, target: div, preventDefault() { }, stopPropagation() { } });
}

arrow();
press("a");
cleanup(arrowSpan, "div", "ArrowLeft", false, false, true);   // registered again: sends it now
await wait(10);
const reRegistered = [...sent];

arrow();
press("b");
cleanup(arrowSpan, "div", "ArrowLeft", false, false, false);   // disposed: drops it
await wait(800);
const disposed = sent.slice(reRegistered.length);

arrow();
press("c");
await wait(800);                                                // sent by its own timer
cleanup(arrowSpan, "div", "ArrowLeft", false, false, true);     // the batch sent is still referenced: nothing again
await wait(10);
const afterTimer = sent.slice(reRegistered.length + disposed.length);

console.log(JSON.stringify({ reRegistered, disposed, afterTimer }));
