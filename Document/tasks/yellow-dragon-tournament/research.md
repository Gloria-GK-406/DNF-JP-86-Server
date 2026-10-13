# 黄龙大会取证与实现边界

## 输入

- 上游：`E:/Project/DNF/ServerS4A21`，HEAD `c20dba5fa52c2faf4b5087a3b477676be8434e8f`。
- 客户端：`E:/Project/DNF/DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin`。客户端原版身份 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。
- 当前服务端内容：`E:/Project/DNF/DNF-JP-86-Server/artifacts/avatar-socket-text-20261011/Script.server.pvf`。
- 历史探索：`Document/SpecialSoloDungeonsExploration-20261011.md` 及其 outputs 证据。当前协议字段由本轮重新核对，不能继续沿用旧生成表的平坦偏移推测。

## ClientStatic / High

0183 handler RVA `D56690`，先读 `u32 dungeonId,u8 difficulty,u8 partyLimit`；固定4轮，每轮 `u8 round,u8 teamCount`，每队 `u8 position` 后每成员 `u32 code,u16 strength`；再固定4轮路径，每轮 `u8 round` 后每成员 `u16 actorId,u32 code,u8 level,u8 type`。黄龙单人总长260字节。头读取VA11566C5/11566D0/11566DF；循环VA11567AD/11569CC；路径VA1156828..1156852。

0184 RVA `D5AB20`，头 `u8 x,u8 y,u32 seed,u8 flag,u8 visitKind`；visitKind=1或2后读 `u32 mapId,u8 actorCount`（14字节每actor）、`u8 p2pCount`（18字节每peer）、`u8 finalFlag`。本期首次无额外actor/peer时15字节；kind0只有8字节，使用缓存地图。头VA115AB85..115ABB0；分支VA115AC34；actorVA115ACB4..115ACFB。

0185 RVA `D49030`，`u8 completed,u32 exp,u8 firstCount` 后可选14字节记录；再两行，每行 `u8 count` 后 `u32 itemId,u32 amount,u16 reserved`。上游firstCount恒0。第二行count0走失败UI，非0走冠军UI（VA11491B0..11491DE）。金币item0，空奖励signed -1。

01CA请求空体；回复success后handler RVA `D04F70` 无条件读8个u8，并进入奖励UI state6，因此成功响应必须9字节。无效状态也保留8个FF避免短包。

01CB请求 `u8 row,u8 index`；回复handler RVA `D04FB0` 仅success非0时读两行，每行count后每卡u8选择者slot，FF代表未选。失败分支不读卡片数组。

本地状态机VA1544810在VA1544957自增当前轮索引+210，准备路径 `1544891→1544160→15441B2→1543480`，从0183缓存的code/level创建对手并按actorId登记。服务端一次登记四个路径actor，客户端本地逐轮激活；不应每轮发送普通换图/ADD_MONSTER。

## Upstream / High

上游根 `Server/DfoServer/`：

- `Game/Dungeon/Tournament/TournamentDungeonApplicationService.cs:23–106`：准备定义/抽签/runtime，关闭普通boss通关，四轮PathActors投影进同一MAP。
- `Network/Handlers/Dungeon/DungeonEntryHandler.cs:3454–3482` 与 `DungeonMapHandler.cs:859–885,960–1003`：跳过普通001C/001D，发送0183→0184。
- `Game/Dungeon/Tournament/TournamentDungeonRuntime.cs:234–323`：只接受当前轮sequence；重复死亡不推进，第四轮终止。
- `Network/Builders/Dungeon/TournamentPacketBuilder.cs:9–104`：与上述client字段吻合；最小0184是15字节。
- `Network/Handlers/Dungeon/TournamentDungeonCoordinator.cs:215–398`：01CA/01CB与每行一次奖励；冠军两行各两卡，失败只有上行两卡。
- `Game/Dungeon/Tournament/TournamentDungeonApplicationService.cs:443–602`：上行resultKey=已赢轮数，冠军下行key5；先抽金币/物品/空，再抽材料数量；金币基础×DGN倍率。

上游不足：RoundFatigue只解析未扣除（规范 `Docs/副本架构业务接入规范.md:876`）；首轮刻意安排最弱对手，高strength必胜；无效奖励CMD常直接return。新实现不继承这些不足。

## 当前规则选择 / 非原生证明

15候选与玩家均匀洗牌；NPC按双方strength比例决定晋级，玩家路径按四胜预生成。每开始一轮扣PVF疲劳，余额扣至0仍完成本局。死亡复用现有10秒复活窗口，超时按已赢轮数结算。奖励状态与发放同事务，本进程幂等，不承诺重启恢复比赛。

以上没有本轮WireCaptured/ClientVerified证据。部署后由用户验证赛程动画、四次对手切换、奖牌选择和失败退出。

## 自动翻牌边界

ClientStatic/High：奖励倒计时处理 VA1559CBE–1559DE0 只推进本地界面状态，选牌01CB发送在点击处理 VA15557D0。3秒自动补选采用上游 Coordinator 的服务端策略，不表示原生客户端倒计时恰为3秒；本项目自动补选经同一领域选牌命令和提交后投影。
