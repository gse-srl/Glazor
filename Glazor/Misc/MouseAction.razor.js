
export function registerMouseHandler(target, dotNetObjRef, methodName, type, shiftKey, ctrlKey, enabled, bindParentWithTag, cancelBubble, exclusiveClick) {
    if (target == undefined || target.isConnected === false)  // taken out by a later render before this ran: nothing to bind
        return null;
    return new MouseHandler(target, dotNetObjRef, methodName, type, shiftKey, ctrlKey, enabled, bindParentWithTag, cancelBubble, exclusiveClick);
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
            : "The element has no parent to attach the mouse handler to.");
    }
    return parent;
}

export function cleanup(target, bindParentWithTag, type, shiftKey, ctrlKey) {
    var parent = findParent(target, bindParentWithTag, false);
    if (parent == null)  // == matches undefined too
        return;
    var strokes = parent.$glazor$mouseStrokes;
    if (strokes == undefined)  // == matches null too
        return;
    var stroke = new MouseStroke(type, shiftKey, ctrlKey);  // just the ones used for Equals
    for (var i = 0; i < strokes.length; i++) {
        if (stroke.equals(strokes[i])) {
            strokes.splice(i, 1);
            break;
        }
    }
    // Detach the listener we added, by the very same function object, so a mouse handler the
    // consuming application put on this element is left in place. Clearing the onclick property
    // instead would take that slot away from whatever the host had put there. removeEventListener
    // matches on type + function + capture, so the passive flag used when adding is irrelevant.
    var listener = parent.$glazor$mouseListener;
    if (listener != undefined && !strokes.some(s => s.getType() == type)) {
        parent.removeEventListener(type, listener);
        delete parent.$glazor$mouseTypes[type];
    }
    if (strokes.length == 0) {
        parent.$glazor$mouseListener = null;
        parent.$glazor$mouseTypes = null;
        parent.$glazor$mouseStrokes = null;
    }
}

/**
 * Adds the element's listener for one event type, the first time a stroke of that type is registered: a wheel
 * listener on every cell would be a scroll-blocking one the page never needs there.
 *
 * wheel takes passive: false because _cancelBubble calls preventDefault(), which a passive listener
 * is not allowed to do. Non-passive is already the default for an ordinary element, but not when the
 * resolved ancestor is body - and saying so keeps the intent from being optimised away later.
 */
function listen(parent, evtType) {
    var types = parent.$glazor$mouseTypes || (parent.$glazor$mouseTypes = {});
    if (types[evtType])
        return;
    parent.addEventListener(evtType, parent.$glazor$mouseListener, evtType == "wheel" ? { passive: false } : undefined);
    types[evtType] = true;
}

/** */
function MouseStroke(type, shiftKey, ctrlKey,  // part of the search key
    enabled, dotNetObjRef, methodName, cancelBubble, exclusiveClick) {
    this.isEnabled = function () {
        return enabled;
    }
    this.getDotNetObjRef = function () {
        return dotNetObjRef;
    }
    this.getMethodName = function () {
        return methodName;
    }
    this.getType = function () {
        return type;
    }
    this.getShiftKey = function () {
        return shiftKey;
    }
    this.getCtrlKey = function () {
        return ctrlKey;
    }
    this.cancelBubble = function () {
        return cancelBubble;
    }
    this.exclusiveClick = function () {
        return exclusiveClick;
    }
    this.equals = function (other) {
        return type == other.getType() &&
            shiftKey == other.getShiftKey() &&
            ctrlKey == other.getCtrlKey();
    }
    this.toString = function () {
        return "(type:" + type + ", shiftKey:" + shiftKey + ", ctrlKey:" + ctrlKey
            + ", methodName:" + methodName + ", enabled:" + enabled + ", cancelBubble:" + cancelBubble + ")";
    }
}

/**
 * The handler returns this function's value, which an addEventListener listener ignores - the
 * cancelling is done by the stopPropagation/preventDefault calls here, not by the returned false.
 * That mattered when the handler was assigned to onclick, where returning false was itself the cancel.
 */
function _cancelBubble(evt) {
    evt.stopPropagation();
    evt.preventDefault();
    return false;
}

/**
 * This is intended to be used as the only mouse handler for a single dom element.
 */
function MouseHandler(target, dotNetObjRef, methodName, type, shiftKey, ctrlKey, enabled, bindParentWithTag, cancelBubble, exclusiveClick) {
    var parent = findParent(target, bindParentWithTag, true);
    // Attached once per element and event type - several MouseActions deliberately share one ancestor and
    // coordinate through $glazor$mouseStrokes below, so one listener serves them all.
    if (parent.$glazor$mouseListener == undefined)
        parent.$glazor$mouseListener = mouse;
    listen(parent, type);
    if (!shiftKey)
        shiftKey = false;
    if (!ctrlKey)
        ctrlKey = false;
    {
        var stroke = new MouseStroke(type, shiftKey, ctrlKey,
            enabled, dotNetObjRef, methodName, cancelBubble, exclusiveClick);
        var strokes = parent.$glazor$mouseStrokes ? parent.$glazor$mouseStrokes : new Array();
        var i;
        for (i = 0; i < strokes.length; i++) {
            if (stroke.equals(strokes[i]))
                break;
        }
        strokes[i] = stroke;
        parent.$glazor$mouseStrokes = strokes;
    }

    var ivPendingClick = null;

    function getMouseStroke(parent, stroke) {
        var strokes = parent.$glazor$mouseStrokes ? parent.$glazor$mouseStrokes : new Array();
        var i;
        for (i = 0; i < strokes.length; i++) {
            if (stroke.equals(strokes[i]))
                return strokes[i];
        }
        return null;
    }

    parent = null;

    function mouse(evt) {
        var parent = evt.currentTarget ? evt.currentTarget : this;
        var evtTarget = evt.target;
        var type = evt.type;
        var wheelDelta = 0;
        if (type == "wheel") {
            var delta = evt.deltaY;
            if (delta == 0)
                return;
            wheelDelta = delta;
        }
        if (!evtTarget)
            evtTarget = null;
        var stroke = getMouseStroke(parent,
            new MouseStroke(type, evt.shiftKey, evt.ctrlKey));
        if (stroke != null && stroke.isEnabled()) {   // a disabled stroke leaves the event to whoever else listens
            // Do I have to process a click or a double-click, never both ?
            if (stroke.exclusiveClick() && type == "click") {
                if (!ivPendingClick) {
                    ivPendingClick = stroke;
                    function clicked() {
                        if (ivPendingClick) {
                            // looked up again: the component may have been cleaned up or disabled while the click was held
                            var stroke = getMouseStroke(parent, ivPendingClick);
                            ivPendingClick = null;
                            if (stroke != null)
                                processStroke(stroke, evtTarget, evt.clientX, evt.clientY, wheelDelta);
                        }
                    }
                    setTimeout(clicked, 500);   // the usual OS double-click interval, which the browser does not expose
                }
            }
            else {
                ivPendingClick = null;
                processStroke(stroke, evtTarget, evt.clientX, evt.clientY, wheelDelta);
            }
            return !stroke.cancelBubble() || _cancelBubble(evt);
        }
    }

    async function processStroke(stroke, target, clientX, clientY, wheelDelta) {
        if (stroke.isEnabled()) {
            await stroke.getDotNetObjRef().invokeMethodAsync(stroke.getMethodName(),
                target == null ? null : (target.name ? target.name : null), target == null ? null : (target.id ? target.id : null),
                clientX, clientY, wheelDelta);
        }
    }
}

