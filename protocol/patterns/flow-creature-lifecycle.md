# 宠物常规生命周期（A21）

状态（2026-09-30）：P01–P10普通流程已由用户确认通过；源码实例重构为 `923f3c28`，其结构与事务通过自动验证及独立复核。实机验收并未覆盖所有拒绝、多客户端可见性、普通进化、零饱食或技能。验收范围见 [人工列表](../../Document/tasks/creature-system/Human-Test-List.md)，当前收尾与后续边界见 [收尾记录](../../Document/tasks/creature-system/verification/closeout-20260930.md)。

## 身份和证据

- 宠物详情key与物品core.Value使用同一UID；槽位仅定位物品位置，不是宠物身份。登录、移动、脱穿、改名、喂食、重登和普通孵化的玩家可见保持由P01–P10支持。
- 名字通知、死亡/复活/进化通知使用玩家场景uniqueId；饱食状态/详情使用宠物UID，两者不可互换。字段宽度、A21/70差异和置信度在各消息页记录。
- 0064/0066在CMD和NOTI两族各有含义；不能把孵化CMD0066与经验NOTI0066合并处理。
- `ClientVerified`在此仅表示用户确认的普通GUI/重登结果。客户端构包/接收静态证据为`ClientStatic`；服务日志中的实际C2S body为`WireCaptured`；本服编码、单元测试和副本探针为`CurrentImplementation`，不能当作客户端原始实帧。

## 已接线的流程

| 触发 | 请求/前置 | 本服提交后输出与受众 | 验收边界 |
|---|---|---|---|
| 登录、重登、移动和穿戴 | 既有选角/库存命令；同UID库存与详情 | 库存投影与[NOTI0069](../game/server-to-client/NOTI-0x0069.md)给主人，详情与core.Value关联 | P01–P04、P09通过；不是所有创建/删除分支已证 |
| 改名卡25 | [CMD0064](../game/client-to-server/0x0064.md)：i16 slot、u8 list、dstring name；校验当前宠物和卡 | 同事务改名/扣卡；先向主人及当前可见成员发[NOTI0065](../game/server-to-client/NOTI-0x0065.md)，再给主人CMD0064及卡槽刷新 | P05改名/扣一张/重登通过；多人受众和错误码未实机验证 |
| 饲料24 | 既有[CMD002C](../game/client-to-server/0x002C.md)消耗品路径；当前佩戴的普通宠物 | 同事务扣料与更新饱食；[NOTI0067](../game/server-to-client/NOTI-0x0067.md)给主人；饱食跨零时另发场景状态通知 | P07通过；自动喂食、集气加速未闭合 |
| 在线时间推进 | 会话在线时钟、战斗/城镇状态；无独立客户端经验请求 | NOTI0067给主人；穿戴宠物正数→零发[NOTI0064](../game/server-to-client/NOTI-0x0064.md)，零→正数发[NOTI006B](../game/server-to-client/NOTI-0x006B.md)给可见成员 | P06/P08/P09普通变化与离线不累计通过；零饱食死亡/复活待条件样本 |
| 清房成长 | 已提交的房间清理奖励，按(run,cell)去重，保留事件入队时佩戴的宠物UID | NOTI0069及0067给主人；仍佩戴该UID时发[NOTI0066](../game/server-to-client/NOTI-0x0066.md)；提交前不发送 | P06普通经验/升级通过；不据此证明所有装备加成、无疲劳地图或极限级行为 |
| 普通自动进化 | 升级触发、单目标、无任务前置、正确PVF映射 | 同UID修改模板、保留名字/饱食/经验；详情和当前穿戴库存给主人，[NOTI006A](../game/server-to-client/NOTI-0x006A.md)给可见成员；已经移走则不刷新旧穿戴槽 | 自动回归和静态证据；尚未单独实机验收 |
| 普通63022蛋孵化 | [CMD0066](../game/client-to-server/0x0066.md)：实际请求070000；本服严格3B解析 | 同UID、同槽变63023，等级1/经验0/饱食100；给主人CMD0066成功01 → NOTI000E原槽 → NOTI0069详情；不播放场景进化通知 | P10通过；失败只回0002为本服拒绝选择，不声称原服错误码全集 |

状态转换由PetInstance/EggInstance负责，命令保持资源消费与状态写入的事务边界；在线小数余额一同提交/回滚。出生作废旧蛋余额，旧蛋不能再次出生；实例提交或回滚后重新取得。领域设计和代码例子归 [实例交付文档](../../Document/tasks/creature-system/verification/instance-refactor-20260930.md)，不以其推导新的线级结论。

## 留存的未闭合路径

[CMD0065](../game/client-to-server/0x0065.md)和[CMD007A](../game/client-to-server/0x007A.md)仅按已证形状解析、记录；不伪造技能、回应广播或脚本成功。[NOTI0068](../game/server-to-client/NOTI-0x0068.md)虽注册encoder但没有业务发送；不能据70请求/应答关系绑定A21。任务/多选/物品成长型进化、自动喂食、主动技能与脚本受众，以及多人可见性继续留在原任务范围，不由本次普通流程收尾推定完成。
