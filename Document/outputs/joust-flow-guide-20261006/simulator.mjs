// 只模拟已取证的消费者与显式业务示例；没有网络、文件或数据库操作。
export const SCENES = {
  0:'基础界面 / scene0',1:'基础界面 / scene1',2:'基础界面 / scene2',
  3:'第一组前置动画',4:'第一组轨迹播放',5:'第一组结果 · 等待输入',
  6:'第二组前置动画',7:'第二组轨迹播放',8:'第二组结果 · 等待输入',
  9:'第三组前置动画',10:'第三组轨迹播放',11:'第三组结束 · 立即进入12',
  12:'最终结果 / 冠军展示',13:'待起播 / scene13'
};
export const STATES={0:'关闭（领域映射）',1:'可投注（领域映射）',2:'结果回顾（领域映射）',3:'停止投注（领域映射）',4:'比赛准备（领域映射）',5:'强制结束当前组（候选解释）'};
export const FIXTURE=[
  {winner:0,loser:1},{winner:3,loser:2},{winner:4,loser:5},{winner:7,loser:6},
  {winner:3,loser:0},{winner:4,loser:7},{winner:3,loser:4}
].map(r=>({...r,winnerAux:0,loserAux:0}));
export const KNIGHTS=Array.from({length:8},(_,index)=>({index,name:`骑士${String.fromCharCode(65+index)}（模拟）`,condition:0,attack:0,wins:0,losses:0,hidden:0,raw4:[0,0,0,65]}));
export function initialState(options={}) {
  return {managerState:options.initialState??0,scene:0,counter:-1,round:0,eventAvailable:false,
    resultObject:false,betObject:false,resultVisible:false,betVisible:false,infoReady:false,
    knights:[],selectedKnight:0,inventoryRemaining:100,inventoryVersion:0,inputInventoryVersion:0,
    personalTotal:0,personalBets:{},history:[],records:[],rawResultRecords:[],serverBetTotal:0,serverInventory:100,
    settlementCount:0,mailCount:0,requests:[],announcements:[],popups:[],warnings:[],
    extensionEnabled:options.extensionEnabled??false,tickWhileHidden:options.tickWhileHidden??false,
    canBet:false,lastBetRequest:null};
}
const copy = x=>JSON.parse(JSON.stringify(x));
const knightName=i=>KNIGHTS.find(k=>k.index===i)?.name??`骑士${i}（模拟）`;
function ensureResult(s) {s.resultObject=true;}
function ensureBet(s) {s.betObject=true;}
function request(s,trace,opcode,payload={}) {
  const r={opcode,direction:'C2S',kind:'CMD',payload,opaquePrefix:'14B Unknown，占位不代表有效线包'};
  s.requests.push(r);trace.requests.push(r);
}
function notice(s,trace,id,origin,text) {
  const n={id,origin,text:text??`状态公告 ${id}（字符串正文尚未资源核对）`};
  s.announcements.push(n);trace.announcements.push(n);
}
function popup(s,trace,id,reason) { const p={id,reason};s.popups.push(p);trace.popups.push(p); }
function warning(s,trace,text) {s.warnings.push(text);trace.warnings.push(text);}
function refreshControls(s,trace) {
  s.inputInventoryVersion=s.inventoryVersion;
  // state!=1时禁用为ClientStatic；其余条件是本说明页显式的示例规则。
  s.canBet=s.managerState===1&&s.infoReady&&s.inventoryRemaining>0;
  trace.effects.push(`投注输入读取库存版本 v${s.inventoryVersion}；state=${s.managerState}${s.canBet?'，模拟条件允许投注':'，当前不可投注'}`);
}
function snapshotValue(p) {
  if(!Number.isInteger(p.value)||p.value<0||p.value>5) throw new RangeError('stateSnapshot只定义整数state 0..5；不支持缺失或范围外值');
  return p.value;
}
function setScene(s,scene,trace) {
  s.scene=scene;trace.scenePath.push(scene);
  if((scene===0||scene===1)&&s.resultVisible) {
    request(s,trace,'0x04E6');trace.effects.push('scene0/1且结果窗已显示：自动查询04E6');
  }
  if(scene===11) {setScene(s,12,trace);return;}
  if(scene===12) {
    const champion=s.records[6]?.winner;
    if(champion!==undefined&&champion>=0&&champion<255) {
      notice(s,trace,'0x12BBF','scene12',`${knightName(champion)} · 冠军公告 12BBF（原文待核对）`);
    } else trace.effects.push('scene12但无有效第三组胜者：此抽象模型不生成冠军文本');
  }
}
function showBet(s,trace) {
  ensureBet(s);s.betVisible=true;
  refreshControls(s,trace);request(s,trace,'0x04E8');
  trace.effects.push('投注窗显示初始化：重建材料控件并查询历史04E8；ensure自身不会执行这一步');
}
const infoErrors={1:'0x12BC0',3:'0x12BC0',4:'0x12BC0',6:'0x12ACA'};
const betErrors={4:'0x12BC0',6:'0x12BC2',7:'0x12BC4',8:'0x12ACA'};
export function applyEvent(previous,event) {
  const s=copy(previous),p=event.payload??{},trace={event:copy(event),before:copy(previous),effects:[],requests:[],announcements:[],popups:[],warnings:[],scenePath:[]};
  switch(event.op) {
    case 'eventList':
      s.eventAvailable=!!p.enabled;
      trace.effects.push(`模拟006C后活动093D${s.eventAvailable?'存在':'不存在'}；不赋马战state、不关闭已显示窗口`);
      if(!s.eventAvailable&&s.resultVisible) warning(s,trace,'取消活动对已打开窗口的关闭行为未知；模型保留窗口');
      break;
    case 'state':case 'stateSnapshot': {
      const marked=event.op==='stateSnapshot';
      let value=marked?snapshotValue(p):(p.value??0);
      if(!marked&&value>=128&&value<=255)value-=256;
      if(marked&&!s.extensionEnabled) {
        value=(value|0x80)-256;
        warning(s,trace,'未启用配套插件：高位标记被原生读取为负state，不能正常映射scene');
      }
      s.managerState=value;ensureBet(s);ensureResult(s);s.round=p.round??1;
      if(value===0||value===1||value===2||value===4) setScene(s,{0:0,1:1,2:2,4:13}[value],trace);
      else if(value===5&&s.counter>=0&&s.counter<=2) {
        const c=s.counter;setScene(s,[5,8,11][c],trace);s.counter=(c+1)%3;
      } else if(value===5) trace.effects.push('counter=-1或范围外：state5不触发有效组结束');
      if(!marked&&value>=1&&value<=4) notice(s,trace,['','0x12BCB','0x12BCC','0x12BBD','0x12BBE'][value],'04B8');
      if(marked&&s.extensionEnabled) trace.effects.push('自定义插件恢复manager/UI的0..5；原始字节仍带高位，B8直接公告被跳过，scene12公告不因此消失');
      refreshControls(s,trace);break;
    }
    case 'info':
      ensureBet(s);ensureResult(s);s.knights=copy(p.knights??KNIGHTS);s.infoReady=true;
      refreshControls(s,trace);trace.effects.push('04B9填8名骑士投影；condition来自包，attack来自模拟PVF规则；不赋manager state');break;
    case 'bets':
      ensureBet(s);s.personalTotal=p.total??0;s.personalBets=copy(p.amounts??{});
      trace.effects.push('04BA刷新个人总投入与各骑士投入；不扣除库存、不结算、不公告');break;
    case 'results': {
      ensureResult(s);s.round=p.round??1;
      const stage=p.stage??0,valid=p.validCount??([4,6,7,7][stage]??0);
      const records=p.records??FIXTURE.map((r,i)=>i<valid?r:{winner:255,loser:255,winnerAux:0,loserAux:0});
      s.rawResultRecords=copy(records);
      // Stage0只消费前两组；第三组集合没有由末槽建立。
      s.records=stage>=0&&stage<=3?copy(records.map((r,i)=>stage===0&&i===6?{winner:255,loser:255,winnerAux:0,loserAux:0}:r)):[];
      if(stage>=0&&stage<=3) {
        const oldScene=s.scene;s.counter=stage===0?0:stage===1?1:2;
        setScene(s,stage===0?(oldScene===13?3:5):stage===1?([13,6].includes(oldScene)?6:8):stage===2?([13,9].includes(oldScene)?9:12):12,trace);
        trace.effects.push(`BB Stage${stage}消费${stage===0?6:7}场记录槽；消费集合有效${s.records.filter(r=>r.winner<255).length}场。未揭晓FF槽是模拟策略，未实机验证`);
      } else warning(s,trace,'stage超范围：round/集合仍可能已改；此模型不进一步模拟未解析分支');
      break;
    }
    case 'openUI':
      if(!s.eventAvailable) popup(s,trace,'0x12BC0','通用活动表没有093D，入口拒绝');
      else {
        ensureResult(s);request(s,trace,'0x04E6');
        if(s.managerState===0) popup(s,trace,'0x12BC0','先发查询，然后立即读已有state=0，拒绝打开');
        else {s.resultVisible=true;showBet(s,trace);trace.effects.push('已有非零state：本模型选取结果窗/投注窗显示路径；受保护show及重开回调顺序尚未完全闭合');}
      }
      break;
    case 'closeUI':s.resultVisible=false;s.betVisible=false;trace.effects.push('模型隐藏两个窗口，保留对象、scene和counter；原生关闭清理细节Unknown');break;
    case 'openBetUI':showBet(s,trace);break;
    case 'selectKnight':s.selectedKnight=p.index??0;refreshControls(s,trace);trace.effects.push(`选择${knightName(s.selectedKnight)}`);break;
    case 'confirmBet':
      if(!s.canBet) popup(s,trace,'MODEL-GATE','模拟输入不可投注，未产生04E7；这是UI gate示例，不是额外服务端校验');
      else if((p.amount??0)!==0) {s.lastBetRequest={knight:p.knight??s.selectedKnight,slot:p.slot??5,amount:p.amount};request(s,trace,'0x04E7',s.lastBetRequest);trace.effects.push('确认回调只生成投注请求；客户端请求本身不修改服务端账本');}
      break;
    case 'queryInfo':request(s,trace,'0x04E6');break;
    case 'queryHistory':request(s,trace,'0x04E8');break;
    case 'ackInfo':case 'ackBet': {
      if((p.success??1)===0) {trace.effects.push('公共success门为0：reader不消费后续result');break;}
      const result=p.result??0;
      if(result===0) {ensureBet(s);if(event.op==='ackBet')refreshControls(s,trace);trace.effects.push(event.op==='ackInfo'?'04E6成功：仅ensure投注UI，不显示、不赋state、不自动查询历史':'04E7成功：重新读取当前库存和输入，不携带扣除或投注合计');}
      else {const id=(event.op==='ackInfo'?infoErrors:betErrors)[result];if(id)popup(s,trace,id,'原生result映射；业务含义与原文待核对');else trace.effects.push(`result=${result}：此reader静默返回`);}
      break;
    }
    case 'ackHistory':
      if((p.success??1)!==0&&(p.result??0)===0) {
        ensureBet(s);s.history=[];
        for(const entry of (p.entries??[]).slice(0,500)) {if(!entry.round)break;s.history.push(copy(entry));}
        trace.effects.push('04E8成功：清历史容器、读至round0或500条、整理滚动UI；服务器账本与邮件无变化');
      } else trace.effects.push('04E8失败：不更新历史；success非0仍需固定长度reader载荷');break;
    case 'localAdvance': {
      if(!s.resultVisible&&!s.tickWhileHidden) {warning(s,trace,'隐藏UI是否tick未知；本场景默认暂停本地推进，不推断原生行为');break;}
      const next={3:4,4:5,6:7,7:8,9:10,10:11}[s.scene];
      if(next!==undefined) {setScene(s,next,trace);trace.effects.push('抽象动画完成/轨迹耗尽一步；概念时刻，不代表9100的已确认时间单位');}
      else trace.effects.push('当前scene不在本地tick推进集合（5/8等待外部输入）');break;
    }
    case 'acceptBetBusiness':
      s.serverBetTotal+=p.amount??0;s.serverInventory-=p.amount??0;
      trace.effects.push('DOMAIN业务示例：保存投注并扣除服务端材料；不是客户端reader自动行为');break;
    case 'inventoryProjection':
      s.inventoryRemaining=p.remaining??s.inventoryRemaining;s.inventoryVersion++;
      trace.effects.push('共享库存投影：更新客户端材料版本；此步骤未编造具体库存包完整布局');break;
    case 'settleBusiness':
      if(s.settlementCount===0) {s.settlementCount++;s.mailCount+=p.mailCount??1;trace.effects.push('DOMAIN业务示例：一次结算并交邮件系统投递奖励；与场景/查询无调用关系');}
      else trace.effects.push('DOMAIN业务示例已结算：幂等跳过。并非从原生协议推导');break;
    default:throw new Error(`Unknown operation ${event.op}`);
  }
  trace.after=copy(s);return trace;
}
export function simulate(scenario) {
  let state=initialState(scenario.options??{});const frames=[];
  for(const event of scenario.events) {const frame=applyEvent(state,event);frames.push(frame);state=frame.after;}
  return {initial:initialState(scenario.options??{}),frames,final:state};
}
export function detailVisibility(state,knight) {
  return !(state.scene<3&&knight.hidden===1);
}
function bytes(n) { const b=new Uint8Array(n);return [b,new DataView(b.buffer)]; }
export function payloadPreview(event,context={}) {
  const p=event.payload??{};let b,d;
  const names={state:'0x04B8',stateSnapshot:'0x04B8',info:'0x04B9',bets:'0x04BA',results:'0x04BB',ackInfo:'0x04E6 ACK',ackBet:'0x04E7 ACK',ackHistory:'0x04E8 ACK'};
  switch(event.op) {
    case 'state':case 'stateSnapshot':
      [b,d]=bytes(3);d.setUint16(0,p.round??1,true);b[2]=(event.op==='stateSnapshot'?snapshotValue(p):(p.value??0))|(event.op==='stateSnapshot'?0x80:0);break;
    case 'info':
      [b,d]=bytes(90);d.setUint16(0,p.round??1,true);
      for(const [i,k] of (p.knights??KNIGHTS).slice(0,8).entries()) {const o=2+i*11;b[o]=k.index;b[o+1]=k.condition??0;b.set(k.raw4??[0,0,0,65],o+2);d.setUint16(o+6,k.wins??0,true);d.setUint16(o+8,k.losses??0,true);b[o+10]=k.hidden??0;}break;
    case 'bets':
      [b,d]=bytes(46);d.setUint16(0,p.round??1,true);d.setInt32(2,p.total??0,true);
      for(let i=0;i<8;i++){b[6+i*5]=i;d.setInt32(7+i*5,p.amounts?.[i]??0,true);}break;
    case 'results': {
      [b,d]=bytes(31);d.setUint16(0,p.round??1,true);b[2]=p.stage??0;
      const rs=p.records??FIXTURE.map((r,i)=>i<(p.validCount??[4,6,7,7][p.stage??0])?r:{winner:255,loser:255,winnerAux:0,loserAux:0});
      for(let i=0;i<7;i++){const r=rs[i]??{winner:255,loser:255};b[3+i*4]=r.winner;b[4+i*4]=r.winnerAux??0;b[5+i*4]=r.loser;b[6+i*4]=r.loserAux??0;}break;
    }
    case 'ackInfo':case 'ackBet':
      [b,d]=bytes(5);b[0]=p.success??1;d.setUint32(1,p.result??0,true);break;
    case 'ackHistory':
      [b,d]=bytes(3505);b[0]=p.success??1;d.setUint32(1,p.result??0,true);
      for(const [i,e] of (p.entries??[]).slice(0,500).entries()) {const o=5+i*7;d.setUint16(o,e.round??0,true);b[o+2]=e.knight??e.winner??0;
        const raw=e.valueRaw??'00000000';for(let j=0;j<4;j++)b[o+3+j]=parseInt(raw.slice(j*2,j*2+2),16)||0;}break;
    case 'confirmBet': {
      const [tail,view]=bytes(9);tail[0]=p.knight??context.selectedKnight??0;view.setInt32(1,p.slot??5,true);view.setInt32(5,p.amount??0,true);
      return {name:'0x04E7 C2S',length:23,hex:Array(14).fill('??').join(' ')+' '+Array.from(tail,x=>x.toString(16).padStart(2,'0')).join(' '),boundary:'14B前缀未知；仅显示局部writer结构，无frame/header/seq/checksum，不可直接发服'};
    }
    case 'queryInfo':case 'queryHistory':return {name:event.op==='queryInfo'?'0x04E6 C2S':'0x04E8 C2S',length:14,hex:Array(14).fill('??').join(' '),boundary:'14B未知占位，不是有效线包'};
    case 'eventList':return {name:'0x006C NOTI',length:'变长',hex:'结构示例：eventId=0x093D, enabled='+Number(!!p.enabled),boundary:'仅模拟map存在结果；未编码通用活动完整字符串、时间和条件尾表'};
    default:return {name:'UI / LOCAL / DOMAIN / 共享系统步骤',length:'—',hex:'无马战包body',boundary:'这一步不是新发现的马战opcode'};
  }
  return {name:names[event.op],length:b.length,hex:Array.from(b,x=>x.toString(16).padStart(2,'0')).join(' '),boundary:'合成body演示，仅用于本页模型；不是捕获样本/真实网络帧'};
}
