import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import vm from 'node:vm';
const root=path.dirname(fileURLToPath(import.meta.url));
const read=name=>fs.readFileSync(path.join(root,name),'utf8');
const embeddedJson=name=>JSON.stringify(JSON.parse(read(name))).replace(/</g,'\\u003c');
const model=read('simulator.mjs').replace(/^export /gm,'');
const app=read('page.js');
// 编译嵌入后的实际脚本，避免只检查各源文件而漏掉名称冲突。
const script=model+'\nconst CATALOG='+embeddedJson('protocol-catalog.json')+';\nconst SCENARIO_DATA='+embeddedJson('scenarios.json')+';\n'+app;
new vm.Script(script,{filename:'guide-inline.js'});
let html=read('page-shell.html').replace('__CSS__',()=>read('page.css')).replace('__MODEL__',()=>model)
  .replace('__CATALOG__',()=>embeddedJson('protocol-catalog.json')).replace('__SCENARIOS__',()=>embeddedJson('scenarios.json')).replace('__APP__',()=>app);
if(/__(?:CSS|MODEL|CATALOG|SCENARIOS|APP)__/.test(html))throw new Error('未替换的模板槽');
fs.writeFileSync(path.join(root,'index.html'),html,'utf8');
console.log(JSON.stringify({output:path.join(root,'index.html'),bytes:Buffer.byteLength(html),standalone:true,externalResources:0}));
