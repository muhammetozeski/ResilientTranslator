const allowed = ['en', 'tr', 'de', 'fr', 'es', 'it', 'ru'];
const iso = { eng: 'en', tur: 'tr', deu: 'de', fra: 'fr', spa: 'es', ita: 'it', rus: 'ru', und: 'und' };
let detect;
async function initialize(kind) {
    if (kind === 'MediaPipe') {
        const { LanguageDetector } = await import('./vendor/mediapipe.mjs');
        const detector = await LanguageDetector.createFromOptions({
            wasmLoaderPath: new URL('./vendor/text_wasm_internal.js', import.meta.url).href,
            wasmBinaryPath: new URL('./vendor/text_wasm_internal.wasm', import.meta.url).href
        }, {
            baseOptions: { modelAssetBuffer: new Uint8Array(await checkedFetch('./models/language_detector.tflite')) },
            maxResults: 1, scoreThreshold: 0, categoryAllowlist: allowed
        });
        detect = text => detector.detect(text).languages[0]?.languageCode ?? null;
    } else if (kind === 'FastText') {
        const { default: createRuntime } = await import('./vendor/fasttext.mjs');
        const runtime = await createRuntime({ wasmBinary: new Uint8Array(await checkedFetch('./vendor/fasttext_wasm.wasm')) });
        runtime.FS.writeFile('/language-model.ftz', new Uint8Array(await checkedFetch('./models/lid.176.ftz')));
        const model = new runtime.FastText();
        model.loadModel('/language-model.ftz');
        detect = text => {
            const results = model.predict(text.replace(/[\r\n]+/g, ' '), 176, 0);
            try {
                for (let i = 0; i < results.size(); i++) {
                    const language = results.get(i)[1].replace('__label__', '');
                    if (allowed.includes(language)) return language;
                }
                return null;
            } finally { results.delete(); }
        };
    } else if (kind === 'TinyLd' || kind === 'TinyLdLight') {
        const module = await import(kind === 'TinyLd' ? './vendor/tinyld.mjs' : './vendor/tinyldLight.mjs');
        detect = text => module.detect(text, { only: allowed }) || null;
    } else if (kind === 'Franc') {
        const { franc } = await import('./vendor/franc.mjs');
        detect = text => iso[franc(text, { minLength: 0, only: Object.keys(iso).filter(code => code !== 'und') })] ?? null;
    } else if (kind === 'Cld3') {
        const { loadModule } = await import('./vendor/cld3.mjs');
        const factory = await loadModule();
        const detector = factory.create(0, 20000);
        detect = text => detector.findLanguage(text).language;
    } else throw new Error(`Unknown detector: ${kind}`);
}
async function checkedFetch(file) {
    const response = await fetch(new URL(file, import.meta.url));
    if (!response.ok) throw new Error(`Detector asset failed: ${file}; HTTP ${response.status}`);
    return response.arrayBuffer();
}
self.onmessage = async ({ data }) => {
    try {
        if (data.type === 'initialize') {
            await initialize(data.kind);
            self.postMessage({ type: 'ready' });
        } else {
            if (!detect) throw new Error('Detector has not initialized.');
            const language = detect(data.text);
            self.postMessage({ type: 'result', language: allowed.includes(language) ? language : null });
        }
    } catch (error) {
        self.postMessage({ type: 'error', error: error?.stack ?? String(error) });
    }
};
