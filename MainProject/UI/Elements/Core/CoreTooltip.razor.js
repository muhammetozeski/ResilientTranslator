const bindings = new WeakMap();
let installed = false;
let globalPanel;
let globalAnchor;
function show(panel, anchor) {
    if (!panel || !anchor || !anchor.isConnected) return;
    if (!panel.matches(':popover-open')) panel.showPopover();
    const rect = anchor.getBoundingClientRect();
    const gap = 8;
    const width = panel.getBoundingClientRect().width;
    const height = panel.getBoundingClientRect().height;
    const left = Math.min(Math.max(gap, rect.left), Math.max(gap, innerWidth - width - gap));
    const below = rect.bottom + gap;
    const top = below + height <= innerHeight - gap ? below : Math.max(gap, rect.top - height - gap);
    panel.style.left = `${left}px`;
    panel.style.top = `${top}px`;
}
function hide(panel) { if (panel?.matches(':popover-open')) panel.hidePopover(); }
export function attach(anchor, panel) {
    install();
    detach(anchor);
    const enter = () => { hide(globalPanel); show(panel, anchor); };
    const leave = event => { if (!anchor.contains(event.relatedTarget) && !panel.contains(event.relatedTarget)) hide(panel); };
    const escape = event => { if (event.key === 'Escape') hide(panel); };
    const focusTarget = anchor.querySelector('button,input,select,textarea,a,[tabindex]') ?? anchor;
    const descriptions = new Set((focusTarget.getAttribute('aria-describedby') ?? '').split(/\s+/).filter(Boolean));
    descriptions.add(panel.id);
    focusTarget.setAttribute('aria-describedby', [...descriptions].join(' '));
    anchor.addEventListener('pointerenter', enter);
    anchor.addEventListener('focusin', enter);
    anchor.addEventListener('pointerleave', leave);
    anchor.addEventListener('focusout', leave);
    anchor.addEventListener('keydown', escape);
    panel.addEventListener('pointerleave', leave);
    bindings.set(anchor, () => {
        hide(panel);
        anchor.removeEventListener('pointerenter', enter);
        anchor.removeEventListener('focusin', enter);
        anchor.removeEventListener('pointerleave', leave);
        anchor.removeEventListener('focusout', leave);
        anchor.removeEventListener('keydown', escape);
        panel.removeEventListener('pointerleave', leave);
        const remaining = (focusTarget.getAttribute('aria-describedby') ?? '').split(/\s+/).filter(id => id && id !== panel.id);
        if (remaining.length) focusTarget.setAttribute('aria-describedby', remaining.join(' ')); else focusTarget.removeAttribute('aria-describedby');
    });
}
export function detach(anchor) { bindings.get(anchor)?.(); bindings.delete(anchor); }
export function install() {
    if (installed) return;
    installed = true;
    globalPanel = document.createElement('div');
    globalPanel.className = 'core-tooltip-panel core-tooltip-global';
    globalPanel.setAttribute('popover', 'manual');
    globalPanel.setAttribute('role', 'tooltip');
    document.body.appendChild(globalPanel);
    const find = target => target instanceof Element ? target.closest('[data-tooltip],[title],[aria-label],button,input,select,textarea,summary') : null;
    const open = event => {
        const anchor = find(event.target);
        if (!anchor || anchor.closest('[data-rich-tooltip]')) return;
        const original = anchor.getAttribute('title');
        if (original) { anchor.dataset.tooltip = original; anchor.removeAttribute('title'); }
        const label = anchor.closest('label');
        const text = anchor.dataset.tooltip || anchor.getAttribute('aria-label') || label?.querySelector(':scope > span')?.textContent?.trim();
        if (!text) return;
        globalAnchor = anchor;
        globalPanel.textContent = text;
        show(globalPanel, anchor);
    };
    document.addEventListener('pointerover', open);
    document.addEventListener('focusin', open);
    document.addEventListener('pointerout', event => {
        if (globalAnchor && !globalAnchor.contains(event.relatedTarget) && !globalPanel.contains(event.relatedTarget)) hide(globalPanel);
    });
    document.addEventListener('focusout', event => {
        if (globalAnchor && !globalAnchor.contains(event.relatedTarget)) hide(globalPanel);
    });
    document.addEventListener('keydown', event => { if (event.key === 'Escape') hide(globalPanel); });
    const hideAll = event => {
        for (const panel of document.querySelectorAll('.core-tooltip-panel:popover-open')) {
            if (!event?.target || !panel.contains(event.target)) hide(panel);
        }
    };
    document.addEventListener('scroll', hideAll, true);
    window.addEventListener('resize', hideAll);
    window.addEventListener('blur', hideAll);
    globalPanel.addEventListener('pointerleave', () => hide(globalPanel));
}
