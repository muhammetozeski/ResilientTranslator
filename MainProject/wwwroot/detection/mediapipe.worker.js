// MediaPipe's supplied UMD loader requires classic-script scope, including Annex B function semantics.
// Keep its published loader unchanged and load its ES module API dynamically from this classic worker.
const allowed=['en','tr','de','fr','es','it','ru'];
let detector;
self.onmessage=async({data})=>{
    try {
        if(data.type==='initialize') {
            const root=new URL('./',self.location);
            const {LanguageDetector}=await import(new URL('vendor/mediapipe.js',root));
            const response=await fetch(new URL('models/language_detector.tflite',root));
            if(!response.ok)throw Error(`MediaPipe model unavailable: ${response.status}`);
            detector=await LanguageDetector.createFromOptions({
                wasmLoaderPath:new URL('vendor/text_wasm_internal.js',root).href,
                wasmBinaryPath:new URL('vendor/text_wasm_internal.wasm',root).href
            },{baseOptions:{modelAssetBuffer:new Uint8Array(await response.arrayBuffer())},maxResults:1,scoreThreshold:0,categoryAllowlist:allowed});
            self.postMessage({type:'ready'});
        }else {
            if(!detector)throw Error('MediaPipe has not initialized');
            const language=detector.detect(data.text).languages[0]?.languageCode??null;
            self.postMessage({type:'result',language:allowed.includes(language)?language:null});
        }
    }catch(error){self.postMessage({type:'error',error:error?.stack??String(error)});}
};
