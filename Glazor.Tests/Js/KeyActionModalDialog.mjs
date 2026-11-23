// Drives KeyAction.razor.js against a fake page holding a modal dialog: F2 strokes on a host panel around the dialog, on the
// dialog itself and on a div inside it, and an Enter stroke on the panel. Presses the keys from inside the dialog, from the
// panel, and from inside a dialog that is open but not modal. The event bubbles from the target up. Prints, per press, the
// handlers that ran and whether the event was cancelled.
// Usage: node KeyActionModalDialog.mjs <path to KeyAction.razor.js copied as .mjs>
import { pathToFileURL } from "node:url";
const { registerKeyboardHandler } = await import(pathToFileURL(process.argv[2]).href);

function El(tag, parent, modal) {
    this.tagName = tag;
    this.parentElement = parent || null;
    this.modal = modal === true;
    this.contains = other => { for (let el = other; el != null; el = el.parentElement) if (el === this) return true; return false; };
    this.closest = selector => {
        if (selector !== "dialog:modal")
            throw new Error("unexpected selector " + selector);
        for (let el = this; el != null; el = el.parentElement) if (el.tagName === "DIALOG" && el.modal) return el;
        return null;
    };
    const listeners = {};
    this.addEventListener = (type, fn) => { (listeners[type] ||= []).push(fn); };
    this.removeEventListener = (type, fn) => { listeners[type] = (listeners[type] || []).filter(f => f !== fn); };
    this.dispatch = (type, evt) => { for (const fn of [...(listeners[type] || [])]) fn.call(this, evt); };
}

const calls = [];
const handler = name => ({ invokeMethodAsync: async () => { calls.push(name); } });
const body = new El("BODY");
const panel = new El("DIV", body);
const dialog = new El("DIALOG", panel, true);
const content = new El("DIV", dialog);
const popup = new El("DIALOG", panel, false);
registerKeyboardHandler(new El("SPAN", panel), handler("panelF2"), "ProcessAsync", "F2", false, false, true, "div");
registerKeyboardHandler(new El("SPAN", panel), handler("panelEnter"), "ProcessAsync", "Enter", false, false, true, "div");
registerKeyboardHandler(new El("SPAN", content), handler("dialogF2"), "ProcessAsync", "F2", false, false, true, "dialog");
registerKeyboardHandler(new El("SPAN", content), handler("contentF2"), "ProcessAsync", "F2", false, false, true, "div");

async function press(key, target) {
    calls.length = 0;
    const evt = { key, shiftKey: false, ctrlKey: false, altKey: false, metaKey: false, isComposing: false,
        target, prevented: false, stopped: false,
        preventDefault() { this.prevented = true; }, stopPropagation() { this.stopped = true; } };
    for (let el = target; el != null && !evt.stopped; el = el.parentElement) {
        evt.currentTarget = el;
        el.dispatch("keydown", evt);
    }
    await new Promise(r => setTimeout(r, 1));
    return { calls: [...calls], prevented: evt.prevented };
}

const input = new El("INPUT", content);
const button = new El("BUTTON", dialog);
const field = new El("INPUT", panel);
console.log(JSON.stringify({
    f2InContent: await press("F2", input),
    f2OnDialog: await press("F2", button),
    enterInDialog: await press("Enter", button),
    f2OnPanel: await press("F2", field),
    enterOnPanel: await press("Enter", field),
    f2InPopup: await press("F2", new El("INPUT", popup)),
}));
