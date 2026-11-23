export function registerScrollerCallback(scrollBarDiv, dotNetObjRef, methodName, rowCount, pageSize, pageIdx, enabled, isVertical = true) {
    var scroller = new Scroller(dotNetObjRef, methodName, rowCount, pageSize, pageIdx, enabled);

    // Clear layout values before measuring parent elements
    if (isVertical) {
        scrollBarDiv.style.height = "0px";
        scrollBarDiv.style.height = scrollBarDiv.parentNode.offsetHeight + "px";
    }
    else {
        scrollBarDiv.style.width = "0px";
        scrollBarDiv.style.width = scrollBarDiv.parentNode.offsetWidth + "px";
    }

    var scrollBar = scrollBarDiv.$glazor$scrollBar;
    if (!scrollBar) {
        scrollBar = new ScrollBar(scrollBarDiv, scroller, null, isVertical);
        scrollBarDiv.$glazor$scrollBar = scrollBar;
    }
    else {
        scrollBar.reset(scroller);
    }
    scrollBar.scrollTo(pageIdx);
}

export function cleanup(scrollBarDiv) {
    var scrollBar = scrollBarDiv.$glazor$scrollBar;
    if (!scrollBar)
        return;
    delete scrollBarDiv.$glazor$scrollBar;
    scrollBar.cleanup();
}

function Scroller(dotNetObjRef, methodName, rowCount, pageSize, pageIdx, enabled) {
    var self = this;
    self._lastIdx = null;

    this.getTotalSize = function () {
        return rowCount;
    };
    this.getViewableSize = function () {
        return pageSize;
    };

    this.scrollTo = function (idx) {
        // Universal index matching regardless of direction
        if (!isNaN(idx) && idx != pageIdx && self._lastIdx != idx) {
            self._lastIdx = idx;
            processScrollTo(idx);
        }
    };
    this.isEnabled = function () {
        return enabled;
    };
    async function processScrollTo(idx) {
        if (enabled) {
            await dotNetObjRef.invokeMethodAsync(methodName, idx);
        }
    }
}

// Original code by Nathan Faubion: http://n-son.com
// see: http://www.java2s.com/Code/JavaScript/GUI-Components/ScrollBar6.htm

/* 
 * interface Scroller {
 *   int getTotalHeight();
 *   int getViewableHeight();
 *   void scrollTo(int idx); 
 *   boolean isEnabled();     
 * }
 */
function ScrollBar(o, s, ev, isVertical) {
    var self = this;
    self._src = s;
    self._isVert = isVertical;

    self.reset = function (scroller) {
        self._parent = o;
        self._src = scroller;
        self.eventHandler = ev ? ev : function () { };
        // Component Mapping
        self._up = self._findComponent("scrollBarUp" /* also left */, self._parent);
        self._down = self._findComponent("scrollBarDown" /* also right */, self._parent);
        self._track = self._findComponent("scrollBarTrack", self._parent);
        self._handle = self._findComponent("scrollBarHandle", self._track);

        // Size Calculations
        if (self._isVert) {
            var hh = self._track.parentNode.offsetHeight;
            hh -= self._up ? self._up.offsetHeight : 0;
            hh -= self._down ? self._down.offsetHeight : 0;
            self._track.style.height = hh + "px";
            self._trackSize = self._track.offsetHeight;
        }
        else {
            var ww = self._track.parentNode.offsetWidth;
            ww -= self._up ? self._up.offsetWidth : 0;
            ww -= self._down ? self._down.offsetWidth : 0;
            self._track.style.width = ww + "px";
            self._trackSize = self._track.offsetWidth;
        }

        // Handle Size Calculations
        self._handle.style.top = "0px";
        self._handle.style.left = "0px";

        var calculatedHandleSize = Math.min(self._trackSize,
            Math.max(Math.min(30, self._trackSize / 2), self._trackSize * self._src.getViewableSize() /   // a short track keeps room to move
                Math.max(1, self._src.getTotalSize()))
        );

        if (self._isVert) {
            self._handle.style.height = calculatedHandleSize + "px";
            self._handleSize = self._handle.offsetHeight;
        }
        else {
            self._handle.style.width = calculatedHandleSize + "px";
            self._handleSize = self._handle.offsetWidth;
        }

        self._pos = 0;
        self._disabled = !self._src.isEnabled();
        self._ratio = (self._src.getTotalSize() - self._src.getViewableSize()) / (self._trackSize - self._handleSize);

        self._handle.ondragstart = function () { return false; };
        self._handle.onmousedown = function () { return false; };

        // Event Binding Redirection
        self._removeEvent(self._parent, "mousedown", self._scrollbarClick);
        self._addEvent(self._parent, "mousedown", self._scrollbarClick);

        self._removeEvent(self._handle, "touchstart", self._scrollbarTStart);
        self._addEvent(self._handle, "touchstart", self._scrollbarTStart);
        self._removeEvent(self._handle, "touchmove", self._scrollbarTDrag);
        self._addEvent(self._handle, "touchmove", self._scrollbarTDrag, false);
        self._removeEvent(self._handle, "touchend", self._stopTScroll);
        self._addEvent(self._handle, "touchend", self._stopTScroll, false);

        if (self._src.getTotalSize() <= self._src.getViewableSize()) {
            self._disabled = true;
            self._handle.style.visibility = "hidden";
        }
        else {
            self._disabled = self._disabled || false;
            self._handle.style.visibility = "visible";
            self._parent.style.visibility = "visible";
        }
        if (self._disabled) {
            _addClass(self._track, "disabled");
            if (self._handle) _addClass(self._handle, "disabled");
            if (self._up) _addClass(self._up, "disabled");
            if (self._down) _addClass(self._down, "disabled");
        }
        else {
            _removeClass(self._track, "disabled");
            if (self._handle) _removeClass(self._handle, "disabled");
            if (self._up) _removeClass(self._up, "disabled");
            if (self._down) _removeClass(self._down, "disabled");
        }
    };

    self.cleanup = function () {
        self._removeEvent(self._parent, "mousedown", self._scrollbarClick);
        self._removeEvent(document, "mousemove", self._scrollbarDrag, false);
        self._removeEvent(document, "mouseup", self._stopScroll, false);

        self._removeEvent(self._handle, "touchstart", self._scrollbarTStart);
        self._removeEvent(self._handle, "touchmove", self._scrollbarTDrag);
        self._removeEvent(self._handle, "touchend", self._stopTScroll);

        if (self._scrollTimer) {
            window.clearInterval(self._scrollTimer);
            self._scrollTimer = null;
        }
        self._restoreSelect();
    };

    // The page's own selectstart handler, taken over for the length of a drag and put back as it was, null included
    self._restoreSelect = function () {
        if (!self._selectSaved)
            return;
        self._selectSaved = false;
        document.onselectstart = self._selectFunc;
        self._selectFunc = null;
    };

    self._addEvent = function (o, t, f) {
        if (o.addEventListener) o.addEventListener(t, f, { passive: false });
        else if (o.attachEvent) o.attachEvent('on' + t, f);
        else o['on' + t] = f;
    };
    self._removeEvent = function (o, t, f) {
        if (o.removeEventListener) o.removeEventListener(t, f, false);
        else if (o.detachEvent) o.detachEvent('on' + t, f);
        else o['on' + t] = null;
    };
    self._findComponent = function (c, o) {
        var kids = o.childNodes;
        for (var i = 0; i < kids.length; i++) {
            if (kids[i].className && _hasClass(kids[i], c)) {
                return kids[i];
            }
        }
    };

    self._scrollbarClick = function (e) {
        if (self._disabled)
            return false;
        e = e ? e : event;
        if (e.button !== 0)
            return false;   // primary button only, as native scrollbars
        var target = e.target || e.srcElement;

        if (_hasClass(target, "scrollBarUp")) self._scrollPrev(e);
        else if (_hasClass(target, "scrollBarDown")) self._scrollNext(e);
        else if (_hasClass(target, "scrollBarTrack")) self._scrollTrack(e);
        else if (_hasClass(target, "scrollBarHandle")) self._scrollHandle(e);

        self._tempTarget = target;
        if (!self._selectSaved) {
            self._selectFunc = document.onselectstart;
            self._selectSaved = true;
        }
        document.onselectstart = function () { return false; };

        self.eventHandler(target, "mousedown");
        self._addEvent(document, "mouseup", self._stopScroll, false);
        return false;
    };

    self._scrollHandle = function (e) {
        var targetRect = e.target.getBoundingClientRect();
        var clientCoord = self._isVert ? e.clientY : e.clientX;
        var startOffset = self._isVert ? targetRect.top : targetRect.left;

        self._grabPoint = clientCoord - Math.round(startOffset);
        self._addEvent(document, "mousemove", self._scrollbarDrag, false);
    };

    self._scrollbarDrag = function (e) {
        e = e ? e : event;
        var trackRect = self._track.getBoundingClientRect();
        var clientCoord = self._isVert ? e.clientY : e.clientX;
        var trackStart = self._isVert ? trackRect.top : trackRect.left;

        var v = clientCoord - Math.round(trackStart);
        var maxPos = self._trackSize - self._handleSize;
        var targetPos = 0;

        if (v >= maxPos + self._grabPoint)
            targetPos = maxPos;
        else if (v <= self._grabPoint)
            targetPos = 0;
        else
            targetPos = v - self._grabPoint;

        if (self._isVert) {
            self._handle.style.top = targetPos + "px";
        }
        else {
            self._handle.style.left = targetPos + "px";
        }

        self._pos = targetPos;
        self._moveContent();
    };

    self._startScroll = function (step, trackClick) {
        if (self._scrollTimer)
            window.clearInterval(self._scrollTimer);   // a press whose mouseup never came
        self._tempStep = step;
        self._trackClick = trackClick;
        self.scrollBy(self._tempStep);
        // repeat only on a held press, as native scrollbars do; clearInterval stops the timeout too, they share one timer list
        self._scrollTimer = window.setTimeout(function () {
            self._scrollTimer = window.setInterval(function () {
                // a held track press stops once the handle reaches the pointer
                var t = self._trackClick;
                if (t != null && (self._tempStep > 0 ? self._pos + self._handleSize > t : self._pos <= t))
                    return;
                self.scrollBy(self._tempStep);
            }, 100);
        }, 400);
    };

    self._stopScroll = function () {
        self._removeEvent(document, "mousemove", self._scrollbarDrag, false);
        self._removeEvent(document, "mouseup", self._stopScroll, false);

        self._restoreSelect();

        if (self._scrollTimer) window.clearInterval(self._scrollTimer);
        self.eventHandler(self._tempTarget, "mouseup");
        self._moveContent();
    };

    self._scrollPrev = function (e) {
        self._startScroll(-1);
    };
    self._scrollNext = function (e) {
        self._startScroll(1);
    };

    self._scrollTrack = function (e) {
        var rect = e.target.getBoundingClientRect();
        var clientCoord = self._isVert ? e.clientY : e.clientX;
        var startOffset = self._isVert ? rect.top : rect.left;

        var curClick = clientCoord - Math.round(startOffset);
        var currentHandlePos = parseInt(self._isVert ? self._handle.style.top : self._handle.style.left) || 0;
        var targetCenter = curClick - self._handleSize / 2;

        if (targetCenter < currentHandlePos)
            self._startScroll(-self._src.getViewableSize(), curClick);
        else
            self._startScroll(self._src.getViewableSize(), curClick);
    };

    self._scroll = function (pos) {
        var maxPos = self._trackSize - self._handleSize;
        if (pos > maxPos)
            pos = maxPos;
        if (pos < 0)
            pos = 0;

        if (self._isVert) {
            self._handle.style.top = pos + "px";
        }
        else {
            self._handle.style.left = pos + "px";
        }
        self._pos = pos;
        self._moveContent();
    };

    self._moveContent = function () {
        var finalIdx = Math.round(self._pos * self._ratio);
        self._src.scrollTo(finalIdx);
    };

    self.scrollBy = function (step) {
        self._scroll((Math.round(self._pos * self._ratio) + step) / self._ratio);
    };
    self.scrollTo = function (idx) {
        if (self._src.getTotalSize() <= self._src.getViewableSize())
            return;
        self._scroll(idx / self._ratio);
    };
    self._scrollbarTStart = function (e) {
        if (self._disabled)
            return false;
        e = e ? e : event;
        var target = e.target || e.srcElement;
        if (_hasClass(target, "scrollBarUp"))
            return true;
        if (_hasClass(target, "scrollBarDown"))
            return true;
        if (_hasClass(target, "scrollBarTrack"))
            return true;
        if (_hasClass(target, "scrollBarHandle"))
            self._scrollTHandle(e);
        if (e.cancelable)
            e.preventDefault();
        return false;
    };
    self._scrollbarTDrag = function (e) {
        e = e ? e : event;
        var trackRect = self._track.getBoundingClientRect();
        var touchCoord = self._isVert ? e.touches[0].clientY : e.touches[0].clientX;
        var trackStart = self._isVert ? trackRect.top : trackRect.left;
        var v = touchCoord - Math.round(trackStart);
        var maxPos = self._trackSize - self._handleSize;
        var targetPos = 0;
        if (v >= maxPos + self._grabPoint)
            targetPos = maxPos;
        else if (v <= self._grabPoint)
            targetPos = 0;
        else
            targetPos = v - self._grabPoint;
        if (self._isVert) {
            self._handle.style.top = targetPos + "px";
        }
        else {
            self._handle.style.left = targetPos + "px";
        }
        self._pos = parseInt(self._isVert ? self._handle.style.top : self._handle.style.left);
        self._moveContent();
        if (e.cancelable)
            e.preventDefault();
        return false;
    };
    self._stopTScroll = function (e) {
        e = e ? e : event;
        self._moveContent();
        if (e.cancelable)
            e.preventDefault();
        return false;
    };
    self._scrollTHandle = function (e) {
        var targetRect = e.target.getBoundingClientRect();
        var touchCoord = e.touches[0].clientY;
        var startOffset = targetRect.top;
        if (!self._isVert) {
            touchCoord = e.touches[0].clientX;
            startOffset = targetRect.left;
        }
        self._grabPoint = touchCoord - Math.round(startOffset);
    };
    self.reset(s);
}

function _hasClass(element, clazz) {
    if (!element || !element.className)
        return false;
    return (element.className.indexOf(clazz) >= 0);
}

function _addClass(element, clazz) {
    if (!element || _hasClass(element, clazz))
        return;
    element.className = (element.className + ' ' + clazz).trim();
}

function _removeClass(element, clazz) {
    if (!element || !_hasClass(element, clazz))
        return;
    var reg = new RegExp('\\b' + clazz + '\\b', 'g');
    element.className = element.className.replace(reg, ' ').trim();
}
