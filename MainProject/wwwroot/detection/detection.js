const workers = new Map();
const operations = new Map();
let options = { loadTimeoutMs: 15000, detectTimeoutMs: 3000 };
export function configure(value) { options = { ...options, ...value }; }
function create(kind) {
    const classic = kind === 'Cld2' || kind === 'MediaPipe';
    const worker = new Worker(new URL(kind === 'Cld2' ? './vendor/cld-worker.js' : kind === 'MediaPipe' ? './mediapipe.worker.js' : './detector.worker.js', import.meta.url), classic ? {} : { type: 'module' });
    const slot = { worker, active: null, ready: null };
    workers.set(kind, slot);
    slot.ready = new Promise((resolve, reject) => {
        const timeout = setTimeout(() => reject(new Error(`Detector initialization timed out: ${kind}`)), options.loadTimeoutMs);
        slot.failInitialization = error => { clearTimeout(timeout); reject(error); };
        worker.onmessage = ({ data }) => {
            if (data === 'ready' || data?.type === 'ready') { clearTimeout(timeout); resolve(); return; }
            if (data?.type === 'error') {
                const error = new Error(data.error);
                if (slot.active) slot.active.reject(error); else slot.failInitialization(error);
                return;
            }
            if (slot.active) slot.active.resolve(data.language === 'un' || data.language === 'und' ? null : data.language);
        };
        worker.onerror = event => {
            const error = new Error(`${kind}: ${event.message}`);
            if (slot.active) slot.active.reject(error); else slot.failInitialization(error);
        };
        if (kind !== 'Cld2') worker.postMessage({ type: 'initialize', kind });
    });
    slot.ready.catch(() => { if (workers.get(kind) === slot) retire(kind, slot); });
    return slot;
}
function retire(kind, slot) {
    if (workers.get(kind) === slot) workers.delete(kind);
    slot.worker.terminate();
}
export async function detect(kind, id, text) {
    const previous = workers.get(kind);
    if (previous?.owner) {
        cancel(previous.owner);
        retire(kind, previous);
    }
    const slot = workers.get(kind) ?? create(kind);
    const operation = { kind, slot, reject: null, canceled: false };
    operations.set(id, operation); slot.owner = id;
    try {
        await slot.ready;
        if (operation.canceled) throw new Error('Detector operation canceled.');
        return await new Promise((resolve, reject) => {
            let finished = false;
            const finish = (callback, value) => {
                if (finished) return;
                finished = true;
                clearTimeout(timeout);
                if (slot.active === active) slot.active = null;
                callback(value);
            };
            const timeout = setTimeout(() => {
                finish(reject, new Error(`Detector operation timed out: ${kind}`));
                retire(kind, slot);
            }, options.detectTimeoutMs);
            const active = { resolve: language => finish(resolve, language), reject: error => finish(reject, error) };
            operation.reject = active.reject;
            slot.active = active;
            slot.worker.postMessage(kind === 'Cld2' ? { text } : { type: 'detect', text });
        });
    } catch (error) {
        retire(kind, slot);
        throw error;
    } finally { operations.delete(id); if (slot.owner === id) slot.owner = null; }
}
export function cancel(id) {
    const operation = operations.get(id);
    if (!operation) return;
    operation.canceled = true;
    const error = new Error('Detector operation canceled.');
    operation.reject?.(error);
    operation.slot.failInitialization?.(error);
    retire(operation.kind, operation.slot);
}
export function dispose() {
    for (const id of operations.keys()) cancel(id);
    for (const [kind, slot] of workers) retire(kind, slot);
}

