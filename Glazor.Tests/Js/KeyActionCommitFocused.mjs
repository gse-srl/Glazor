// Drives KeyAction.razor.js against a fake container carrying an Enter stroke with commitFocused and an Escape stroke without,
// as the grid's post and cancel keys do, and presses each with the focus on an input, a select and a button. Prints, per
// press, the events in the order they happened: "change" when the target received one, then the handler that ran.
// Usage: node KeyActionCommitFocused.mjs <path to KeyAction.razor.js copied as .mjs>
import { pathToFileURL } from "node:url";
const { registerKeyboardHandler } = await import(pathToFileURL(process.argv[2]).href);

const order = [];

function El(tag, parent) {
    this.tagName = tag;
    this.parentElement = parent || null;
    this.matches = selector => selector.split(",").map(s => s.trim()).some(s => s === tag.toLowerCase());
    const listeners = {};
    this.addEventListener = (type, fn) => { (listeners[type] ||= []).push(fn); };
    this.removeEventListener = (type, fn) => { listeners[type] = (listeners[type] || []).filter(f => f !== fn); };
    this.dispatch = (type, evt) => { for (const fn of [...(listeners[type] || [])]) fn.call(this, evt); };
    this.dispatchEvent = evt => { order.push(evt.type); return true; };
}

const handler = name => ({ invokeMethodAsync: async () => { order.push(name); } });
const div = new El("DIV");
registerKeyboardHandler(new El("SPAN", div), handler("post"), "ProcessAsync", "Enter", false, false, true, "div", true);
registerKeyboardHandler(new El("SPAN", div), handler("cancel"), "ProcessAsync", "Escape", false, false, true, "div");

async function press(key, target) {
    order.length = 0;
    const evt = { key, shiftKey: false, ctrlKey: false, altKey: false, metaKey: false, isComposing: false,
        target, stopped: false, preventDefault() { }, stopPropagation() { this.stopped = true; } };
    for (let el = target; el != null && !evt.stopped; el = el.parentElement) {
        evt.currentTarget = el;
        el.dispatch("keydown", evt);
    }
    await new Promise(r => setTimeout(r, 1));
    return [...order];
}

console.log(JSON.stringify({
    enterOnInput: await press("Enter", new El("INPUT", div)),
    enterOnSelect: await press("Enter", new El("SELECT", div)),
    enterOnButton: await press("Enter", new El("BUTTON", div)),
    escapeOnInput: await press("Escape", new El("INPUT", div)),
}));
