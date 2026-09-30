# 装备锻造：证据及方案

## 来源边界

| 来源 | 已核对事实 | 限制 |
|---|---|---|
| `70Reference` | 70 ELF `df_game_r` 符号 `Dispatcher_UpgradeItemSeparate::read` `0x081cbf24` 依次 `get_short/get_int/get_short/get_int/get_str`；`WongWork::CItemUpgrade_Separate` 的条件、决策与处理在 `0x0811e138/0x0811e2e2/0x0811e66e`。 | 70 的请求无 A21 新增列表字段及尾标志，不能直接当 A21 布局或概率。 |
| `UpstreamReference` | `ServerS4A21/Server/DfoServer/Network/Parsers/Inventory/SeparateUpgradeRequest.cs` 把 `0x01B7` 解析为 `u8 list, i16 target, i32 itemId, i16 material, i32 nameLength, name bytes, u8 variant`。`InventorySeparateUpgradeService.cs` 普通路径只接受武器、满耐久、未锁定目标，取 `GenuineUpgrade`，消耗 `upgrade_separate.etc` 材料，成功加一、失败保持。`SeparateUpgradeAckBuilder.cs` 的响应为成功标志、材料槽/余量、旧级、成功/失败、当前级、目标列表/槽；失败为标志+错误码。 | 其角色在线/数据库行为不是本服证明；卷券 variant1/2 隔离；舍入规则仅为上游实现线索。 |
| `ClientStatic` | 原版 A21 `DNF.exe` SHA-256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD` 的协议普查给 `0x01B7` 构包 RVA `0x012FDB74`、CMD 响应 RVA `0x0119A040`。在解包 b1 SHA-256 `D7618A102251F1DB724F2514C74A4CC855DF26E6A4BC308F8EE9F2397DB0A99A`、image base `0x00400000` 上重读：构包 VA `0x016FDB66–0x016FDC21` 写 type、列表、槽、模板、材料槽，随后虚调用取动态字符串，再写尾部字节；响应 VA `0x0159A040–0x0159A161` 成功时顺序读 `u16,u32,u8,u8,u8,u8,u16`，失败分支 VA `0x0159A502` 读取错误码。 | 动态字符串的语义及长度来自 70/上游约束推断；无 A21 实帧或 GUI。本服只收与上游/70 共同支持的普通尾标志 `0`，不以名字决定扣料。 |
| `ContentStatic` | A21 `Script.pvf` SHA-256 `F1FD58E6873F0A3EB050265BF912A01F2B2B0782A1BE17578BAF45B4D4BC2D41` 的 `etc/upgrade_separate.etc`：上限 `8`，首级成功权重 `10000`、二级 `5000`；材料等级表中 grade `77` 对应 `3326 × 39` 基数，rarity `2` 权重 `1`，二级材料权重 `1.11`。装备 `101010339` 是 Lv75、grade77、rarity2、满耐久45的刀类武器；材料 `3326` 名为“强烈的气息”，说明用于武器锻造。 | 表未声明金币费用，当前普通路径不收金币。PVF 不证明客户端面板、服务端错误文案或自然材料获取。 |
| `CurrentImplementation` | 现有装备 core 独立保留 `GenuineUpgrade`，与 `EnchantUpgradeCount` 分别持久/投影；命令总线支持账户排它事务及材料消费。 | 必须用本 Spec 测试证明写回/重读及刷新，不把现有字段存在当成锻造闭环。 |

## 可实现边界

仅接 `0x01B7` 普通锻造变体：目标列表 `Main(0)` 或 `Equipment(3)`；材料主背包槽；动态名只作有界结构校验而不作身份依据，尾标志仅 `0`。目标武器须自有、未锁、未过期、满耐久、锻造未到 PVF 上限，材料须为真实 PVF 对应模板及数量。别的列表、卷券 variant、畸形动态尾一律不进入资产事务。

每次合格尝试按 PVF 消耗材料，无金币费用；抽样落在 `0..9999`，`roll < SuccessWeight` 增 `GenuineUpgrade`，否则旧级保留，不降级/销毁。目标原 UID、普通强化、增幅、封印、词条等无关属性保持。命令事务提交后才发与 A21 静态接收顺序一致的 ACK 与权威装备/材料刷新；同序号重放不再扣料，提交异常回滚。错误码保留为静态/上游推断，待实帧校准。

## 待集中实机验证

`0x01B7` 动态名/尾标志实帧、成功/失败 ACK、错误文案、GUI 刷新与重登仍待 A21。卷券变体 `1/2` 不属于当前已闭合路径。不可把此静态结论标为 `ClientVerified`。
