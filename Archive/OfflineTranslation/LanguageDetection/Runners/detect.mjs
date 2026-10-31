import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import {createRequire} from 'node:module';
import {performance} from 'node:perf_hooks';
const require=createRequire(import.meta.url);
const root=path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/,'$1')),'..');
const cases=JSON.parse(fs.readFileSync(path.join(root,'Corpus/cases.json'),'utf8'));
const allowed=['en','tr','de','fr','es','it','ru'];
const iso={eng:'en',tur:'tr',deu:'de',fra:'fr',spa:'es',ita:'it',rus:'ru',und:'und'};
const mode=process.argv[2];
const start=performance.now();
let detect;
if(mode==='cld2'){
 const folder=path.join(root,'Sources/Firefox');
 let last; let signalReady; const ready=new Promise(resolve=>signalReady=resolve);
 class LocalRequest { open(method,url){this.url=url;} send(){const bytes=fs.readFileSync(this.url);this.response=bytes.buffer.slice(bytes.byteOffset,bytes.byteOffset+bytes.byteLength);this.status=200;setTimeout(()=>this.onload(),0);} }
 const ctx={console,setTimeout,clearTimeout,onmessage:null,importScripts(){},XMLHttpRequest:LocalRequest,
 Module:{ENVIRONMENT:'WORKER',locateFile:n=>path.join(folder,n)},postMessage:x=>{last=x;if(x==='ready')signalReady();}};
 vm.createContext(ctx);
 vm.runInContext(fs.readFileSync(path.join(folder,'cld-worker.js'),'utf8'),ctx,{filename:'cld-worker.js'});
 await ready;
 detect=text=>{ctx.onmessage({data:{text}});return last.language;};
}else if(mode==='cld3'){
 const {loadModule}=require('cld3-asm');
 const factory=await loadModule();
 const detector=factory.create(0,1000);
 detect=text=>detector.findLanguage(text).language;
}else if(mode.startsWith('tinyld')){
 const mod=require(mode.includes('light')?'tinyld/light':'tinyld');
 detect=mode.endsWith('-7')?text=>mod.detect(text,{only:allowed}):text=>mod.detect(text);
}else if(mode.startsWith('franc')){
 const {franc}=await import('franc-min');
 const opts={minLength:0,...(mode.endsWith('-7')?{only:Object.keys(iso).filter(x=>x!=='und')}:{})};
 detect=text=>{const result=franc(text,opts);return iso[result]??result;};
}else throw Error(mode);
const loadMs=performance.now()-start;
console.log(JSON.stringify({mode,loadMs,smoke:detect('Bu cümle Türkçe olarak yazılmıştır.')}));
for(let i=0;i<3;i++)for(const c of cases)detect(c.text);
const rows=cases.map(c=>({...c,predicted:detect(c.text),samples:[]}));
for(let repeat=0;repeat<15;repeat++)for(const row of rows){const t=performance.now();detect(row.text);row.samples.push(performance.now()-t);}
for(const row of rows){row.samples.sort((a,b)=>a-b);row.ms=row.samples[7];delete row.samples;row.correct=row.source===row.predicted;}
const dependencyFiles=Object.keys(require.cache).filter(x=>x.startsWith(path.join(root,'node_modules')));
const result={mode,loadMs,dependencyFiles,loadedCommonJsBytes:dependencyFiles.reduce((sum,x)=>sum+fs.statSync(x).size,0),rows};
fs.writeFileSync(path.join(root,`Results/${mode}.json`),JSON.stringify(result,null,2));
console.log(JSON.stringify({mode,correct:rows.filter(x=>x.correct).length,total:rows.length,medianMs:rows.map(x=>x.ms).sort((a,b)=>a-b)[Math.floor(rows.length/2)]}));
