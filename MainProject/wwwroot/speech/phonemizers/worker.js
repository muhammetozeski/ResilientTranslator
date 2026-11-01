importScripts('../../offline/vendor/toz/ort.wasm.min.js', 'misaki.js', 'fyls.js');
ort.env.wasm.wasmPaths = new URL('../../offline/vendor/toz/', self.location).href;
ort.env.wasm.numThreads = 1;
ort.env.wasm.proxy = false;
const speechTensor = values => new ort.Tensor('int64', BigInt64Array.from(values, BigInt), [1, values.length]);
async function speechJson(url) { const response=await fetch(url);if(!response.ok)throw Error(`Pronunciation data unavailable: ${url}`);return response.json(); }
async function speechSession(url) {const response=await fetch(url);if(!response.ok)throw Error(`Pronunciation model unavailable: ${url}`);return ort.InferenceSession.create(new Uint8Array(await response.arrayBuffer()),{executionProviders:['wasm']});}
let engine;
self.onmessage=async({data})=>{
    try {
        if(!engine){engine=data.engine==='Misaki'?new MisakiNeural():data.engine==='Fyls'?new FylsPronunciation():null;if(!engine)throw Error('Unknown pronunciation engine');await engine.load(new URL('./',self.location));}
        const result=data.engine==='Misaki'?await engine.word(data.text):await engine.sentence(data.text);
        self.postMessage({result});
    }catch(error){self.postMessage({error:{message:error.message,stack:error.stack}});}
};
