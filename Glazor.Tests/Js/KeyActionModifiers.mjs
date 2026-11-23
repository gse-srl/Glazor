// Drives KeyAction.razor.js against a fake element carrying the strokes a grid registers (ArrowLeft, Home, Ctrl+Space, Enter and
// Alt+Enter), and presses them with and without Alt and Meta. Prints, per key, which handler ran and whether the event was cancelled.
// Usage: node KeyActionModifiers.mjs <path to KeyAction.razor.js copied as .mjs>
import { pathToFileURL } from "node:url";
const { registerKeyboardHandler } = await import(pathToFileURL(process.argv[2]).href);

function El(tag, parent) {
    this.tagName = tag;
    this.parentElement = parent || null;
    const listeners = {};
    this.addEventListener = (type, fn) => { (listeners[type] ||= []).push(fn); };
    this.removeEventListener = (type, fn) => { listeners[type] = (listeners[type] || []).filter(f => f !== fn); };
    this.dispatch = (type, evt) => { for (const fn of [...(listeners[type] || [])]) fn.call(this, evt); };
}

const calls = [];
const div = new El("DIV");
for (const [name, key, ctrl, alt] of [["arrowLeft", "ArrowLeft", false, false], ["home", "Home", false, false], ["ctrlSpace", " ", true, false],
        ["enter", "Enter", false, false], ["altEnter", "Enter", false, true]])
    registerKeyboardHandler(new El("SPAN", div), { invokeMethodAsync: async () => { calls.push(name); } }, "ProcessAsync", key, false, ctrl, true, "div",
        false, alt);

async function press(key, mods) {
    calls.length = 0;
    const evt = { key, shiftKey: false, ctrlKey: !!mods.ctrl, altKey: !!mods.alt, metaKey: !!mods.meta, isComposing: false,
        currentTarget: div, target: div, prevented: false, preventDefault() { this.prevented = true; }, stopPropagation() { } };
    div.dispatch("keydown", evt);
    await new Promise(r => setTimeout(r, 1));
    return { handled: calls.length > 0, by: calls.join(), prevented: evt.prevented };
}

console.log(JSON.stringify({
    arrowLeft: await press("ArrowLeft", {}),
    altArrowLeft: await press("ArrowLeft", { alt: true }),
    metaHome: await press("Home", { meta: true }),
    ctrlSpace: await press(" ", { ctrl: true }),
    ctrlAltSpace: await press(" ", { ctrl: true, alt: true }),
    enter: await press("Enter", {}),
    altEnter: await press("Enter", { alt: true }),
    ctrlAltEnter: await press("Enter", { ctrl: true, alt: true }),
}));
