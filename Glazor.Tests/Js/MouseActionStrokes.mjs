// Drives MouseAction.razor.js against a fake DOM: a row with an enabled dblclick stroke and a data cell with a
// disabled one, as DefaultCell and DefaultRowHeading register them in row-cursor mode. Prints the outcome as JSON.
// Usage: node MouseActionStrokes.mjs <path to MouseAction.razor.js copied as .mjs>
import { pathToFileURL } from "node:url";
const { registerMouseHandler, cleanup } = await import(pathToFileURL(process.argv[2]).href);

function El(tag, parent) {
    this.tagName = tag;
    this.parentElement = parent || null;
    this._listeners = {};
    this.addEventListener = (type, fn) => { (this._listeners[type] ||= []).push(fn); };
    this.removeEventListener = (type, fn) => { this._listeners[type] = (this._listeners[type] || []).filter(f => f !== fn); };
}

function dispatch(target, type) {
    const evt = {
        type, target, srcElement: target, shiftKey: false, ctrlKey: false, clientX: 1, clientY: 2,
        stopped: false, prevented: false,
        stopPropagation() { this.stopped = true; this.stoppedAt = this.currentTarget.tagName; },
        preventDefault() { this.prevented = true; },
    };
    for (let el = target; el && !evt.stopped; el = el.parentElement) {
        evt.currentTarget = el;
        for (const fn of (el._listeners[type] || []))
            fn.call(el, evt);
    }
    return evt;
}

const calls = [];
const dotNet = (name) => ({ invokeMethodAsync: async (method) => { calls.push(name); } });
const tr = new El('TR');
const headingTd = new El('TD', tr);
const dataTd = new El('TD', tr);
registerMouseHandler(new El('SPAN', tr), dotNet('row'), 'ProcessAsync', 'dblclick', false, false, true, 'tr', true, false);
registerMouseHandler(new El('SPAN', dataTd), dotNet('cell'), 'ProcessAsync', 'dblclick', false, false, false, 'td', true, false);

const settle = () => new Promise(r => setTimeout(r, 10));
const onData = dispatch(dataTd, 'dblclick'); await settle();
const dataCalls = [...calls]; calls.length = 0;
const onHeading = dispatch(headingTd, 'dblclick'); await settle();

// Listeners go on per event type registered, and leave with the last stroke of their type: a cell with a dblclick stroke
// must carry no wheel listener, which would be a scroll-blocking one.
const listening = (el) => Object.keys(el._listeners).filter(t => el._listeners[t].length > 0).sort();
const div = new El('DIV');
const divSpan = new El('SPAN', div);
registerMouseHandler(divSpan, dotNet('div'), 'ProcessAsync', 'click', false, false, true, 'div', true, false);
registerMouseHandler(divSpan, dotNet('div'), 'ProcessAsync', 'wheel', false, false, true, 'div', true, false);
registerMouseHandler(divSpan, dotNet('div'), 'ProcessAsync', 'wheel', false, true, true, 'div', true, false);
const afterRegister = listening(div);
cleanup(divSpan, 'div', 'wheel', false, false);
const afterOneWheelLeft = listening(div);
cleanup(divSpan, 'div', 'wheel', false, true);
const afterBothWheelLeft = listening(div);
cleanup(divSpan, 'div', 'click', false, false);
const afterAllLeft = listening(div);

console.log(JSON.stringify({
    dataCell: { stoppedAt: onData.stoppedAt ?? null, calls: dataCalls },
    headingCell: { stoppedAt: onHeading.stoppedAt ?? null, calls: [...calls] },
    listeners: { row: listening(tr), cell: listening(dataTd), afterRegister, afterOneWheelLeft, afterBothWheelLeft, afterAllLeft },
}));
