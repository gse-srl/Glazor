
export function registerScrollerCallback(scrollable, dotNetObjRef, methodName, pageSize, fingers, enabled) {
    if (!scrollable)   // removed by the render that passed it: the next render passes its replacement
        return;
    var scroller = new Scroller(dotNetObjRef, methodName, pageSize, enabled);
    var touchScroller = scrollable.$glazor$touchScroller;
    if (!touchScroller) {
        touchScroller = new TouchScroller(scrollable, scroller, fingers);
        scrollable.$glazor$touchScroller = touchScroller;
    }
    else {
        touchScroller.reset(scroller, fingers);
    }
}

export function cleanup(scrollable) {
    if (!scrollable)   // a reference to an element removed from the DOM arrives as null
        return;
    var ts = scrollable.$glazor$touchScroller;
    if (!ts)
        return;
    ts._removeEvent(scrollable, "touchstart", ts._scrollableTStart);
    ts._removeEvent(scrollable, "touchmove", ts._scrollableTDrag);
    ts._removeEvent(scrollable, "touchend", ts._scrollableTStop);
    delete scrollable.$glazor$touchScroller;
}

function Scroller(dotNetObjRef, methodName, pageSize, enabled) {
    this.getViewableHeight = function () {
        return pageSize;
    };
    this.scrollBy = function (dx, dy) {
        if (dx != 0 || dy != 0) {
            processScrollBy(dx, dy);
        }
    };
    this.isEnabled = function () {
        return enabled;
    };
    async function processScrollBy(dx, dy) {
        if (enabled) {
            await dotNetObjRef.invokeMethodAsync(methodName, dx, dy);
        }
    }
};

/* 
 * interface Scroller {
 *   int getViewableHeight();
 *   void scrollBy(int dx, int dy); 
 *   boolean isEnabled();     
 * }
 */
function TouchScroller(scrollable, s, touches) {
    var self = this;
    // _src == scroller
    self._src = s;
    self._touchPointY = null;
    self._touchPointX = null;

    self.reset = function (scroller, fingers) {
        //Arguments that were passed
        self._scrollable = scrollable;
        self._src = scroller;
        touches = fingers;   // re-registered after every render, so a changed Fingers parameter arrives here
        //Height and position properties
        self._disabled = !self._src.isEnabled();

        self._removeEvent(scrollable, "touchstart", self._scrollableTStart);
        self._addEvent(scrollable, "touchstart", self._scrollableTStart, true);   // cancels nothing: the browser need not wait for it
        self._removeEvent(scrollable, "touchmove", self._scrollableTDrag);
        self._removeEvent(scrollable, "touchend", self._scrollableTStop);
        if (!self._disabled) {   // a touchmove that can cancel makes the browser wait for it before every scroll of the page
            self._addEvent(scrollable, "touchmove", self._scrollableTDrag, false);
            self._addEvent(scrollable, "touchend", self._scrollableTStop, false);
        }
        // Registered again after every render, a scroll step's included: a touch in progress survives it unless now disabled
        if (self._disabled) {
            self._touchPointY = null;
            self._touchPointX = null;
        }
    };
    //
    self._addEvent = function (o, t, f, passive) {
        // Passive only where the handler cancels nothing: a preventDefault() from a passive listener is ignored with a
        // console error, and on document, body or window touch listeners are passive by default, so the flag is explicit.
        if (o.addEventListener) o.addEventListener(t, f, { passive: passive === true });
        else if (o.attachEvent) o.attachEvent('on' + t, f);
        else o['on' + t] = f;
    };
    self._removeEvent = function (o, t, f) {
        if (o.removeEventListener) o.removeEventListener(t, f, false);
        else if (o.detachEvent) o.detachEvent('on' + t, f);
        else o['on' + t] = null;
    };
    // scrollable
    self._scrollableTStart = function (e) {
        e = e ? e : event;
        if (e.touches.length != touches) {
            self._touchPointY = null;
            self._touchPointX = null;
            return true;
        }
        if (self._disabled) {
            self._touchPointY = null;
            self._touchPointX = null;
            return false;
        }
        self._touchPointY = e.touches[0].clientY;
        self._touchPointX = e.touches[0].clientX;
        // not cancelled: a cancelled touchstart gives no click, and a tap must still select the row; the moves are cancelled
        return false;
    };
    self._scrollableTDrag = function (e) {
        e = e ? e : event;
        if (self._touchPointY === null) return true;   // null means no touch in progress; 0 is a touch on the top edge
        if (e.touches.length != touches) {
            self._touchPointY = null;
            self._touchPointX = null;
            return true;
        }
        var deltaY = -(e.touches[0].clientY - self._touchPointY);
        var dy = (deltaY * self._src.getViewableHeight()) / self._scrollable.offsetHeight;
        dy = Math.round(dy) * 3;
        var deltaX = -(e.touches[0].clientX - self._touchPointX);
        var dx = (deltaX < -40) ? -1 : ((deltaX > 40) ? 1 : 0);

        if (dy != 0) self._touchPointY = e.touches[0].clientY;   // less than a row adds up with the next move
        if (dx != 0) self._touchPointX = e.touches[0].clientX;

        self._src.scrollBy(dx, dy);
        if (e.cancelable)   // once a native fling has started touchmove is no longer cancelable
            e.preventDefault();
        return false;
    }
    self._scrollableTStop = function (e) {
        e = e ? e : event;
        self._touchPointY = null;
        self._touchPointX = null;
        if (e.touches.length != touches)
            return true;
        if (e.cancelable)
            e.preventDefault();
        return false;
    };

    self.reset(s, touches);
};
