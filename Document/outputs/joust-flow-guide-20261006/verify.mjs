import assert from 'node:assert/strict';
import fs from 'node:fs';
import {fileURLToPath} from 'node:url';
import path from 'node:path';
import {applyEvent,initialState,simulate,payloadPreview,FIXTURE,detailVisibility,KNIGHTS} from './simulator.mjs';
const root=path.dirname(fileURLToPath(import.meta.url));
const catalog=JSON.parse(fs.readFileSync(path.join(root,'protocol-catalog.json'),'utf8'));
const scenarios=JSON.parse(fs.readFileSync(path.join(root,'scenarios.json'),'utf8')).scenarios;
let checks=0;const eq=(a,b,message)=>{assert.deepEqual(a,b,message);checks++;};
const op=(op,payload={})=>({id:'check',op,payload,label:'check',time:'test',evidence:'test'});
eq(catalog.protocols.length,11,'11 direction entries');
eq(new Set(catalog.protocols.map(p=>p.id)).size,11,'direction/type identity unique');
eq(catalog.protocols.filter(p=>p.scope==='direct_joust').length,10,'10 direct direction entries');
// Oracle从原始stage/scene矩阵独立列出；不调用模拟器自己的scene选择函数。
const oracle=[{13:3,default:5},{13:6,6:6,default:8},{13:9,9:9,default:12},{default:12}];
for(let stage=0;stage<4;stage++)for(let scene=0;scene<14;scene++) {
  const s=initialState();s.scene=scene;
  const f=applyEvent(s,op('results',{stage,records:FIXTURE}));
  eq(f.after.scene,oracle[stage][scene]??oracle[stage].default,`stage ${stage}, scene ${scene}`);
  eq(f.after.managerState,0,'BB不赋manager state');
}
for(const [value,scene] of [[0,0],[1,1],[2,2],[3,8],[4,13]]) {
  const s=initialState();s.scene=8;
  const f=applyEvent(s,op('state',{value}));
  eq(f.after.scene,scene,`B8 state${value}`);eq(f.announcements.length,value===0?0:1,'B8 notice branch');
}
for(const [counter,scene,next] of [[-1,0,-1],[0,5,1],[1,8,2],[2,12,0]]) {
  const s=initialState();s.counter=counter;s.records=FIXTURE;
  const f=applyEvent(s,op('state',{value:5}));eq(f.after.scene,scene,'state5 counter-gate');eq(f.after.counter,next,'state5 counter rotation');
}
let onlyFirst=applyEvent(initialState(),op('results',{stage:0,records:FIXTURE})).after;
eq(onlyFirst.rawResultRecords[6].winner,3,'演示包末槽仍可有值');
eq(onlyFirst.records[6].winner,255,'Stage0没有消费第三组');
for(let i=0;i<3;i++)onlyFirst=applyEvent(onlyFirst,op('state',{value:5})).after;
eq(onlyFirst.announcements.filter(n=>n.id==='0x12BBF').length,0,'未消费冠军槽不能伪造冠军');
const gate=initialState();gate.eventAvailable=true;
const blocked=applyEvent(gate,op('openUI'));
eq(blocked.requests.map(r=>r.opcode),['0x04E6'],'先发E6再拒绝');eq(blocked.after.resultVisible,false,'state0 open拒绝');
const ensure=applyEvent(initialState(),op('ackInfo'));
eq(ensure.after.betObject,true,'ACK ensure');eq(ensure.after.betVisible,false,'ACK未show');eq(ensure.requests.length,0,'ACK不自动查history');
const completed=initialState({initialState:4});completed.eventAvailable=true;completed.scene=12;completed.counter=2;completed.records=FIXTURE;
const reopened=applyEvent(completed,op('openUI'));
eq(reopened.after.scene,12,'打开不重播scene');eq(reopened.announcements.length,0,'打开本身不加冠军公告');
eq(reopened.requests.map(r=>r.opcode),['0x04E6','0x04E8'],'所选show路径的自动查询');
const base=initialState({initialState:1});base.scene=1;base.serverBetTotal=10;base.serverInventory=90;base.mailCount=2;base.settlementCount=1;
for(const event of [op('queryInfo'),op('queryHistory'),op('ackInfo'),op('info'),op('bets'),op('ackHistory',{entries:[{round:1,knight:3,valueRaw:'00000000'}]})]) {
  const f=applyEvent(base,event);
  eq([f.after.serverBetTotal,f.after.serverInventory,f.after.mailCount,f.after.settlementCount],[10,90,2,1],`${event.op}只读server业务`);
  eq(f.announcements.length,0,'纯查询/资料不公告');eq(f.after.managerState,1,'纯查询/资料不赋state');
}
const sHidden=initialState();sHidden.scene=9;
eq(applyEvent(sHidden,op('localAdvance')).after.scene,9,'隐藏tick默认假设暂停');
eq(applyEvent({...sHidden,tickWhileHidden:true},op('localAdvance')).after.scene,10,'显式隐藏tick假设可开');
const oldInventory=applyEvent(initialState({initialState:1}),op('ackBet'));
eq(oldInventory.after.inputInventoryVersion,0,'ACK读取当前旧库存');
const projected=applyEvent(oldInventory.after,op('inventoryProjection',{remaining:90}));
eq(projected.after.inventoryVersion,1,'共享投影新版本');eq(projected.after.inputInventoryVersion,0,'投影不伪造ACK刷新');
eq(applyEvent(projected.after,op('ackBet')).after.inputInventoryVersion,1,'后ACK读取新版本');
const plugin=initialState({extensionEnabled:true});plugin.records=FIXTURE;plugin.counter=2;
eq(applyEvent(plugin,op('stateSnapshot',{value:4})).announcements.length,0,'marker跳B8直接公告');
eq(applyEvent(plugin,op('stateSnapshot',{value:5})).announcements.length,1,'marker不屏蔽scene12公告');
const unsupported=applyEvent(initialState(),op('stateSnapshot',{value:4}));
eq(unsupported.after.managerState,-124,'原生高位signed负值');eq(unsupported.after.scene,0,'无plugin不正常初始化scene');
eq(payloadPreview(op('stateSnapshot',{value:4})).hex,'01 00 84','marker原始byte');
for(const value of [undefined,-1,6,128,1.5]) {
  assert.throws(()=>applyEvent(plugin,op('stateSnapshot',{value})),RangeError);checks++;
  assert.throws(()=>payloadPreview(op('stateSnapshot',{value})),RangeError);checks++;
}
eq(applyEvent(initialState(),op('state',{value:255})).after.managerState,-1,'原生signed byte读取');
eq(payloadPreview(op('confirmBet',{amount:10}),{selectedKnight:3}).hex.split(' ')[14],'03','预览继承实际选择');
for(const [event,length] of [[op('state',{value:1}),3],[op('info'),90],[op('bets'),46],[op('results',{stage:2}),31],[op('ackInfo'),5],[op('ackBet'),5],[op('ackHistory'),3505]]) {
  const p=payloadPreview(event);eq(p.length,length,'body长度');eq(p.hex.split(' ').length,length,'实际hex长度');
}
eq(payloadPreview(op('confirmBet',{knight:3,slot:7,amount:10})).hex.split(' ').filter(x=>x==='??').length,14,'C2S未知前缀不填零');
for(const [scene,hidden,visible] of [[1,1,false],[3,1,true],[1,2,true]])eq(detailVisibility({...initialState(),scene},{...KNIGHTS[0],hidden}),visible,'B9精确隐藏门');
// 检验七场模拟赛果确实来自同一个淘汰树，不编造跨配对胜者。
let entrants=[0,1,2,3,4,5,6,7],offset=0;
while(entrants.length>1){let next=[];for(let i=0;i<entrants.length/2;i++){const r=FIXTURE[offset+i];eq([r.winner,r.loser].sort(),[entrants[i*2],entrants[i*2+1]].sort(),'同一对局');next.push(r.winner);}offset+=next.length;entrants=next;}
const outputs=scenarios.map(s=>({scenario:s,result:simulate(s)}));
for(const {scenario,result} of outputs) {
  eq(result.frames.length,scenario.events.length,'所有场景事件执行完成');
  for(const f of result.frames) if(!['acceptBetBusiness','settleBusiness'].includes(f.event.op)) {
    eq([f.before.serverBetTotal,f.before.serverInventory,f.before.mailCount,f.before.settlementCount],[f.after.serverBetTotal,f.after.serverInventory,f.after.mailCount,f.after.settlementCount],`${scenario.id}/${f.event.op}不偷改业务`);
  }
}
const full=outputs.find(o=>o.scenario.id==='native-candidate-full').result;
eq(full.frames.filter(f=>f.event.op==='localAdvance').map(f=>f.after.scene),[4,5,7,8,10,12],'三组完整local路径');
eq(full.final.announcements.filter(n=>n.id==='0x12BBF').length,1,'完整候选不自动补BB3重复冠军');
eq([full.final.serverBetTotal,full.final.serverInventory,full.final.inventoryRemaining,full.final.mailCount],[10,90,90,1],'示例领域闭环');
const repeat=outputs.find(o=>o.scenario.id==='repeat-bb3-champion').result;
eq(repeat.final.announcements.filter(n=>n.id==='0x12BBF').length,2,'BB3重放确实重复冠军');
for(const o of outputs.filter(o=>o.scenario.kind==='compatibility'))eq(o.result.final.managerState>=0,true,'兼容场景必须真的启用plugin模型');
const report=`# 模型验证\n\n${checks} 个断言通过，${scenarios.length} 个时间线场景执行完成。\n\n覆盖BB四阶段×14场景、B8状态/counter、入口门、ensure/show、查询业务只读、共享库存版本、marker边界、payload长度、淘汰树、三组完整推进和重复冠军。\n\n这是研究模型一致性检查，不是原生客户端等价性、WireCaptured或ClientVerified。\n`;
fs.writeFileSync(path.join(root,'model-verification.md'),report,'utf8');
console.log(JSON.stringify({checks,scenarios:scenarios.length,result:'passed',boundary:'simulation consistency only'}));
