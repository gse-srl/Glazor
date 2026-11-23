// Drives KeyAction.razor.js against a fake element carrying the ANY_CHARS stroke a sorted grid registers for its type-ahead,
// and types a character in fields of a cell template and on the grid itself. Prints, per target, whether the key was
// cancelled, and the characters the type-ahead collected.
// Usage: node KeyActionTextFields.mjs <path to KeyAction.razor.js copied as .mjs>
import { pathToFileURL } from "node:url";
const { registerKeyboardHandler } = await import(pathToFileURL(process.argv[2]).href);

function El(tag, parent, props) {
    this.tagName = tag;
    this.parentElement = parent || null;
    Object.assign(this, props);
    const listeners = {};
    this.addEventListener = (type, fn) => { (listeners[type] ||= []).push(fn); };
    this.removeEventListener = (type, fn) => { listeners[type] = (listeners[type] || []).filter(f => f !== fn); };
    this.dispatch = (type, evt) => { for (const fn of [...(listeners[type] || [])]) fn.call(this, evt); };
}

const collected = [];
const div = new El("DIV");
registerKeyboardHandler(new El("SPAN", div), { invokeMethodAsync: async (method, chars) => { collected.push(chars); } },
    "ProcessAsync", -1, false, false, true, "div");

function press(key, target) {
    const evt = { key, shiftKey: false, ctrlKey: false, altKey: false, metaKey: false, isComposing: false,
        currentTarget: div, target, prevented: false, preventDefault() { this.prevented = true; }, stopPropagation() { } };
    div.dispatch("keydown", evt);
    return evt.prevented;
}

const prevented = {
    text: press("x", new El("INPUT", div, { type: "text" })),
    number: press("1", new El("INPUT", div, { type: "number" })),
    textarea: press("x", new El("TEXTAREA", div)),
    select: press("x", new El("SELECT", div)),
    editable: press("x", new El("SPAN", div, { isContentEditable: true })),
    grid: press("a", div),
    checkbox: press("b", new El("INPUT", div, { type: "checkbox" })),
};
await new Promise(r => setTimeout(r, 800));
console.log(JSON.stringify({ prevented, collected }));
