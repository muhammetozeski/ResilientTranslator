import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createRequire} from 'node:module';
const require=createRequire(import.meta.url);
const research=path.resolve('Archive/OfflineTranslation/LanguageDetection');
const esbuild=require(path.join(research,'node_modules/esbuild'));
const out=path.resolve('MainProject/wwwroot/detection/vendor');
const nodeModules=path.join(research,'node_modules');
for(const [name,entry] of Object.entries({tinyld:'tinyld',tinyldLight:'tinyld/light',franc:'franc-min',cld3:'cld3-asm'})){
 const result=await esbuild.build({stdin:{contents:name==='cld3'?`import module from ${JSON.stringify(entry)}; export const loadModule=module.loadModule;`:`export * from ${JSON.stringify(entry)};`,resolveDir:research,sourcefile:name+'.mjs'},bundle:true,platform:'browser',mainFields:['browser','main','module'],format:'esm',minify:true,write:false,nodePaths:[nodeModules]});
 fs.writeFileSync(path.join(out,name+'.mjs'),result.outputFiles[0].contents);
}
await esbuild.build({entryPoints:[path.join(nodeModules,'@echogarden/fasttext-wasm/fasttext_wasm.cjs')],bundle:true,platform:'browser',mainFields:['browser','main','module'],format:'esm',minify:true,outfile:path.join(out,'fasttext.mjs'),external:['fs','path','crypto']});
esbuild.stop();
