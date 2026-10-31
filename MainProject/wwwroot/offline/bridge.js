const root = new URL('./', import.meta.url);
let options = {}, callbacks, manifest, warmJob;
let rpcNumber = 0;
const jobs = new Map();
const pools = new Map();
const warmText = { en: 'Hello, how are you?', tr: 'Merhaba, nasılsın?', de: 'Hallo, wie geht es dir?', fr: 'Bonjour, comment allez-vous ?', es: 'Hola, ¿cómo estás?', it: 'Ciao, come stai?', ru: 'Привет, как дела?' };

function report(level, message) {
    callbacks?.invokeMethodAsync('Report', level, message).catch(error => console.error('Offline logging failed', error));
}

export async function initialize(configuration, receiver) {
    callbacks = receiver;
    await configure(configuration);
    const response = await fetch(new URL('models.json', root));
    if (!response.ok) throw new Error(`Offline model manifest could not be read: ${response.status}`);
    manifest = await response.json();
}

export function configure(configuration) {
    const changed = JSON.stringify(configuration) !== JSON.stringify(options);
    options = configuration;
    if (!changed) return;
    if (warmJob) { abort(warmJob, 'Engine settings changed'); warmJob = null; }
    for (const job of jobs.values()) abort(job, 'Engine settings changed');
    for (const pool of pools.values()) for (const slot of pool.slots) retire(slot, new Error('Engine settings changed'));
    pools.clear();
    pools.set('Bergamot', { engine: 'Bergamot', capacity: configuration.bergamotWorkers, slots: [], listeners: new Set() });
    pools.set('Toz', { engine: 'Toz', capacity: configuration.tozWorkers, slots: [], listeners: new Set() });
    report('information', 'Offline worker configuration applied');
}

export function start(number) {
    if (warmJob) { abort(warmJob, 'Warmup superseded by user command'); warmJob = null; }
    if (jobs.has(number)) throw new Error('Duplicate offline invocation');
    jobs.set(number, { number, controller: new AbortController(), slots: new Set() });
}

export function cancel(number) {
    const job = jobs.get(number);
    if (!job) return;
    abort(job, 'Offline invocation superseded');
    jobs.delete(number);
}

function abort(job, reason) {
    job.controller.abort(new DOMException(reason, 'AbortError'));
    for (const slot of job.slots) {
        if (slot.owner === job) {
            retire(slot, job.controller.signal.reason);
            slot.owner = null;
            // Start a replacement event listener now; expensive model loading remains lazy and cancellable.
            createWorker(slot);
            notify(slot.pool);
        }
    }
    job.slots.clear();
}

function notify(pool) { for (const listener of [...pool.listeners]) listener(); }

function retire(slot, error) {
    slot.worker?.terminate();
    slot.worker = null;
    slot.initialized = false;
    slot.models.clear();
    for (const pending of slot.pending.values()) pending.reject(error);
    slot.pending.clear();
}

function createWorker(slot) {
    const url = slot.pool.engine === 'Bergamot' ? 'vendor/bergamot/translator-worker.js' : 'toz-worker.js';
    const worker = new Worker(new URL(url, root));
    slot.worker = worker;
    worker.onmessage = ({ data }) => {
        if (slot.worker !== worker) return;
        if (data.log) { report(data.log.level, `${slot.pool.engine}: ${data.log.message}`); return; }
        const pending = slot.pending.get(data.id);
        if (!pending) return;
        slot.pending.delete(data.id);
        if (data.error) {
            const error = new Error(data.error.message || String(data.error));
            error.stack = data.error.stack || error.stack;
            pending.reject(error);
        } else pending.resolve(data.result);
    };
    worker.onerror = event => {
        if (slot.worker !== worker) return;
        report('error', `${slot.pool.engine} worker failed: ${event.message}`);
        retire(slot, new Error(event.message || 'Offline worker failed'));
    };
}

function rpc(slot, name, args, signal, timeoutMs) {
    signal.throwIfAborted();
    if (!slot.worker) createWorker(slot);
    const id = ++rpcNumber;
    return new Promise((resolve, reject) => {
        let timer;
        const finish = callback => value => {
            clearTimeout(timer);
            signal.removeEventListener('abort', canceled);
            callback(value);
        };
        const canceled = () => {
            clearTimeout(timer);
            signal.removeEventListener('abort', canceled);
            slot.pending.delete(id);
            reject(signal.reason);
        };
        signal.addEventListener('abort', canceled, { once: true });
        timer = setTimeout(() => {
            const error = new Error(`${slot.pool.engine} ${name} exceeded ${timeoutMs} ms`);
            retire(slot, error);
        }, timeoutMs);
        slot.pending.set(id, { resolve: finish(resolve), reject: finish(reject) });
        slot.worker.postMessage({ id, name, args });
    });
}

async function acquire(engine, job) {
    const pool = pools.get(engine);
    if (!pool) throw new Error(`Unknown offline engine: ${engine}`);
    const signal = job.controller.signal;
    while (true) {
        signal.throwIfAborted();
        let slot = pool.slots.find(candidate => !candidate.owner);
        if (!slot && pool.slots.length < pool.capacity) {
            slot = { pool, owner: null, worker: null, initialized: false, models: new Set(), pending: new Map() };
            pool.slots.push(slot);
        }
        if (slot) { slot.owner = job; job.slots.add(slot); return slot; }
        // Event-driven availability; no polling or CPU loop while a worker is occupied.
        await new Promise((resolve, reject) => {
            const available = () => { cleanup(); resolve(); };
            const canceled = () => { cleanup(); reject(signal.reason); };
            const cleanup = () => { pool.listeners.delete(available); signal.removeEventListener('abort', canceled); };
            pool.listeners.add(available);
            signal.addEventListener('abort', canceled, { once: true });
        });
    }
}

async function useWorker(engine, job, operation) {
    const slot = await acquire(engine, job);
    try { return await operation(slot); }
    catch (error) { if (slot.owner === job) retire(slot, error); throw error; }
    finally {
        job.slots.delete(slot);
        if (slot.owner === job) { slot.owner = null; notify(slot.pool); }
    }
}

async function readAsset(file, signal) {
    const response = await fetch(new URL(file.path, root), { signal });
    if (!response.ok) throw new Error(`Missing offline model asset: ${file.path}; status=${response.status}`);
    const bytes = await response.arrayBuffer();
    if (bytes.byteLength !== file.bytes) throw new Error(`Offline model size mismatch: ${file.path}`);
    // Installation verifies SHA-256. Avoid hashing hundreds of MB again for every worker replacement.
    return bytes;
}

async function prepareBergamot(slot, pairs, job) {
    const signal = job.controller.signal;
    const required = pairs.map(pair => `${pair.from}-${pair.to}`);
    const capacity = Math.max(pairs.length, options.bergamotCachedDirections || 2);
    const additions = required.filter(key => !slot.models.has(key)).length;
    if (slot.models.size + additions > capacity) {
        report('information', 'Bergamot direction cache replaced before the worker memory budget is exceeded');
        retire(slot, new Error('Bergamot direction cache replaced'));
    }
    if (!slot.initialized) {
        report('information', 'Bergamot runtime loading');
        await rpc(slot, 'initialize', [{ cacheSize: 0, useNativeIntGemm: false }], signal, options.modelLoadTimeoutMs);
        slot.initialized = true;
    }
    for (const pair of pairs) {
        const key = `${pair.from}-${pair.to}`;
        if (slot.models.has(key)) continue;
        const files = manifest[key];
        if (!files) throw new Error(`No installed Bergamot model for ${key}`);
        report('information', `Bergamot model loading: ${key}`);
        const [model, shortlist, vocab] = await Promise.all([
            readAsset(files.model, signal), readAsset(files.shortlist, signal), readAsset(files.vocab, signal)
        ]);
        await rpc(slot, 'loadTranslationModel', [pair, { model, shortlist, vocabs: [vocab], config: { 'beam-size': options.bergamotBeam, 'gemm-precision': 'int8shiftAll' } }], signal, options.modelLoadTimeoutMs);
        slot.models.add(key);
        report('information', `Bergamot model ready: ${key}`);
    }
}

function bergamotPairs(source, target) {
    return source === 'en' || target === 'en'
        ? [{ from: source, to: target }]
        : [{ from: source, to: 'en' }, { from: 'en', to: target }];
}

async function bergamot(text, source, target, job, warmOnly = false) {
    if (source === target) return text;
    return useWorker('Bergamot', job, async slot => {
        const pairs = bergamotPairs(source, target);
        await prepareBergamot(slot, pairs, job);
        const result = await rpc(slot, 'translate', [{ models: pairs, texts: [{ text: warmOnly ? warmText[source] : text, html: false, qualityScores: false }] }], job.controller.signal, options.translationTimeoutMs);
        return warmOnly ? '' : result[0].target.text;
    });
}

async function toz(text, source, target, job, warmOnly = false) {
    if (source !== 'tr' && target !== 'tr') throw new Error('Töz is available for Turkish-containing language pairs only');
    let intermediate = text;
    if (source !== 'tr' && source !== 'en') intermediate = await bergamot(text, source, 'en', job, warmOnly);
    const translated = await useWorker('Toz', job, async slot => {
        if (!slot.initialized) {
            report('information', 'Töz models loading');
            await rpc(slot, 'initialize', [{ root: root.href, threads: options.tozThreads, beam: options.tozBeam }], job.controller.signal, options.modelLoadTimeoutMs);
            slot.initialized = true;
            report('information', 'Töz models ready');
        }
        return rpc(slot, 'translate', [warmOnly ? warmText[source === 'tr' ? 'tr' : 'en'] : intermediate, source === 'tr' ? 'en' : 'tr'], job.controller.signal, options.translationTimeoutMs);
    });
    if (target !== 'tr' && target !== 'en') return bergamot(translated, 'en', target, job, warmOnly);
    return translated;
}

export async function translate(number, engine, text, source, target) {
    const job = jobs.get(number);
    if (!job) throw new DOMException('Offline invocation canceled before dispatch', 'AbortError');
    job.controller.signal.throwIfAborted();
    try {
        const result = engine === 'Toz' ? await toz(text, source, target, job) : await bergamot(text, source, target, job);
        job.controller.signal.throwIfAborted();
        if (!result?.trim()) throw new Error(`${engine} returned an empty translation`);
        return result;
    } finally { jobs.delete(number); }
}

export async function warm(number, source, target) {
    if (!manifest) return;
    if (source === 'auto' || !source) source = 'en';
    if (target === 'auto' || !target || source === target) target = source === 'tr' ? 'en' : 'tr';
    if (warmJob) abort(warmJob, 'Warmup direction changed');
    const job = warmJob = jobs.get(number);
    if (!job) throw new DOMException('Warmup canceled before dispatch', 'AbortError');
    try {
        await bergamot('', source, target, job, true);
        if (source === 'tr' || target === 'tr') await toz('', source, target, job, true);
    } catch (error) {
        if (!job.controller.signal.aborted) { report('error', `Offline warmup failed: ${error.stack || error}`); throw error; }
    } finally { if (warmJob === job) warmJob = null; jobs.delete(number); }
}
