// Drives KeyAction.razor.js against a fake dialog content carrying an Enter stroke, as the layout and filter dialogs do, and
// presses Enter with the focus on the content, an input, a button, a link, and a button carrying its own Enter stroke, as
// the dialogs' Cancel does. The event bubbles from the target up. Prints, per target, the handlers that ran and whether the
// event was cancelled.
// Usage: node KeyActionEnterOnButton.mjs <path to KeyAction.razor.js copied as .mjs>
import { pathToFileURL } from "node:url";
const { registerKeyboardHandler } = await import(pathToFileURL(process.argv[2]).href);

function El(tag, parent, href) {
    this.tagName = tag;
    this.parentElement = parent || null;
    this.matches = selector => selector.split(",").map(s => s.trim())
        .some(s => s === tag.toLowerCase() || (s === "a[href]" && tag === "A" && href !== undefined));
    const listeners = {};
    this.addEventListener = (type, fn) => { (listeners[type] ||= []).push(fn); };
    this.removeEventListener = (type, fn) => { listeners[type] = (listeners[type] || []).filter(f => f !== fn); };
    this.dispatch = (type, evt) => { for (const fn of [...(listeners[type] || [])]) fn.call(this, evt); };
}

const calls = [];
const handler = name => ({ invokeMethodAsync: async () => { calls.push(name); } });
const div = new El("DIV");
registerKeyboardHandler(new El("SPAN", div), handler("content"), "ProcessAsync", "Enter", false, false, true, "div");
const cancel = new El("BUTTON", div);
registerKeyboardHandler(new El("SPAN", cancel), handler("cancel"), "ProcessAsync", "Enter", false, false, true, null);

async function press(target) {
    calls.length = 0;
    const evt = { key: "Enter", shiftKey: false, ctrlKey: false, altKey: false, metaKey: false, isComposing: false,
        target, prevented: false, stopped: false,
        preventDefault() { this.prevented = true; }, stopPropagation() { this.stopped = true; } };
    for (let el = target; el != null && !evt.stopped; el = el.parentElement) {
        evt.currentTarget = el;
        el.dispatch("keydown", evt);
    }
    await new Promise(r => setTimeout(r, 1));
    return { calls: [...calls], prevented: evt.prevented };
}

console.log(JSON.stringify({
    content: await press(div),
    input: await press(new El("INPUT", div)),
    button: await press(new El("BUTTON", div)),
    link: await press(new El("A", div, "#")),
    ownEnter: await press(cancel),
}));
