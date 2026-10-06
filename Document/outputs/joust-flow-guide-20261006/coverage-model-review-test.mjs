import assert from 'node:assert/strict';
import {initialState,applyEvent,payloadPreview} from './simulator.mjs';
const step=(s,op,payload={})=>applyEvent(s,{op,payload});
let passed=0,failed=0;
function check(name,fn){try{fn();passed++;console.log('PASS '+name);}catch(e){failed++;console.log('FAIL '+name+': '+e.message);}}
check('query/history与成功ACK不改领域账本或邮件',()=>{
 let s=initialState();s.serverBetTotal=10;s.serverInventory=90;s.settlementCount=1;s.mailCount=2;
 for(const op of ['queryInfo','queryHistory','ackInfo','ackBet','ackHistory'])s=step(s,op).after;
 assert.deepEqual([s.serverBetTotal,s.serverInventory,s.settlementCount,s.mailCount],[10,90,1,2]);
});
check('成功ACK ensure不show、不产生自动history请求、不赋state',()=>{
 const f=step(initialState(),'ackInfo');assert.equal(f.after.betObject,true);
 assert.equal(f.after.betVisible,false);assert.equal(f.after.managerState,0);assert.equal(f.requests.length,0);
});
check('无插件marker1作为signed负state且不走scene1',()=>{
 const f=step(initialState(),'stateSnapshot',{value:1});assert.equal(f.after.managerState,-127);assert.equal(f.after.scene,0);assert.equal(f.announcements.length,0);
});
check('配套插件marker1恢复state但无B8直接公告',()=>{
 const f=step(initialState({extensionEnabled:true}),'stateSnapshot',{value:1});assert.equal(f.after.managerState,1);assert.equal(f.after.scene,1);assert.equal(f.announcements.length,0);
});
check('scene12重复进入重复公告',()=>{
 let s=step(initialState(),'results',{stage:3}).after;const f=step(s,'results',{stage:3});assert.equal(f.after.announcements.length,2);
});
for(const scene of [5,8])check('scene'+scene+'无本地自动推进',()=>{let s=initialState();s.scene=scene;s.resultVisible=true;assert.equal(step(s,'localAdvance').after.scene,scene);});
check('共享库存投影先改变库存版本，ACK再读；不扣领域库存',()=>{
 let s=initialState();s=step(s,'inventoryProjection',{remaining:90}).after;
 assert.equal(s.inputInventoryVersion,0);assert.equal(s.inventoryVersion,1);assert.equal(s.serverInventory,100);
 s=step(s,'ackBet').after;assert.equal(s.inputInventoryVersion,1);assert.equal(s.serverInventory,100);
});
check('移除093D仅关gate，不reset已显示state/scene',()=>{
 let s=initialState();s.managerState=4;s.scene=13;s.resultVisible=true;
 const f=step(s,'eventList',{enabled:false});assert.equal(f.after.managerState,4);assert.equal(f.after.scene,13);assert.equal(f.after.resultVisible,true);
});
check('confirmBet预览与实际派生request使用同一selectedKnight',()=>{
 let s=initialState();s.managerState=1;s.infoReady=true;s.canBet=true;s.selectedKnight=3;
 const event={op:'confirmBet',payload:{slot:7,amount:10}},f=applyEvent(s,event),preview=payloadPreview(event,s);
 const tail=preview.hex.split(' ').slice(14);assert.equal(parseInt(tail[0],16),f.requests[0].payload.knight);
});
check('stateSnapshot拒绝0..5之外的非插件值',()=>{
 assert.throws(()=>step(initialState({extensionEnabled:true}),'stateSnapshot',{value:128}));
});
console.log(JSON.stringify({passed,failed}));
process.exitCode=failed?1:0;
