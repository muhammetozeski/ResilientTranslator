const splitters = new WeakMap();

/** Converts a raw percentage into a clamped, softly centered share. */
export function normalize(percentage, snapRange) {
    const bounded = Math.max(10, Math.min(90, percentage));
    return Math.abs(bounded - 50) <= snapRange / 2 ? 50 : Math.round(bounded * 100) / 100;
}

/** Installs pointer ownership once and reapplies persisted proportions outside active gestures. */
export function attach(element, reference, percentage, snapRange) {
    let state = splitters.get(element);
    if (!state) {
        const area = element.closest('.editors');
        state = { reference, snapRange, pointer: null, percentage };
        state.apply = value => {
            state.percentage = value;
            area.style.setProperty('--input-share', `${value}fr`);
            area.style.setProperty('--output-share', `${100 - value}fr`);
            element.setAttribute('aria-valuenow', String(value));
            element.classList.toggle('snapped', value === 50);
        };
        state.save = () => state.reference.invokeMethodAsync('SaveShare', state.percentage).catch(error => console.error('Splitter save failed', error));
        state.down = event => {
            if (event.button !== 0) return;
            event.preventDefault();
            state.pointer = event.pointerId;
            element.setPointerCapture(event.pointerId);
            element.classList.add('dragging');
        };
        state.move = event => {
            if (state.pointer !== event.pointerId) return;
            if (!(event.buttons & 1)) { state.finish(); return; }
            const bounds = area.getBoundingClientRect();
            const gripWidth = element.getBoundingClientRect().width;
            state.apply(normalize((event.clientX - bounds.left - gripWidth / 2) / (bounds.width - gripWidth) * 100, state.snapRange));
        };
        state.finish = () => {
            if (state.pointer === null) return;
            const pointer = state.pointer;
            state.pointer = null;
            if (element.hasPointerCapture(pointer)) element.releasePointerCapture(pointer);
            element.classList.remove('dragging');
            state.save();
        };
        state.key = event => {
            const delta = event.key === 'ArrowLeft' ? -1 : event.key === 'ArrowRight' ? 1 : 0;
            if (!delta && event.key !== 'Home') return;
            event.preventDefault();
            // Keyboard steps remain precise; Home centers without a mouse gesture.
            state.apply(event.key === 'Home' ? 50 : Math.max(10, Math.min(90, state.percentage + delta)));
            state.save();
        };
        element.addEventListener('pointerdown', state.down);
        element.addEventListener('pointermove', state.move);
        for (const name of ['pointerup','pointercancel','lostpointercapture']) element.addEventListener(name, state.finish);
        element.addEventListener('keydown', state.key);
        window.addEventListener('blur', state.finish);
        splitters.set(element, state);
    }
    state.reference = reference;
    state.snapRange = snapRange;
    if (state.pointer === null) state.apply(percentage);
}

/** Removes all event handlers when the owning split layout is disposed. */
export function dispose(element) {
    const state = splitters.get(element);
    if (!state) return;
    element.removeEventListener('pointerdown', state.down);
    element.removeEventListener('pointermove', state.move);
    for (const name of ['pointerup','pointercancel','lostpointercapture']) element.removeEventListener(name, state.finish);
    element.removeEventListener('keydown', state.key);
    window.removeEventListener('blur', state.finish);
    splitters.delete(element);
}
