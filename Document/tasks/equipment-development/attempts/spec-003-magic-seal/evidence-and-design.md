# 魔法封印证据与首轮实现裁定

日期：2026-09-30。代码基线：`8ab85ebd`。70 样本为 `Research/1995chen-dnf/other/等级补丁/70/df_game_r`，仅作 `70Reference`，不推定为 A21 官方服务端。

| 主题 | 证据及等级 | 本轮裁定 |
|---|---|---|
| 装备资格 | A21 PVF `equipment/character/swordman/weapon/katana/101010339.equ` 有 `[random option] 1`、等级 75、rarity 2；全装备列表 118479 项中 3355 项含该标记（PvfProbe，`ContentStatic`）。现有 `SealFlag` 同时参与物品封装/寄件判断（`MailItemRuleService.cs`），不等于随机属性状态（`CurrentImplementation`）。 | 用 PVF `[random option] 1` 判断模板资格，用实例随机属性空/非空判断未解封/已解封；不改 `SealFlag`。 |
| 解封请求 | A21 `protocol/game/client-to-server/0x0191.md`：RVA `0x012EEF9E`，两个 `u16`，共 4 字节；第二字段在现有生成记录为 Hypothesis。70 ELF `Dispatcher_UnsealRandomOption::dispatch_sig` `0x08228e5e` 依次 `get_short` 两次，`0x08229644` 按第二槽检查可选卷轴，`0xFFFF` 不用卷轴；后续扣钱、`SendCmdOkPacket(0x0191)`、`0x000E` 物品通知（`70Reference`）。上游 `InventoryHandler.RandomOption.cs` 只读目标槽，忽略第二字段并发一字节 ACK（`UpstreamReference`）。 | 70 支持卷轴槽候选解释，但不能升级为 A21 事实。本轮只允许无卷轴 `0xFFFF`，其他第二字段安全拒绝且不扣物。 |
| 卷轴 | A21 PVF `stackable/10015723.stk` 名为解封卷轴，`[unseal scroll level limit] 4`；`stackable/professional/common/alchemy_unlock1.stk`（ID `2600460`）为 Lv30 以下卷轴、limit 0（`ContentStatic`）。70 的 `_check_unseal_scroll_item` `0x08229644` 查背包堆叠物和 `GetUnsealScrollPossibleLevel`（`70Reference`）。A21 `0x0191` 第二字段尚是 `Hypothesis`，PVF 不能证明它是卷轴槽。 | 本轮只接受 `0xFFFF` 无卷轴请求；所有非哨兵第二字段拒绝且不扣材料。卷轴 PVF 解析留作后续证据，不接资产结算。 |
| 随机规则和费用 | A21 PVF `etc/randomoption/optiongroupselection.etc`、`optiongrouping.etc`、`optionquantity.etc`、`randomoption.lst` 与 `options/*.etc` 给候选/权重/数值；`randomizedoptionoverall2.etc` 含 `[break seal cost]` 与 `[option modification]`（`ContentStatic`）。上游 `RandomOptionResolver.cs` 有可对照解析，不能把其使用 `Random.Shared` 或默认值当客户端规则（`UpstreamReference`）。 | `EquipmentMagicSealRuleService` 是 PVF 内容唯一读者，接受装备 facts 与显式种子，返回不可变决定/费用；领域服务只负责实例/资金结算。使用加密随机源生成种子并入收据，测试固定种子可复现；缺少可解析声明则拒绝。 |
| 词条变更 | A21 `0x01B6.md`：RVA `0x012EF0CE`，`u16+u16+u8`。客户端响应 handler RVA `0x00C45060`（VA `0x01045060`）在 `+0x03` 检查 `[ebp+0x0c]` 通用成功标志；成功分支 VA `0x0104506D–0x010450FF` 不读 payload 字段，错误分支 `0x01045100` 取 `[ebp+0x10]` 单字节 reason（`ClientStatic`）。同类 `0x0110` 记录已确认通用 CMD 状态在载荷首字节，成功/错误分别由分发层传参。70 `Dispatcher_ChangeRandomOption::read` `0x081dc8de` 同为两个 short 和一个 byte（`70Reference`）。 | 成功 `01`、失败 `00+reason` 的最小 ACK 有客户端静态读法支持；错误具体提示仍无实帧。已解封条目指定位置替换，第二字段非 `0xFFFF` 时不结算材料；未解封装备不替换。 |
| 相邻入口 | A21 `0x0197` 有两个不一致构包点、布局未闭合；A21 `0x01C8` 为三个 short 共 6 字节，而 70 `DispatcherResetRandomOption::read` `0x081dd1e2` 是两个 short 加 byte，版本差异明确（`ClientStatic`/`70Reference`）。 | `0x0197/0x01C8` 本轮记录并隔离，不挂到解封/替换结算路径；需后续实帧或进一步静态取证才能安全扣资产。 |

设计：规则服务返回资格、金币成本、带种子的候选词条；领域服务在角色拥有的装备实例及同一事务中验证/扣费/写入，保留 UID、`SealFlag`、强化、附魔、异次元等字段。协议 handler 只解析请求、调用命令、提交后投递权威物品/当前金币及静态读法支持的最小 ACK。没有 A21 实帧，所有 GUI 效果仍待用户实测。
