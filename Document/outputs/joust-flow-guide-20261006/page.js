const $=id=>document.getElementById(id);
const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const list=items=>`<ul>${(items??[]).map(x=>`<li>${esc(x)}</li>`).join('')}</ul>`;
const fmt=v=>v===null||v===undefined?'—':typeof v==='boolean'?(v?'是':'否'):String(v);
const scenarios=SCENARIO_DATA.scenarios;
const kindLabels={candidate:'模拟候选顺序',contrast:'错误路径对照','read-only':'查询只读对照',recovery:'中途恢复候选',compatibility:'自定义兼容扩展','compatibility-contrast':'扩展缺失对照','client-static':'原生消费边界',counterexample:'错误路径对照',boundary:'行为边界',current:'当前实现对照'};
let chosen=scenarios[0],result=null,cursor=-1,timer=null;
function stop(){if(timer!==null){clearInterval(timer);timer=null;}$('play').textContent='自动播放';}
function eventClass(event){
  if(['acceptBetBusiness','settleBusiness'].includes(event.op))return ['DOMAIN · 示例业务','warn'];
  if(event.op==='inventoryProjection')return ['共享系统投影',''];
  if(event.op==='localAdvance')return ['LOCAL · 动画消费',''];
  if(event.op==='stateSnapshot')return ['CompatibilityExtension','warn'];
  if(['openUI','closeUI','openBetUI','selectKnight','confirmBet','queryInfo','queryHistory'].includes(event.op))return ['UI / C2S',''];
  return ['S2C · 合成输入','good'];
}
function rebuild(){result=simulate({...chosen,options:{...chosen.options,tickWhileHidden:$('hidden-tick').checked}});}
function choose(id){
  stop();chosen=scenarios.find(s=>s.id===id)??scenarios[0];cursor=-1;$('hidden-tick').checked=!!chosen.options?.tickWhileHidden;rebuild();
  $('scenario-kind').textContent=kindLabels[chosen.kind]??chosen.kind;
  $('scenario-title').textContent=chosen.title;$('scenario-description').textContent=chosen.description+(chosen.id==='native-candidate-full'?' 三组均可起播，但每组state4仍会公告；这是可达性候选，尚不是上线方案。':'');
  $('assumptions').innerHTML=list(chosen.assumptions)+`<p><b>${chosen.options?.extensionEnabled?'本场景启用0190插件模型':'本场景使用原生state消费模型'}</b>；隐藏UI是否tick由上方开关显式设为模拟假定。</p>`;
  document.querySelectorAll('.scenario-btn').forEach(b=>b.classList.toggle('active',b.dataset.scenario===chosen.id));
  $('step-range').max=chosen.events.length-1;render();
}
function jump(index){stop();cursor=Math.max(-1,Math.min(chosen.events.length-1,index));render();}
function nname(index){return `骑士${String.fromCharCode(65+index)}`;}
function renderClient(s){
  $('ui-visibility').textContent=s.resultVisible?'结果窗已显示':s.resultObject?'结果窗对象存在 · 未显示':'结果窗尚未创建';
  $('ui-visibility').className='pill '+(s.resultVisible?'good':'');
  $('calendar-gate').innerHTML=`<div class="gate-line">通用活动表093D：<b>${s.eventAvailable?'存在 · 入口开放':'不存在 · 入口拒绝'}</b> / 投注窗：${s.betVisible?'已显示':s.betObject?'对象存在':'未创建'}</div>`;
  $('round-label').textContent=s.round?`轮次标记 ${s.round}`:'轮次未同步';$('game-state').textContent=SCENES[s.scene]??`scene${s.scene}`;
  $('game-overlay').textContent=s.resultVisible?'下方展示当前数据和scene的概念投影；不是原生渲染的像素复刻。':'窗口未显示：下方仍可查看对象缓存的资料；对象存在不代表窗口已打开或tick正在运行。';
  $('knight-grid').innerHTML=s.infoReady?s.knights.map(k=>{
    const visible=detailVisibility(s,k);
    return `<div class="knight ${k.index===s.selectedKnight?'selected':''} ${visible?'':'hidden'}"><div class="avatar">${visible?esc(String.fromCharCode(65+k.index)):'?'}</div><div class="name">${visible?`${nname(k.index)} (${k.index})`:'资料被遮蔽'}</div><div class="tiny">${visible?`个人投入 ${esc(s.personalBets[k.index]??0)}`:'scene&lt;3 &amp; hidden=1'}</div></div>`;
  }).join(''):'<div class="empty">尚未收到04B9骑士资料。ACK确保对象存在，不会填充这些资料。</div>';
  $('inventory-value').textContent=s.inventoryRemaining;$('bet-value').textContent=s.personalTotal;
  $('bet-gate').textContent=s.canBet?'模拟条件可投注':'当前不可投注';$('bet-gate').className='pill '+(s.canBet?'good':'warn');
  const selected=s.knights.find(k=>k.index===s.selectedKnight);
  $('selected-knight').textContent=selected?(detailVisibility(s,selected)?`${nname(selected.index)}：conditionImg(${(selected.condition??0)+3}) / attackTypeImg(${(selected.attack??0)+8}) / 计数 ${selected.wins??0}:${selected.losses??0}；图片中文业务含义未知。`:'所选骑士的姓名、计数及两个图标被隐藏门遮蔽。'):'等待资料，尚无可展示的选手详情。';
  const groups=[[0,1,2,3],[4,5],[6]];
  $('bracket').innerHTML=groups.map((indices,group)=>`<div class="bracket-col"><div class="bracket-heading">${['第一组 · 四场','第二组 · 两场','第三组 · 一场'][group]}</div>${indices.map(i=>{const r=s.records[i];return r&&r.winner<255?`<div class="match"><span class="win">${nname(r.winner)} 胜</span><br><span class="lose">${nname(r.loser)} 败</span><div class="tiny">轨迹aux ${r.winnerAux??0}/${r.loserAux??0}</div></div>`:'<div class="match pending">未揭晓 / 未消费</div>';}).join('')}</div>`).join('');
}
function logItem(text,type='',meta=''){return `<div class="log-row ${type}">${esc(text)}${meta?`<small>${esc(meta)}</small>`:''}</div>`;}
function render(){
  const frame=cursor>=0?result.frames[cursor]:null,s=frame?.after??result.initial,before=frame?.before??s;
  $('step-range').value=cursor;$('step-counter').textContent=`${cursor+1} / ${chosen.events.length}`;
  $('prev').disabled=cursor<0;$('next').disabled=cursor>=chosen.events.length-1;
  $('state-value').textContent=s.managerState;$('state-label').textContent=STATES[s.managerState]??'范围外signed状态';
  $('scene-value').textContent=s.scene;$('scene-label').textContent=SCENES[s.scene]??'范围外';
  $('counter-value').textContent=s.counter;$('notice-value').textContent=s.announcements.length;
  $('event-time').textContent=frame?.event.time??'T+00';$('event-label').textContent=frame?.event.label??'模拟初始状态 · 等待第一步';
  const [label,color]=frame?eventClass(frame.event):['假定reset后的初值','warn'];$('event-class').textContent=label;$('event-class').className='pill '+color;
  $('event-note').textContent=frame?`${frame.event.note??''} ${frame.event.evidence?'['+frame.event.evidence+']':''}`:'state0是本模型假定reset后的初值，不是客户端构造或登录必清零的证明。';
  $('effect-list').innerHTML=(frame?.effects??['006C活动入口与马战state尚未同步。按“下一步”开始。']).map(t=>`<div class="effect">${esc(t)}</div>`).join('');
  $('automatic-requests').innerHTML=frame?.requests.length?`<div class="automatic">自动产生：${frame.requests.map(r=>esc(`${r.kind} ${r.opcode}${r.opcode==='0x04E7'?' '+JSON.stringify(r.payload):''}`)).join(' → ')} <span class="micro">（只记录，不发真实网络）</span></div>`:'';
  renderClient(s);
  $('ledger-value').textContent=s.serverBetTotal;$('server-inventory-value').textContent=s.serverInventory;$('mail-value').textContent=`${s.settlementCount} / ${s.mailCount}`;
  const current=[];
  frame?.requests.forEach(r=>current.push(logItem(`${r.kind} ${r.opcode} 请求`,'',r.opaquePrefix)));
  frame?.announcements.forEach(n=>current.push(logItem(n.text,'notice',`${n.origin} · ${n.id}`)));
  frame?.popups.forEach(p=>current.push(logItem(`${p.id} 弹窗路径`,'popup',p.reason)));
  frame?.warnings.forEach(t=>current.push(logItem(t,'popup','模型边界 / Unknown')));
  $('current-side-effects').innerHTML=current.join('')||'<div class="empty">本步没有自动请求、公告或弹窗。</div>';
  $('notice-list').innerHTML=s.announcements.length?s.announcements.map((n,i)=>logItem(`${i+1}. ${n.text}`,'notice',`${n.origin} · ${n.id}`)).join(''):'<div class="empty">暂无公告。</div>';
  $('history-list').innerHTML=s.history.length?s.history.map(e=>logItem(`轮次${e.round} · index=${e.knight??e.winner??'—'} · raw4=${e.valueRaw??'Unknown'}`,'','字段业务意义尚未全部闭合；不是追加邮件或补结算')).join(''):'<div class="empty">无历史记录 / 尚未查询。</div>';
  const rows=[['manager state','managerState'],['主窗口 scene','scene'],['比赛组 counter','counter'],['轮次标记','round'],['093D存在门','eventAvailable'],['结果窗对象','resultObject'],['结果窗显示','resultVisible'],['投注窗对象','betObject'],['投注窗显示','betVisible'],['客户端剩余材料','inventoryRemaining'],['客户端库存版本','inventoryVersion'],['输入控件读取版本','inputInventoryVersion'],['个人投入投影','personalTotal'],['服务端投注账本','serverBetTotal'],['服务端剩余材料','serverInventory'],['结算次数','settlementCount'],['邮件数','mailCount']];
  $('state-diff').innerHTML=rows.map(([title,key])=>`<tr class="${before[key]!==s[key]?'changed':''}"><td>${title}</td><td>${esc(fmt(before[key]))}</td><td>${esc(fmt(s[key]))}</td></tr>`).join('');
  const preview=frame?payloadPreview(frame.event,frame.before):null;
  $('packet-content').innerHTML=preview?`<p><code>${esc(preview.name)}</code> · body ${esc(preview.length)} B</p><p class="micro warning-text">${esc(preview.boundary)}</p><pre>${esc(preview.hex)}</pre><details><summary>结构化输入</summary><pre>${esc(JSON.stringify(frame.event.payload??{},null,2))}</pre></details>${frame.event.anchor?`<p class="micro">${esc(frame.event.anchor)}</p>`:''}`:'<p>初值没有网络包。</p>';
  $('timeline-list').innerHTML=result.frames.map((f,i)=>`<button class="timeline-row ${i===cursor?'active':''}" data-step="${i}" aria-label="第${i+1}步 ${esc(f.event.label)}"><span class="timeline-time">${esc(f.event.time)}</span><span>${esc(f.event.label)}</span><span class="timeline-scene">${f.before.scene} → ${f.after.scene}${f.announcements.length?' · 公告+'+f.announcements.length:''}</span></button>`).join('');
}
function sourceHref(source){
  const old='/Document/outputs/joust-client-reanalysis-20261006/';
  if(source.includes(old))return '../joust-client-reanalysis-20261006/'+source.split(old)[1];
  if(source.includes('/protocol/'))return '../../../protocol/'+source.split('/protocol/')[1];
  return source.startsWith('E:/')?'file:///'+source:source;
}
function fieldsTable(fields){return `<div class="table-scroll"><table><thead><tr><th>Offset</th><th>字节</th><th>编码 / 字段</th><th>含义与边界</th></tr></thead><tbody>${fields.map(f=>`<tr><td>${esc(f.offset??'—')}</td><td>${esc(f.size_bytes??'变长')}</td><td><code>${esc(f.encoding??'')}</code><br>${esc(f.name)}</td><td>${esc(f.meaning??'')}<div class="micro">${esc(f.certainty??'')}</div>${f.record_fields?`<details><summary>重复项字段</summary><pre>${esc(JSON.stringify(f.record_fields,null,2))}</pre></details>`:''}</td></tr>`).join('')}</tbody></table></div>`;}
function renderCatalog(){
  const q=$('catalog-search').value.trim().toLowerCase(),direction=$('direction-filter').value;
  const protocols=CATALOG.protocols.filter(p=>(direction==='all'||p.direction===direction)&&(!q||JSON.stringify(p).toLowerCase().includes(q)));
  $('catalog-count').textContent=`显示 ${protocols.length} / 11`;
  $('catalog-list').innerHTML=protocols.map(p=>`<details class="protocol-item"><summary><span class="proto-id">${esc(p.opcode)}</span><span class="proto-direction">${esc(p.direction)} ${esc(p.kind)} / cmd=${p.command_byte}</span><span class="proto-title">${esc(p.title)}</span><span class="pill">${esc(p.body.length_bytes??'变长')} B</span></summary><div class="protocol-content">${fieldsTable(p.body.fields??[])}${Object.entries(p.effects??{}).map(([name,effects])=>`<h4>${esc(({state:'管理器状态',ui:'UI变化',announcement:'公告/弹窗',server_effects:'服务端职责',client_request_trigger:'请求触发'}[name])??name)}</h4>${list(Array.isArray(effects)?effects:[effects])}`).join('')}<h4>未知与约束</h4>${list(p.unknowns)}${p.error_map?`<h4>Result分支</h4><pre>${esc(JSON.stringify(p.error_map,null,2))}</pre>`:''}<div class="anchors">${(p.anchors??[]).map(a=>`${esc(a.role)}: RVA ${esc(a.rva)} / VA ${esc(a.va)}`).join('<br>')}</div><details><summary class="micro">取证来源 / 完整结构</summary>${(p.sources??[]).map(src=>`<p><a class="source-link" href="${esc(sourceHref(src))}">${esc(src.split('/').slice(-1)[0])}</a></p>`).join('')}<pre>${esc(JSON.stringify(p,null,2))}</pre></details></div></details>`).join('')||'<div class="empty">没有匹配条目。</div>';
}
function fillRules(){
  const state=CATALOG.protocols.find(p=>p.id==='S2C-NOTI-04B8');
  const stage=CATALOG.protocols.find(p=>p.id==='S2C-NOTI-04BB');
  $('state-matrix').innerHTML=`<table><thead><tr><th>state</th><th>scene消费</th><th>直接公告</th></tr></thead><tbody>${state.state_table.map(r=>`<tr><td><code>${r.state}</code></td><td>${r.state===5?'counter0/1/2 → 5/8/11':r.scene===null?'保持原scene':r.scene}</td><td>${esc(r.direct_string_id??'无')}</td></tr>`).join('')}</tbody></table>`;
  $('stage-matrix').innerHTML=`<table><thead><tr><th>stage</th><th>起播条件scene</th><th>满足时</th><th>其它</th></tr></thead><tbody>${stage.stage_table.map(r=>`<tr><td><code>${r.stage}</code></td><td>${esc(r.scene_gate?.join(' / ')??'无条件')}</td><td>${r.on_gate_scene}</td><td>${r.otherwise_scene}</td></tr>`).join('')}</tbody></table>`;
  $('sandbox-scene').innerHTML=Object.entries(SCENES).map(([i,label])=>`<option value="${i}" ${i==='8'?'selected':''}>${i} · ${esc(label)}</option>`).join('');$('sandbox-stage').value='2';
  $('global-unknowns').innerHTML=list([...CATALOG.global_unknowns,'原生登录是否reset、通用取消活动是否关闭现有UI，仍未闭合。','模型的阶段名称、材料100、骑士A–H和七场固定胜负是解释用fixture，不是PVF原始数据。','首14B仅标??占位；合成body没有frame头、seq、checksum，不可直接用来发服。','每组state4→BB的候选可起播但带重复状态公告，完整生产时序和最小patch选择尚待决定。']);
  $('catalog-extra').innerHTML=`<h4>共享依赖</h4>${CATALOG.dependencies.map(d=>`<p><code>${esc(d.id)}</code> · ${esc(d.meaning??d.description??JSON.stringify(d))}</p>`).join('')}<h4>邻近候选：未证明归属马战</h4><pre>${esc(JSON.stringify(CATALOG.candidate_appendix,null,2))}</pre><h4>陈旧记录与重分析的差异</h4>${CATALOG.document_conflicts.map(d=>`<p><code>${esc(d.source)}</code> · ${esc(d.issue)}</p>`).join('')}`;
  $('identity').innerHTML=`<p>Dump SHA256：<code>${esc(CATALOG.identity.dump_sha256)}</code><br>EXE预期SHA256（本页不是重新实算）：<code>${esc(CATALOG.identity.exe_expected_sha256)}</code><br>Image base：<code>${esc(CATALOG.identity.image_base)}</code></p><p>模型367个断言、16个场景通过。验证的是模型一致性，不能替代原生客户端时序验收。</p>`;
  runSandbox();
}
function runSandbox(){
  const s=initialState();s.scene=Number($('sandbox-scene').value);s.resultVisible=true;
  const f=applyEvent(s,{op:'results',payload:{stage:Number($('sandbox-stage').value),records:FIXTURE}});
  $('sandbox-result').innerHTML=`<strong>scene ${s.scene} → ${f.after.scene}</strong> · ${esc(SCENES[f.after.scene])}<br><span class="micro">counter → ${f.after.counter} / 本次冠军公告 ${f.announcements.filter(n=>n.id==='0x12BBF').length} 次。试验使用完整有效七场fixture，Stage0只消费前6槽。</span>`;
}
$('scenario-list').innerHTML=scenarios.map((s,i)=>`<button class="scenario-btn" data-scenario="${esc(s.id)}">${String(i+1).padStart(2,'0')} · ${esc(s.title)}<small>${s.events.length} 步 · ${esc(kindLabels[s.kind]??s.kind)}</small></button>`).join('');
$('scenario-list').addEventListener('click',e=>{const b=e.target.closest('[data-scenario]');if(b)choose(b.dataset.scenario);});
$('timeline-list').addEventListener('click',e=>{const b=e.target.closest('[data-step]');if(b)jump(Number(b.dataset.step));});
$('prev').addEventListener('click',()=>jump(cursor-1));$('next').addEventListener('click',()=>jump(cursor+1));$('reset').addEventListener('click',()=>jump(-1));
$('step-range').addEventListener('input',()=>jump(Number($('step-range').value)));
$('play').title='每900ms播放一个概念步骤，仅为阅读节奏，不代表赛程时间';
$('play').addEventListener('click',()=>{if(timer!==null){stop();return;}if(cursor>=chosen.events.length-1)cursor=-1;$('play').textContent='暂停';timer=setInterval(()=>{cursor++;render();if(cursor>=chosen.events.length-1)stop();},900);});
$('hidden-tick').addEventListener('change',()=>{stop();rebuild();render();});
$('catalog-search').addEventListener('input',renderCatalog);$('direction-filter').addEventListener('change',renderCatalog);
document.querySelectorAll('[data-tab]').forEach(b=>b.addEventListener('click',()=>{stop();document.querySelectorAll('[data-tab]').forEach(t=>t.classList.toggle('active',t===b));document.querySelectorAll('.tab-panel').forEach(p=>p.classList.toggle('active',p.id===b.dataset.tab));}));
$('sandbox-run').addEventListener('click',runSandbox);$('sandbox-scene').addEventListener('change',runSandbox);$('sandbox-stage').addEventListener('change',runSandbox);
fillRules();renderCatalog();choose(scenarios[0].id);
document.documentElement.dataset.guideReady='true';
