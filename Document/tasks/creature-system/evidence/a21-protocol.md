# A21 宠物协议与 PVF 证据（2026-09-29）

范围：常规宠物的实例、穿戴、孵化、喂食、改名、成长、进化、技能与脚本消息。本文仅供当前宠物重构取证；`70Reference` 是玩法主规则的另一份证据，不从 A21 opcode 名称反推出规则。A21 静态登记所用客户端 SHA-256 为 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。

证据级别：`ClientStatic` 指 A21 客户端注册表、构包/读包指令；`WireCaptured` 指 A21 实际帧；`PVF` 指此客户端目录的 `Script.pvf`；`ReferenceImplementation` 指 `ServerS4A21` 的服务端写法。后者只能指导候选实现，不能证明 A21 请求语义或真实客户端效果。尚无本文所述宠物操作的 `ClientVerified` 闭环。新增的接收端静态复核使用 `DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin`，SHA-256 `AAAE24216E0E260AFA528445775E767D104F658FD68FDAD21EC7BB601B9C8A3D`，image base `0x00400000`；[反汇编切片](a21-pet-noti-handlers.asm.txt) 中列的是 VA，减 image base 才是协议登记表中的 RVA。

## 当前实例关联与面板故障

- 宠物物品实例 UID、物品 core.Value、宠物详情 UID 是三个存储位置；核心关联是 **`core.Value == CreatureDetail.Uid`**，不是模板 ID，也不能只靠物品 UID 推断。上游 `PetInventoryAccessor.TryGetEquippedCreature` 以装备槽 `core.Value` 查 detail，`CreatureListBodyBuilder` 把 detail UID 写入 `NOTI 0x0069`。当前错误样本 `items.item_uid=948/949/950`，但 `core.Value=938/939/940`，导入未回填导致关联失败和面板 null；已有 level=1、satiety=100，不应归因于协议或缺省等级。生产 `SqliteItemDomainStore` 的创建路径会回填 UID。修复样本时要同时核对角色所有容器的同一物品身份和 detail 唯一性，不直接更改生产 DB。来源：上游 `Game/Inventory/Pets/PetInventoryAccessor.cs:9-27,90-133`、`Network/Builders/Init/CreatureListBodyBuilder.cs:28-43`；样本诊断由当前任务提供。
- 选角 `NOTI 0x0002` 的宠物装备项含额外 `Noti2CreatureMarker`。本工作树 `StateSynchronizationPayloadCodec.Entry.cs:191-194` 在槽 25、kind 5 且 marker 缺失时抛错；`SupportCreatureMarkerProjection` 已存在，但 `SelectCharacterPreparationService.CompleteSupportCreatureMarkerAsync` 当前只在支援角色路径调用。普通当前角色也要在发送前完成 marker 投影。marker 的优先级与期限计算见该类源码；这属于当前实现缺口，不是 A21 实帧证明。穿戴位置变动的基本入口为 `CMD 0x0013 MOVE_ITEMSPACE`，请求至少带来源/目标列表与槽、实例值等；物品同步依赖库存通知与选角快照，不可单靠 `NOTI 0x0069` 当作穿戴状态通知。参见 [`0x0013`](../../../../protocol/game/client-to-server/0x0013.md) 和 [`NOTI 0x0002`](../../../../protocol/game/server-to-client/NOTI-0x0002.md)。

## A21 入口与通知矩阵

`CMD` 与 `NOTI` 是不同命名空间，同号也不表示请求/响应一对。以下“可实施”仅指有足够边界实施保守路径；所有物品变更仍须先校验角色、槽、实例关联并提交事务，再发响应与刷新。

| 流程 | A21 C→S CMD 与已知 body | S→C 候选与证据 | 决定 |
|---|---|---|---|
| 穿戴/卸下宠物和宠物装备 | `0x0013 MOVE_ITEMSPACE`：固定前缀 `u8 sourceList, i16 sourceSlot, i32 sourceInstanceValue, i32 count, u8 destList, i16 destSlot, i32 destInstanceValue`；后续字段及宠物路径见专项记录 | 参考服刷新装备/宠物物品库存；`NOTI 0x0069` 是 detail 列表；选角 `NOTI 0x0002` 携带穿戴槽及 marker | 可实施库存移动、UID 不变和快照同步；穿戴后的即时客户端效果需抓包验收 |
| 喂食 | `0x002C USE_STACKABLE`，A21 已知前缀 `i16 slot, u8 list, i32 instanceValue, i32 itemCode`；多构包路径导致后续布局分化。参考服将宠物栏消耗品分流到喂食 | 同号 CMD ACK；参考服发物品刷新、`NOTI 0x0067` 状态、`0x0069` 列表，救活时 `0x006B` | 可按已验证的普通使用物品事务路径接入；宠物饲料的具体 A21 请求与通知顺序尚无 WireCaptured |
| 改名 | `0x0064 RENAME_CREATURE`：构包静态完整 body `i16 slot, u8 list=7, dstring name`，共 `7+nameBytes` B | 同号 CMD 成功响应 `u8 success=1, u16 slot, u8 list`；失败通用响应 `u8 success=0, u8 reason`。`NOTI 0x0065` 为 `u16 + dstring`，参考服填 userId+name | 请求/响应/通知的字节布局可实施；服务端处理仍须验证改名道具、实例、名字规则及真实通知顺序 |
| 宠物回应/喊话 | `0x0065 RESPONSE_CREATURE` 静态恰 `u16`，无同号响应注册；`0x007A CREATURE_SCRIPT_MESSAGE` 发送分支恰 `u8 mode, u16 targetKey=0, u32 characterId, dstring message`（`11+消息字节`） | `NOTI 0x0068` 恰 `u16`；`NOTI 0x0077` 恰 `u8,u16,u8,dstring`；`NOTI 0x0072` 与其共用 handler | 0065/007A 可严格解析并分类记录；目标受众与回应/技能的领域效果没有闭合，不自行 ACK、广播或扣饥饿 |
| 孵化 | `0x0066 HATCH_CREATURE` 有 A21 CMD 响应 handler，但 `.text` 未找到请求构包点，body 未知。上游另外注册 `0x00AD HATCH_CREATURE_EGG` / `0x00AE REQUEST_HATCHED_CREATURE`，A21 普查没有这些入口 | 参考服 `0x0066/0x00AD` 回同号 ACK 后发库存和 `NOTI 0x0069`；这是参考行为 | PVF 可确定蛋与产物模板映射；**不能**以参考服“多 offset 猜蛋 ID”的解析器宣称 A21 wire 已知。需实际孵化抓包 |
| 进化/成长 | `0x01E0 VERIFY_CREATURE_QUEST` 结构未知；上游枚举还有 `0x0143 USE_CREATURE_EVOLUTION_ITEM`、`0x00E2 COMPOUND_CREATURE`，当前 A21 普查未确认入口 | A21 `NOTI 0x0066` 有 **6B/8B 条件形状**，见下节；`0x006A` 恰读两个 u16；`0x0064 DIED_CREATURE` 与 `0x006B REVIVAL_CREATURE` 各恰读一个 u16 | 可编码普通经验形状；70 普通自动进化同 UID，006A 第一值为目标脚本 species ID 的业务线索，但 70 写 u8、A21 读 u16，仍需 A21 实帧验收 |
| 宠物技能 | PVF `creature/*.cre` 给技能、超技能、等级、MP、冷却等静态声明；目前没有证据将普通宠物施法绑定到某个专用 Game CMD。`0x007A` 是脚本消息候选，不等于所有技能施放 | 参考服存在脚本欢迎/聊天 NOTI；技能主动施放可能复用角色技能/战斗协议，未决 | 保留为待观察：装备宠物→入图→释放技能，同步抓 C2S/S2C 与场景事件 |
| 扩展操作 | A21 枚举/普查列有 `0x01FE USE_RENAME_CARD` 2B、`0x0200 EQUIPED_CREATURE_CHANGE_INFINITY_CREATURE` 0B、`0x022C CREATURE_FILLPOINT` 4B、`0x03DC SEAL_CREATURE` 结构未知；枚举另含 `0x0433 CREATURE_EXP_BOOK` 等 | 各响应和效果尚未闭合 | 不并入基础宠物闭环；按实际玩法触发逐条取证 |

矩阵静态入口可回查 `protocol/patterns/data/probes/a21-identity-census.tsv` 与 `protocol/game/client-to-server/0x0064.md`、`0x0065.md`、`0x0066.md`、`0x007A.md`、`0x002C.md`、`0x01FE.md`、`0x022C.md`。上游参考入口见 `ServerS4A21/Server/DfoServer/Network/Protocol/GameProtocolHandler.cs:562-582`、`Network/Handlers/Pets/PetCreatureHandler.cs`。仅枚举存在或上游注册，不能替代 A21 构包证据。

## 接收端字段闭合

以下是从函数入口到最后读字段的 A21 客户端指令路径；`0x027C5230/52A0/5310` 分别推进接收游标 1/2/4 字节。`0x0274FCB0 → 0x0274FC10` 从游标读 LE u32 字节长度，再读该长度的名字原始字节，写入缓冲区后转宽字符。因此调用参数 `0x14`、`0x1E` 是名字缓冲上限，不是线上的固定 20/30 字节。反汇编原文见[切片](a21-pet-noti-handlers.asm.txt)。

| NOTI | handler VA | A21 实际读取顺序 | 业务含义的证据边界 |
|---|---:|---|---|
| `0x0064 DIED_CREATURE` | `0x0114F130` | `u16` | 以该值找到角色后将宠物死亡标志 `+0x2DC` 设为 1；参考服填 userId，A21 值身份尚无实帧 |
| `0x0065 RENAME_CREATURE` | `0x0118E840` | `u16, dstring name`；名字 helper 的目标容量为 20 字节 | 名字写入角色关联宠物对象；参考服填 userId+名字，客户端只证明边界 |
| `0x0066 GAIN_EXP_CREATURE` | `0x01166750` | `u8 level, u8 mode, u32 exp`；仅当 `mode==1` 再读 `u8 extra0, u8 extra1`；总长为 **6B 或 8B** | 第一个字节与已有等级比较并传给成长更新函数；u32 与等级一起传入该函数。两个 extra 写入对象字段，具体奖励/成长模式意义仍 Unknown |
| `0x0067 CREATURE_STATE` | `0x01146850` | `u32 creatureKey, u32 stateValue`，恰 8B | key 用于查宠物对象，value 交给 `0x01958080`：存入对象浮点状态并记当前时间；参考服填饱食度，A21 需要饱食实帧确认值域 |
| `0x0068 RESPONSE_CREATURE` | `0x01189D70` | `u16`，恰 2B | 后续调状态方法；值的身份/响应关系未证 |
| `0x0069 CREATURE_ITEM_LIST` | `0x01163430` | `u8 count`；逐项 `u32 key, u8 field04, u8 mode, u32 progress, [mode==1 时 u8,u8], u8 level, dstring name, u8 tail` | A21 对每项按 key upsert，count=0 不清表；字段顺序与参考服 builder 同形，见下节 |
| `0x006A EVOLUTE_CREATURE` | `0x01146890` | `u16, u16`，恰 4B | 第二值用于找到场景对象，第一值写该对象 `+0x2D4`；70 首值是目标宠物脚本 species ID、第二值是玩家场景 uniqueId，但 70 首值仅写 u8，A21 的 u16 是版本差异，需实帧确认 |
| `0x006B REVIVAL_CREATURE` | `0x01146950` | `u16`，恰 2B | A21 以它查角色对象，将宠物死亡标志 `+0x2DC` 清为 0；参考服填 userId |

`0x0066` 的“至少 8B”静态普查抓到了 `mode==1` 分支；参考服 `level, 0, exp` 六字节走 `mode!=1` 分支，两者没有长度冲突。客户端还把 `mode!=0` 写入对象 `+0x22`，所以不能把所有非 1 值都解释为普通模式；当前可直接编码的普通经验通知使用 `mode=0`。普通服路径无条件写 6B 是否涵盖升级以外奖励，仍需运行差分。

`70Reference`：[`MakeNotipacketEvoluteCreature`](evolve-notify.asm.txt) 的 `0x08340842..0x083408B4` 写 `u8 targetCreatureId,u16 ownerUniqueId`；`CCreature::Evolute` 把目标脚本 creature ID 传入，修改**同一个**宠物对象和其穿戴物品模板，不重置 UID、经验、等级、饱食和名字，详见 [70 调查](tw70.md)。这是首值的业务依据，不是 A21 线宽依据。当前 A21 按接收端静态读取发送 `u16 targetSpecies,u16 ownerUniqueId`，实机仍需检验效果。

### 改名 CMD 的完整构包与响应边界

`ClientStatic`：函数 VA `0x023CFEF0` 先检查面板状态 `[esi+0x1B8] == 2`，再要求 `[esi+0x1CC] == 7`。该分支于 `0x023CFF10` 调用 `MakeHeader(0x0064)`，依次调用 `WriteU16([esi+0x1C8])`、`WriteU8([esi+0x1CC])`、`WriteDString([esi+0x1D0])`，随后在 `0x023CFF5B` 跳入最终发送；**没有尾字段**。同函数 `0x023CFF60` 开始的另一个分支属于 `CMD 0x0005 CREATE_CHARACTER`，其中多次写入的字节不能算入改名请求。既有自动记录 `protocol/game/client-to-server/0x0064.md` 混入了这个分支的字段，需以此处按分支追踪的证据覆盖。字节切片见[改名构包与响应反汇编](a21-pet-rename-builder.asm.txt)。

`ClientStatic`：改名 CMD 响应 handler VA `0x0112CA20` 使用公共分发器传来的 success 标志，成功分支从正文依次读取 `u16 slot,u8 list`；公共失败格式为 `u8 success=0,u8 reason`，成功正文前缀为 `u8 success=1`。因此成功 body 恰 4B，失败 body 恰 2B。参考实现 `PetCreatureHandler.BuildCreatureRenameAckBody` 与成功形状吻合。名字 `dstring` 是 LE i32 字节长度加原始字节；当前构包函数未证明名字的最大可接受长度或服务端命名规则，这些留给领域校验，协议解析只检验长度和完整消耗。

`PVF`：`stackable/stackable.lst` 将 ID `25` 映射到 `stackable/cash/creature/rename_card.stk`，其 `[stackable type]=[creature] 1`、说明为宠物换名卡。本服 list 7 的 189–239 槽映射 `CreatureConsumable`。`70Reference` 的 `CCreatureMgr::RenameCreature` 在写名字之前先用 `CInventory::use_item` 消费源物品，失败即退出；`CCreature::RenameCreature` 的名字字段是 12 字节。A21 参考服只判断 kind 7、最多 13 字节，并不验证具体模板，因此改名成本以 PVF ID 25 与 70 先消费为门，不能把任何宠物消耗品当换名卡；最大字节取 12 的保守边界，仍需 A21 实机验收。

`CurrentImplementation`：改名命令在事务内验证当前会话、宠物消耗品来源槽、ID 25、卡数量、同 UID 穿戴宠物和两者期限，保存名字并扣一张卡；成功后先向主人及当前副本参与者或城镇互见成员发 `NOTI 0x0065`，再给主人发同号 CMD 成功响应与卡槽刷新。城镇互见使用 `TownAreaSnapshot.VisibleTo`，主人只发一次；某接收者发送失败只关闭该接收者连接。这个受众与顺序是**本服实现选择**，不是 A21 `WireCaptured` 或 `ClientVerified`。

### 回应和脚本消息：请求形状已闭合，玩法仍待证

`ClientStatic`：[`CMD 0x0065` 构包切片](a21-pet-script-builders.asm.txt) 的 VA `0x01948EA0..ED2` 在 header 后调用虚表 getter 取一个值，只写 `u16` 然后 Finalize。虚调用影响字段**值**，不再追加字段。当前静态不能证明这是技能施法、也没有同号 CMD 响应注册，故不能把 `NOTI 0x0068` 视为必然 ACK。

`ClientStatic`：[`CMD 0x007A` 完整函数](a21-pet-script-prefix.asm.txt) VA `0x022C7D10..0x022C7F11` 的发送分支在状态映射后写 `u8 mode,u16 0,u32 characterId,dstring message` 并 Finalize，**没有**参考实现解析器声称 mode 1/7 时的第二个名字字段。该函数另有绕过 007A 构包的虚方法分支，所以不能把所有宠物脚本/技能动作等同于此报告。`NOTI 0x0077` handler VA `0x01191DD0` 与 `0x0072` 共用，直接读 `u8,u16,u8,dstring`（目标 buffer 256B），见[接收切片](a21-pet-script-noti.asm.txt)。上游把 007A 回发为 0077 仅证明参考行为；城镇/副本受众与触发条件仍缺 A21 实帧。

`70Reference`：`Dispatcher_ResponseCreature` 不读取 `CMD 0x0065` body，`CCreatureMgr::ResponseCreature` 只向场景广播 `NOTI 0x0068 {u16 ownerUniqueId}`，不改宠物技能或持久状态。A21 构包恰发一个来自虚 getter 的 `u16`，与 70 的空 body 路径不等价，客户端是否以 0068 表示该动作仍缺实帧。`CurrentImplementation` 因此只严格解析并记录 `CMD 0x0065/0x007A`，不自行 ACK、回复 0068/0077、广播、消耗饱食或触发技能。

### 孵化 CMD 的静态停点

`CMD 0x0066 HATCH_CREATURE` 的 A21 CMD 表响应函数 RVA `0x00D1B420`（VA `0x0111B420`）读取的是调用者传入的成功标志 `[ebp+0xC]` 和错误码 `[ebp+0x10]`，没有直接解析孵化请求。`census-identity.py` 通过全部 `MakeHeader(0x0234D1E0)` 直接调用点恢复 C2S type；`protocol-identity.tsv` 找不到 `0x0066`、`0x00AD` 或 `0x00AE` 的构包点，仍有一个 indirect、四个 no-push-in-block 和十五个 register 类未解调用点。故此处的精确停点是 **请求构包点缺失/可能在间接或混淆代码区**，而不是接收 NOTI 原语不可解。`0x00AD/0x00AE` 目前只有上游 `PacketTypesA21.cs` 枚举及参考服注册，不能宣称本 A21 客户端会使用。直接验收孵化需要捕获客户端实际发出的 CMD body；参考服在多个偏移探查蛋模板 ID 的策略不能作为编码定义。

进一步沿蛋的右键使用通道检查：A21 PVF `equipment/creature/egg_bobo.equ` 是 kind 5、`[equipment type]=[creature] 0`。客户端通用物品动作的[静态切片](a21-item-use-switch.asm.txt) VA `0x017007E5..0x01700910` 在 `0x017008EB` 调 `0x00EE6E00`，后者从物品定义对象 `+0x390` 取类别值；类别 `3` 时设置 UI 标志，然后该路径构建 `CMD 0x002C`。同一段更早的 `0x0170089F..AA` 通过 UI 虚表 `+0x7B4` 分流物品专属动作，静态调用图在此 **未达实际实现 callee**。目前没有证据将蛋的 kind 5／PVF 子类型 0 对应到 `+0x390 == 3`，也没有证据称虚方法就是孵化；故不能把普通 `USE_STACKABLE` 直接用于蛋。孵化响应[切片](a21-hatch-response.asm.txt) 中 `0x0111B423` 取得 UI 编号 7 后调用 `0x016F3360`，其作用仅为弹出/清理 UI 栈的一项，未携带蛋 itemCode 或 source slot；据此反向无法补请求体。

最小实机样本是：保持一枚 PVF 已知蛋（例如模板 63022）在宠物背包且 UID 一致，开启现有 ClientHack 原始帧日志，右键该蛋并确认孵化一次，保存该动作前后所有 C→S/S→C 的 type、完整 body、宠物栏物品快照与 detail UID。只需这一个操作就可判定实际入口是 `0x0066`、`0x002C` 或别的 opcode，并闭合 source slot/list/itemCode 的边界；无需启动新客户端版本或修改生产数据库。

## `NOTI 0x0069` 精确边界与刷新

`WireCaptured`：A21 参考服入场帧 `#67` 与实机入场帧 `#44` 均为 body `00`、长度 1，证明空列表形状及它在 `NOTI 0x0002 USERINFO` 前发送；见 `protocol/patterns/data/traces/a21-reference-entry-bytes-20260825.md` 与 `a21-live-entry-20260825.md`。这些样本不能证明非空条目布局。

`ClientStatic`：A21 接收 handler RVA `0x00D63430`，首字段是 `u8 count`，随后是上表所列逐项循环。`0x0116349F` 按 mode==1 有条件读取两个字节，`0x011634C5..D7 → 0x0274FCB0 → 0x0274FC10` 读取带 u32 长度的名字，`0x011634D7` 读取尾字节；这已越过自动提取器的间接调用边界。`ReferenceImplementation` 的 `CreatureListBodyBuilder.WriteCreatureEntry` 与这一字段顺序相同。`dstring` 是 LE i32 字节长度加原始字节；每项线长为 `16+nameBytes`，mode 1 时 `18+nameBytes`。旧批次的 `n×44` 是固定最大宽度近似，不能作为编码长度。自动文档仅列 `1+n×14` 下界且遗漏尾 `tailFlag`，应以本次指令闭合为准。

该消息的名字虽叫 LIST，但客户端把每项按 key 更新：count 为零在 VA `0x0116346C` 直接退到函数尾，未清容器；逐项在 `0x01163563..67` 把首个 u32 key 与新对象传给 `0x01958E80`，后者以 key 调 map `operator[]`（`0x00864F80`）并写对象指针，见[按 key 插入切片](a21-creature-list-insert.asm.txt)。因此**count=1 可以发送单宠物条目更新**，不要求在每次成长/进化时重发整表；count=0 不能用作删除或清空通知。参考服总发整表是其实现选择。非空 A21 实帧与可见效果仍待验收。

这里的 `detailUid` 应当与宠物物品 `core.Value` 对齐。即使 `NOTI 0x0069` 把 948/949/950 写进去，若物品 core.Value 仍是 938/939/940，客户端/服务端实例关联仍会失配。先修一致性，再以一只实际非空宠物的入场帧、穿戴后的刷新帧和客户端面板表现验收。

## PVF 实读

以 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf` 调用本工作树 `Tool/PvfProbe`，读得：

- `creature/creature.lst` 有 486 个 `.cre`。`20 → Bobo/Bobo.cre`；它的 `[start level]=1`、`[max level]=20`、`[artifact slot]=red blue green`、`[learn overskill level]=20`，有 `[skill type]=[passive]`、`[over skill type]=[passive]` 与脚本/喊话文字。这些是内容静态声明，不是运行时经验曲线或协议载荷。
- `equipment/creature/egg_bobo.equ` 的 `[equipment type]=[creature] 0`、`[creature species]=20`、`[output index]=63023`。该蛋的商品物品 ID 是 `63022`（既有 A21 商城内容证据）；`63023` 是声明的产物模板候选，**不表示购买即自动孵化**。实例 UID 在生成时另外分配。
- `stackable/cash/creature/creature_food.stk`（`stackable.lst` ID 24）的 `[stackable type]=[feed] 1`、`[int data]=300 -20 30`；描述文字指增加 30 饱食度、减少 20% 技能集气时间、持续 5 分钟，并在饱食度降至 10 时自动使用。参考服从 int data 第三项取 +30；自动消耗与 buff 仍需运行行为确认。
- `equipment/creature/artifact_blue/claw_roc.equ` 是 `[artifact blue] 15`、`[creature minimum level]=20`、`[creature physical attack]=105`。宠物装备有独立类型、等级门和数值；商城/背包分区须按 PVF 类型处理。
- `creature/Zagan/Zagan.cre`（ID 9）声明 `[evolution creature id]=10`、`[evolution level]=20`、`[evolution quest]=0`。整库标签普查：272/486 个 `.cre` 含 `[evolution creature id]`，82 个含 `[evolution level]`；不能把“有进化目标字段”视为全部都可立即进化。经验、进化、技能规则须逐项解析并用真实玩法校验。

## 下一轮最小实机样本

1. 一只 UID 一致的宠物，记录选角 `NOTI 0x0002` 穿戴项、`0x0069` 非空整帧，退出重进确认稳定；差分 `mode=0/1`、名字空/非空和尾字节。
2. 对同一实例执行穿戴/卸下、饲料、改名、孵化各一次，保留 C2S、CMD 响应、NOTI 与前后库存/detail 快照。特别确认 `0x0066` 对 `0x00AD`，以及喂食是否真的走 `0x002C`。
3. 在副本中记录饱食度、经验、等级与主动技能事件；以 PVF 一只可进化宠物触发进化，核对 `NOTI 0x0066` 的 6B/8B 分支触发条件及两个额外字节的语义、`0x006A` 两个 u16 的身份和值。

资料定位：A21 专项协议文档均在 `protocol/game/`；参考实现位于 `E:/Project/DNF/ServerS4A21/Server/DfoServer/`。`Tool/PvfProbe/Program.cs` 是读取方法，PVF 文件保持只读。本文不把上游服务端的宽松解析、推断发送序列或旧 A12 帧提升为 A21 已证事实。
