const bindings = new WeakMap();
let installed = false, globalPanel, globalAnchor, activePanel, openTimer, closeTimer;
const OPEN_DELAY = 350, CLOSE_DELAY = 120, GAP = 10;
function clearTimers() { clearTimeout(openTimer); clearTimeout(closeTimer); }
function hide(panel = activePanel) {
    if (panel?.matches(':popover-open')) panel.hidePopover();
    if (activePanel === panel) activePanel = null;
}
function dismiss() { clearTimers(); hide(); globalAnchor = null; }
function show(panel, anchor) {
    if (!anchor?.isConnected) return;
    if (activePanel !== panel) hide();
    panel.style.left = '0px'; panel.style.top = '0px';
    if (!panel.matches(':popover-open')) panel.showPopover();
    activePanel = panel;
    const rect = anchor.getBoundingClientRect(), bounds = panel.getBoundingClientRect();
    const left = Math.max(GAP, Math.min(rect.left + (rect.width - bounds.width) / 2, innerWidth - bounds.width - GAP));
    const below = rect.bottom + GAP, above = rect.top - bounds.height - GAP;
    panel.style.left = `${left}px`;
    panel.style.top = `${below + bounds.height <= innerHeight - GAP ? below : Math.max(GAP, above)}px`;
}
function schedule(panel, anchor, immediate = false) {
    clearTimers();
    if (activePanel === panel && panel.matches(':popover-open')) return;
    hide();
    openTimer = setTimeout(() => show(panel, anchor), immediate ? 0 : OPEN_DELAY);
}
function leave(panel, anchor, related) {
    if (related instanceof Node && (anchor.contains(related) || panel.contains(related))) return;
    clearTimeout(openTimer); clearTimeout(closeTimer);
    closeTimer = setTimeout(() => hide(panel), CLOSE_DELAY);
}
export function attach(anchor, panel) {
    install(); detach(anchor);
    const enter = () => schedule(panel, anchor);
    const focus = event => { if (event.target.matches(':focus-visible') && !event.target.matches('input,textarea,select')) schedule(panel, anchor); };
    const exit = event => leave(panel, anchor, event.relatedTarget);
    const retain = () => clearTimeout(closeTimer);
    const focusTarget = anchor.querySelector('button,input,select,textarea,a,[tabindex]') ?? anchor;
    const descriptions = new Set((focusTarget.getAttribute('aria-describedby') ?? '').split(/\s+/).filter(Boolean));
    descriptions.add(panel.id); focusTarget.setAttribute('aria-describedby', [...descriptions].join(' '));
    anchor.addEventListener('pointerenter', enter); anchor.addEventListener('focusin', focus);
    anchor.addEventListener('pointerleave', exit); anchor.addEventListener('focusout', exit);
    panel.addEventListener('pointerenter', retain); panel.addEventListener('pointerleave', exit);
    bindings.set(anchor, () => {
        clearTimers(); hide(panel);
        anchor.removeEventListener('pointerenter', enter); anchor.removeEventListener('focusin', focus);
        anchor.removeEventListener('pointerleave', exit); anchor.removeEventListener('focusout', exit);
        panel.removeEventListener('pointerenter', retain); panel.removeEventListener('pointerleave', exit);
        const remaining = (focusTarget.getAttribute('aria-describedby') ?? '').split(/\s+/).filter(id => id && id !== panel.id);
        if (remaining.length) focusTarget.setAttribute('aria-describedby', remaining.join(' ')); else focusTarget.removeAttribute('aria-describedby');
    });
}
export function detach(anchor) { bindings.get(anchor)?.(); bindings.delete(anchor); }
export function install() {
    if (installed) return;
    installed = true;
    globalPanel = document.createElement('div'); globalPanel.className = 'core-tooltip-panel core-tooltip-global';
    globalPanel.setAttribute('popover', 'manual'); globalPanel.setAttribute('role', 'tooltip'); document.body.appendChild(globalPanel);
    const find = target => target instanceof Element ? target.closest('[data-tooltip],[title],[aria-label],button,input,select,textarea,summary') : null;
    const open = event => {
        const anchor = find(event.target);
        if (!anchor || anchor.closest('[data-rich-tooltip]') || globalPanel.contains(anchor)) return;
        if (anchor.matches('input,textarea,select') && document.activeElement === anchor) return;
        const original = anchor.getAttribute('title');
        if (original) { anchor.dataset.tooltip = original; anchor.removeAttribute('title'); }
        const label = anchor.closest('label');
        const text = anchor.dataset.tooltip || label?.dataset.tooltip || anchor.getAttribute('aria-label') || label?.querySelector(':scope > span')?.textContent?.trim();
        if (!text || (anchor === globalAnchor && (openTimer || activePanel === globalPanel))) return;
        globalAnchor = anchor; globalPanel.textContent = text; schedule(globalPanel, anchor);
    };
    document.addEventListener('pointerover', open);
    document.addEventListener('pointerout', event => { if (globalAnchor) { leave(globalPanel, globalAnchor, event.relatedTarget); if (!(event.relatedTarget instanceof Node) || !globalAnchor.contains(event.relatedTarget)) globalAnchor = null; } });
    document.addEventListener('focusin', event => { if (event.target.matches?.(':focus-visible') && !event.target.matches('input,textarea,select')) open(event); });
    globalPanel.addEventListener('pointerenter', () => clearTimeout(closeTimer));
    globalPanel.addEventListener('pointerleave', () => { closeTimer = setTimeout(() => hide(globalPanel), CLOSE_DELAY); });
    document.addEventListener('pointerdown', dismiss, true);
    document.addEventListener('keydown', dismiss, true);
    document.addEventListener('input', dismiss, true);
    document.addEventListener('scroll', dismiss, true);
    window.addEventListener('resize', dismiss); window.addEventListener('blur', dismiss);
}
