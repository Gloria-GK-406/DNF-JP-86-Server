import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {spawnSync} from 'node:child_process';
const root=path.dirname(fileURLToPath(import.meta.url));
const artifacts=path.resolve(root,'../../../artifacts/joust-flow-guide-preview-20261006');
const verification=path.join(root,'verification');
fs.mkdirSync(artifacts,{recursive:true});fs.mkdirSync(verification,{recursive:true});
const chrome='C:/Program Files/Google/Chrome/Application/chrome.exe';
if(!fs.existsSync(chrome))throw new Error('本机Chrome不存在；不下载替代浏览器');
const profile=fs.mkdtempSync(path.join(artifacts,'chrome-profile-'));
const reports=[];
// Windows headless Chrome极窄窗口的截图与viewport可能不一致，使用实际可验证的窄屏。
for(const [name,width,height] of [['desktop',1440,1200],['compact',680,1000]]) {
  const screenshot=path.join(verification,name+'.png');
  const result=spawnSync(chrome,['--headless=new','--disable-gpu','--disable-background-networking','--no-first-run','--no-default-browser-check',
    '--user-data-dir='+profile,'--window-size='+width+','+height,'--virtual-time-budget=4000','--screenshot='+screenshot,'--dump-dom',
    pathToFileURL(path.join(root,'browser-test.html')).href],{encoding:'utf8',timeout:30000,maxBuffer:8*1024*1024,windowsHide:true});
  fs.writeFileSync(path.join(artifacts,name+'-dom.html'),result.stdout??'','utf8');
  fs.writeFileSync(path.join(artifacts,name+'-stderr.log'),result.stderr??'','utf8');
  if(result.error)throw result.error;
  const proof=(result.stdout??'').match(/<pre id="browser-test-result" data-status="([^"]+)">([\s\S]*?)<\/pre>/);
  if(result.status!==0||!proof||proof[1]!=='passed')throw new Error(name+' Chrome验证失败: '+(proof?.[2]??`exit=${result.status}; 没有DOM证明；见artifacts日志`));
  reports.push({name,screenshot,proof:JSON.parse(proof[2]),chromeExit:result.status});
}
fs.writeFileSync(path.join(verification,'chrome-results.json'),JSON.stringify(reports,null,2),'utf8');
fs.writeFileSync(path.join(verification,'chrome-verification.md'),'# 本机Chrome验证\n\n独立用户目录、headless Chrome，无GPT内置浏览器，无游戏连接。\n\n'+reports.map(r=>`- ${r.name}：${r.proof.checks}项页面断言通过；viewport ${r.proof.viewport.join(' × ')}；16个场景逐步遍历、协议搜索/方向过滤/展开、场景条件试验、无横向溢出。`).join('\n')+'\n\n截图为本说明页，不是原生游戏客户端验收。\n','utf8');
console.log(JSON.stringify(reports));
