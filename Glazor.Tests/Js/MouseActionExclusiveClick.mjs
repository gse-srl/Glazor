// Drives MouseAction.razor.js against a fake element carrying an exclusive click stroke and a double-click stroke,
// with real timing: a slow double-click whose clicks are 300 ms apart, and a single click. Prints the callbacks as JSON.
// Usage: node MouseActionExclusiveClick.mjs <path to MouseAction.razor.js copied as .mjs>
import { pathToFileURL } from "node:url";
const { registerMouseHandler, cleanup } = await import(pathToFileURL(process.argv[2]).href);

function El(tag, parent) {
    this.tagName = tag;
    this.parentElement = parent || null;
    const listeners = {};
    this.addEventListener = (type, fn) => { (listeners[type] ||= []).push(fn); };
    this.removeEventListener = (type, fn) => { listeners[type] = (listeners[type] || []).filter(f => f !== fn); };
    this.dispatch = (type) => {
        const evt = { type, target: this, srcElement: this, currentTarget: this, shiftKey: false, ctrlKey: false, clientX: 1, clientY: 2,
            stopPropagation() { }, preventDefault() { } };
        for (const fn of [...(listeners[type] || [])]) fn.call(this, evt);
    };
}

const sleep = ms => new Promise(r => setTimeout(r, ms));
const calls = [];
const td = new El("TD");
registerMouseHandler(new El("SPAN", td), { invokeMethodAsync: async () => { calls.push("click"); } }, "ProcessAsync", "click", false, false, true, "td", true, true);
registerMouseHandler(new El("SPAN", td), { invokeMethodAsync: async () => { calls.push("dblclick"); } }, "ProcessAsync", "dblclick", false, false, true, "td", true, false);

td.dispatch("click");
await sleep(300);
td.dispatch("click");
td.dispatch("dblclick");   // the browser sends it with the second click, within the OS interval
await sleep(700);
const slowDoubleClick = [...calls];
calls.length = 0;
td.dispatch("click");
await sleep(700);
const singleClick = [...calls];
calls.length = 0;

// A click held back, then the component cleaned up, or disabled, before the window passes: nothing reaches .NET
const clickSpan = new El("SPAN", td);
td.dispatch("click");
cleanup(clickSpan, "td", "click", false, false);
await sleep(700);
const afterCleanup = [...calls];
calls.length = 0;
registerMouseHandler(clickSpan, { invokeMethodAsync: async () => { calls.push("click"); } }, "ProcessAsync", "click", false, false, true, "td", true, true);
td.dispatch("click");
registerMouseHandler(clickSpan, { invokeMethodAsync: async () => { calls.push("click"); } }, "ProcessAsync", "click", false, false, false, "td", true, true);
await sleep(700);
console.log(JSON.stringify({ slowDoubleClick, singleClick, afterCleanup, afterDisable: [...calls] }));
