export function registerKeyboardHandler(target, dotNetObjRef, methodName, key, shiftKey, ctrlKey, enabled, bindParentWithTag, commitFocused, altKey) {
    if (target == undefined || target.isConnected === false)  // taken out by a later render before this ran: nothing to bind
        return null;
    return new KeyboardHandler(target, dotNetObjRef, methodName, key, shiftKey, ctrlKey, enabled, bindParentWithTag, commitFocused, altKey);
}

/**
 * The element the handler goes on: the placeholder's parent, or the nearest ancestor carrying the
 * requested tag.
 */
function findParent(target, bindParentWithTag, required) {
    var parent = target != undefined ? target.parentElement : null;
    if (bindParentWithTag != undefined) {
        for (; parent != null && parent.tagName.toUpperCase() != bindParentWithTag.toUpperCase(); parent = parent.parentElement) {
        }
    }
    if (parent == null && required) {
        throw new Error(bindParentWithTag != undefined
            ? "No parent with tag: '" + bindParentWithTag + "' has been found."
            : "The element has no parent to attach the keyboard handler to.");
    }
    return parent;
}

export function cleanup(target, bindParentWithTag, key, shiftKey, ctrlKey, sendPending, altKey) {
    var parent = findParent(target, bindParentWithTag, false);
    if (parent == null)  // == matches undefined too
        return;
    var strokes = parent.$glazor$keyStrokes;
    if (strokes == undefined)  // == matches null too
        return;
    // A key registered again sends the characters still waiting: opening the search popup re-registers the grid's keys
    // while one typed meanwhile waits. A key removed for good drops them.
    var pending = parent.$glazor$keyStrokes.pendingStrokeProcessor;
    if (sendPending)
        pending?.process();
    else
        pending?.cleanup();
    var stroke = new KeyStroke(key, shiftKey == true, ctrlKey == true, altKey == true);  // just the ones used for Equals
    for (var i = 0; i < strokes.length; i++) {
        if (stroke.equals(strokes[i])) {
            strokes.splice(i, 1);
            break;
        }
    }
    if (strokes.length == 0) {
        // Detach the listener we added, by the very same function object, so a keyboard handler the
        // consuming application put on this element is left in place. Clearing the onkeydown property
        // instead would take that slot away from whatever the host had put there.
        var listener = parent.$glazor$keyListener;
        if (listener != undefined) {
            parent.removeEventListener("keydown", listener);
            parent.$glazor$keyListener = null;
        }
        parent.$glazor$keyStrokes = null;
    }
}

/** */
function KeyStroke(key, shiftKey, ctrlKey, altKey,  // part of the search key
    enabled, dotNetObjRef, methodName, commitFocused) {
    this.commitsFocused = function () {
        return commitFocused == true;
    }
    this.isEnabled = function () {
        return enabled;
    }
    this.getDotNetObjRef = function () {
        return dotNetObjRef;
    }
    this.getMethodName = function () {
        return methodName;
    }
    this.getKey = function () {
        return key;
    }
    this.getShiftKey = function () {
        return shiftKey;
    }
    this.getCtrlKey = function () {
        return ctrlKey;
    }
    this.getAltKey = function () {
        return altKey;
    }
    this.equals = function (other) {
        return key == other.getKey() &&
            shiftKey == other.getShiftKey() &&
            ctrlKey == other.getCtrlKey() &&
            altKey == other.getAltKey();
    }
    this.toString = function () {
        return "(key:" + key + ", shiftKey:" + shiftKey + ", ctrlKey:" + ctrlKey + ", altKey:" + altKey
            + ", methodName:" + methodName + ", enabled:" + enabled + ")";
    }
}

/**
 * True for a key that produces text on its own: a one-character `key` with no Ctrl, Alt or Meta held.
 * Named keys ("ArrowLeft", "Enter", "F5", "Delete", "Dead", "Process", ...) all have a longer `key`.
 * Space is excluded on purpose: it never fed the incremental search, and letting it through would
 * make the grid swallow the page-scroll key.
 * AltGr, which Chrome on Windows reports as Ctrl+Alt, is left out too: a character typed with it
 * (@, #, [ on an Italian keyboard) types into the search box once that is open.
 */
function _isTextKey(evt) {
    return evt.key != undefined && evt.key.length == 1 && evt.key != " "
        && !evt.ctrlKey && !evt.altKey && !evt.metaKey;
}

// A field the character types into: the collected characters leave it the key
function _takesText(target) {
    return target != null && (target.tagName == "TEXTAREA" || target.tagName == "SELECT" || target.isContentEditable === true
        || target.tagName == "INPUT" && !/^(checkbox|radio|button|submit|reset|range|color|file|image)$/.test(target.type));
}

/**
 * Fires change on the field the key was pressed in, as the browser does before a form's submit: the key is cancelled, so the
 * field would not commit its value, and the action that follows would reach the server before it.
 */
function _commit(target) {
    if (target != null && (target.tagName == "INPUT" || target.tagName == "TEXTAREA" || target.tagName == "SELECT"))
        target.dispatchEvent(new Event("change", { bubbles: true }));
}

/**
 * Cancelling keydown also suppresses the browser's default action and the legacy keypress that would
 * follow, so one call here is all the "we consumed it" the event needs.
 */
function _cancel(evt) {
    evt.stopPropagation();
    evt.preventDefault();
    return false;
}

/**
 * This is intended to be used as the only keyboard handler for a single dom element.
 *
 * keydown is the only event listened to. `KeyboardEvent.key` already carries the layout-resolved
 * character or the key name, so neither keypress nor the deprecated keyCode/charCode pair is needed;
 * keypress is also unreliable for anything but plain characters and is deprecated in the spec.
 */
function KeyboardHandler(target, dotNetObjRef, methodName, key, shiftKey, ctrlKey, enabled, bindParentWithTag, commitFocused, altKey) {
    var parent = findParent(target, bindParentWithTag, true);
    // Attached once per element - several KeyActions deliberately share one ancestor and
    // coordinate through $glazor$keyStrokes below, so the first registration's handler serves all of them
    // and the state it closes over (pendingStrokeProcessor) stays single.
    if (parent.$glazor$keyListener == undefined) {
        parent.addEventListener("keydown", keydown);
        parent.$glazor$keyListener = keydown;
    }
    shiftKey = shiftKey == true;
    ctrlKey = ctrlKey == true;
    altKey = altKey == true;
    {
        var stroke = new KeyStroke(key, shiftKey, ctrlKey, altKey,
            enabled, dotNetObjRef, methodName, commitFocused);
        var strokes = parent.$glazor$keyStrokes ? parent.$glazor$keyStrokes : new Array();
        var i;
        for (i = 0; i < strokes.length; i++) {
            if (stroke.equals(strokes[i])) {
                console.warn("Two KeyActions bind " + (ctrlKey ? "Ctrl+" : "") + (altKey ? "Alt+" : "") + (shiftKey ? "Shift+" : "")
                    + (key == -1 ? "any character" : JSON.stringify(key)) + " on the same element: the later one replaces the earlier");
                break;
            }
        }
        strokes[i] = stroke;
        parent.$glazor$keyStrokes = strokes;
    }

    var pendingStrokeProcessor = null;
    var pendingStrokeProcessorTO = null;

    function getKeyStroke(parent, stroke) {
        var strokes = parent.$glazor$keyStrokes ? parent.$glazor$keyStrokes : new Array();
        var i;
        for (i = 0; i < strokes.length; i++) {
            if (stroke.equals(strokes[i]))
                return strokes[i];
        }
        return null;
    }

    parent = null;

    function keydown(evt) {
        if (evt.isComposing)  // an IME is still building the character; nothing to act on yet
            return;
        var parent = evt.currentTarget;
        var evtTarget = evt.target ? evt.target : null;
        // A key pressed in a modal dialog is the dialog's, as in a desktop dialog: one bound around it does not see it
        var modal = evtTarget?.closest?.("dialog:modal");
        if (modal != null && modal !== parent && parent.contains(modal))
            return;
        // 1. A key registered by name, modifiers included: "Enter", "ArrowLeft", Ctrl+" ", ...
        //    Meta is left to the browser, and Alt but where a stroke asks for it: Alt+ArrowLeft is Back
        var stroke = evt.metaKey ? null : getKeyStroke(parent,
            new KeyStroke(evt.key, evt.shiftKey, evt.ctrlKey, evt.altKey));
        if (stroke != null && stroke.isEnabled()) {
            if (stroke.commitsFocused())
                _commit(evtTarget);
            processStroke(stroke, evtTarget, null);
            return _cancel(evt);
        }
        // 2. A plain character, collected for the ANY_CHARS stroke (registered with key -1).
        if (!_isTextKey(evt) || _takesText(evtTarget))
            return;
        stroke = getKeyStroke(parent, new KeyStroke(-1, false, false, false));
        if (stroke == null || !stroke.isEnabled())
            return;
        var ch = evt.key;
        function doIt() {
            pendingStrokeProcessor.process();
        }
        if (pendingStrokeProcessor == null) {
            pendingStrokeProcessor = new function () {
                var self = this;
                var ivChars = ch;
                // Only the batch waiting ends: one already sent or dropped is still referenced by the element, and
                // must neither send its characters again nor end the batch that follows it
                function end() {
                    if (pendingStrokeProcessor !== self)
                        return false;
                    pendingStrokeProcessor = null;
                    clearTimeout(pendingStrokeProcessorTO);
                    return true;
                }
                this.process = function () {
                    if (end())
                        processStroke(stroke, evtTarget, ivChars);
                }
                this.append = function (more) {
                    ivChars += more;
                    clearTimeout(pendingStrokeProcessorTO);
                    pendingStrokeProcessorTO = setTimeout(doIt, 500);
                }
                this.cleanup = function () {
                    end();
                }
            }
            pendingStrokeProcessorTO = setTimeout(doIt, 700);
            parent.$glazor$keyStrokes.pendingStrokeProcessor = pendingStrokeProcessor;
        }
        else {
            pendingStrokeProcessor.append(ch);
        }
        return _cancel(evt);
    }

    async function processStroke(stroke, target, cliParams) {
        if (pendingStrokeProcessor != null) {
            pendingStrokeProcessor.process();
        }
        if (stroke.isEnabled()) {
            await stroke.getDotNetObjRef().invokeMethodAsync(stroke.getMethodName(), cliParams,
                target == null ? null : (target.name ? target.name : null), target == null ? null : (target.id ? target.id : null));
        }
    }
}
