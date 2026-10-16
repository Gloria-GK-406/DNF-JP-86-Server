# 订婚状态与婚前任务

2026-10-16；源码基线66dc3f76。A21客户端与当前PVF为依据；实现及自动化已验证，新增客户端可见结果待验收。

## 状态和菜单

接受求婚提交持久Engaged后，答复者收到CMD03F1、03D0快照、可接任务列表；另一端收到NOTI03CE、相同来源的状态和任务刷新。03D0按当前角色投影持久对象CharacterId与阶段1，客户端阶段1..3原生显示“退婚／举办婚礼”，移除“申请订婚”。成功窗口本身没有更新婚姻manager，这是先前菜单未切换的原因。

选角/重登使用同一持久关系生成03D0及婚前任务资格；未绑定角色发送15零清理客户端残留。对象离线不影响关系恢复。03D0更新会触发C2S03F3，现已补齐18B请求与98B成功回包，返回绑定对象的名字、职业、等级、进阶和13槽装备模板。只允许当前角色自己的绑定对象查询。

阶段1是本期持久订婚的初始投影，阶段1/2/3细分及其他字段的全部原生语义尚未闭合。本期只完成菜单状态切换；“退婚／举办婚礼”操作业务仍由后续阶段接入，不能据入口可见宣称婚礼或退婚可用。

## 婚前任务

当前PVF `etc/weddingsystem.etc`声明Lv30与任务20737、20738。仅持久Engaged角色取得这两项Wedding任务资格；继续经过PVF等级、前置、已完成和已接限制。接取与交付再次校验关系，不能绕过菜单直接领取。

- 20737：`condition under clear` subtype22，参数[-1,-1,5]，初始计数5。接取时绑定关系ID与时间。双方恰好两人共同完成普通地下城、服务端确认Boss房清场才减少各自已接任务计数；按角色／任务／关系／run持久去重。接取前事件、单人、错误对象、非Boss房和重放不增加进度；客户端trigger不能自行完成试炼。放弃重接重置本次绑定和计数账本。
- 普通图认定复用本服PVF分类，要求Maze、排除特殊dungeon type／dimension／非零ancient，校验实际地图归属。部分普通图地图候选由动态表提供，因此使用清场生产端冻结的Boss房事实，不能只查静态迷宫候选数组。该范围为本服一期策略，未宣称官方全部计数规则已还原。
- 五次完成后复用通用任务结算，发10002616真爱考验箱；当前PVF通用booster已支持该箱，无物品ID特例。
- 20738在20737完成后开放，消费10002610心意娃娃×1，发10002901×5；继续使用通用材料持有、事务扣除、ItemDelivery和奖励路径。缺材料拒绝，重复交付不重复奖励。

SQLite新增`marriage_quest_bindings`和`marriage_trial_clears`，建表归属仍在Persistence。接取绑定、通关账本/trigger、交付扣物/发奖均参与已有事务；回滚不留下额外计数。可靠DungeonMapCleared订阅与QuestProgressChanged刷新复用既有EventBus，不引入客户端提交计数或另一套奖励实现。

## 协议证据与验证

[客户端只读探针](outputs/marriage-engagement-20261016/probe_client.py)、[输出](outputs/marriage-engagement-20261016/client-evidence.txt)覆盖03D0读取/写回、NPC菜单分支、03F3发送与固定97B消费。客户端镜像SHA256为6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507。协议人工记录：[03D0](../protocol/game/server-to-client/NOTI-0x03D0.md)、[03F3请求](../protocol/game/client-to-server/0x03F3.md)、[03F3答复](../protocol/game/server-to-client/CMD-0x03F3.md)。自动生成区保持原样。

自动化证据在Git忽略目录`artifacts/marriage-engagement-20261016/tests/`：Application全量3490通过／424跳过，Protocol1484通过／60跳过，Persistence468通过，Networking92通过；当前PVF婚姻应用13通过、内容2通过。覆盖持久重开/对象离线、选角空态清理、名字UTF-8截断、严格包长、未选角真实TCP拒绝后连接可继续、资格和前置、通关去重/回滚、真实PVF两步交付与防重复奖励。跳过项不计通过。

双端求婚/拒绝/接受已有用户反馈与运行日志/数据库核对；新增03D0、03F3、菜单切换、五次任务、材料交付与重登UI仍需客户端操作验证。没有新增完整正常实帧，不标记WireCaptured或整项ClientVerified。婚礼、退婚、婚房和婚后权益不在本期实现范围。
