# 异界专属机关静态盘点

日期：2026-10-10。证据等级：ContentStatic / High；未追完客户端控制类，也未逐房实机验证。
目标是区分客户端本地动作与可能需要服务端协调的机制，不在本轮新增机关或patch。

## 输入、范围与复核

输入为部署使用的Script.dimension.pvf，SHA-256 ADAC21543D89AE1A8228C6D6031757BB54A5A15F996AF062B331B09795EBF096。
[inventory.json](inventory.json)保留DGN、MAP及直接对象/怪物声明引用：13个带[dimension dungeon]标记的DGN、138张MAP、198个OBJ、145个可识别怪物ID。
DGN62～67是普通六图，180～185是教学变体，7100是融合入口。这是内容清单，非全部入口开放或全图实机通过。

读取范围为MAP的[dungeon]归属、[passive object]、[special passive object]所有头及其子怪，以及可识别的普通/条件怪物声明；不是生产解析器的替代品。未递归展开OBJ/ACT/AI中的生成链，也未盘点APC全集。非美术section原token仅保留七张候选MAP，六个重点OBJ另存独立JSON。数值保留token.Value原值（若为Float32则是位模式），字符串解析为文本；int data不猜全部位置语义。

可复核工具：[Program.cs](audit-tool/Program.cs)、[Audit.csproj](audit-tool/Audit.csproj)。只读PVF，不连接服务、写DB或改资源。
在仓库根先构建Dfo.Pvf库，再执行（.NET10）：

~~~powershell
dotnet build Server/src/Dfo.Pvf -c Release
dotnet run --project Document/outputs/dimension-mechanisms-20261010/audit-tool -- <PVF绝对路径> <输出目录>/inventory.json
~~~

工具默认引用当前工作区Release版Dfo.Pvf.dll；可通过MSBuild属性DfoPvfAssembly显式指定另一编译产物。需先创建输出目录；输出还包含六个OBJ的section与token JSON。固定输入重跑inventory及六个OBJ应逐字节一致。

## 初查结果

未发现138张MAP直接引用电梯对象1111/1112/1113。已实现电梯机制是精确识别控制对象1113，再以NOTI00FE更新阶段/结果；不能从“无1113”推导成“无任何服务端机关”。

| MAP / 所属DGN | 直接引用 | 初查与证据 |
|---|---|---|
| 16101 / 62；53071 / 7100 | 61982 | 哥布林碉堡，MAP包含子怪模板61337/61338/61339；[OBJ](Im_GoblinBunker.obj.json)有召唤数据及销毁后创建61986的声明 |
| 17054 / 66；39101/39139 / 180 | 10285；17054另含10286 | [管理器](swaycle_box_mgr.obj.json)和[箱子](swaycle_box.obj.json)；空basic action并不证明没有客户端硬编码控制 |
| 17058 / 66 | 14133 | [Boss地板](AutoTile_Boss.obj.json)引用Action/_AutoTile_Boss.act；尚未追踪ACT执行与控制类 |
| 15083 / 67 | 10362 | [米歇尔对象](micha.obj.json)有MAP子怪模板；变身与同步规则待核 |

[61985自爆对象](selfkill.obj.json)另作相邻动作参考，不将文件名直接解释成服务端协议。

CurrentImplementation：PvfDungeonRoomActorSource.IsVerifiedSceneWave只将精确的1112电梯召唤及GentDefence已确认家族标记为场景波次；61982不在该名单。由此需要核对异界模板预创建与场景生成是否一致，但尚无足够证据裁定已发生重复生成或必须新增哪条消息。

ClientStatic：[NOTI0222](../../../protocol/game/server-to-client/NOTI-0x0222.md)入口RVA0x00D62230只确认u8读取下界，完整用途未知。UpstreamStatic：对上游的搜索只找到枚举声明、没有发送实现；搜索未命中不是原生不需要的证明。不得凭名称把它绑定虚无之境Boss，也不得用安全密码GOBLIN_PAD相关协议替代地图机关。

后续按 [#145](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/145) 追踪，验收与部署身份见 [收尾记录](../../DimensionDungeonCloseout-20261010.md)。抗魔削属性是用户本轮ClientObserved，不属于本次静态盘点得出的公式结论。
