---
kind: rks-review
verdict: approved
scope: change
baseline: 1d887c59
reviewed_at: 2026-09-08
---

# spec-004 独立整改复审

评审对象是 `1d887c59` 至当前工作区的完整未提交差异，以及本 attempt 的 TaskContract、spec、plan、读者清点、行为清单和旧评审。复审集中裁定 BF-1..BF-7、NB-1..NB-4 及整改的直接回归；没有扩大为全局会话并发改造。检查源码、精确变异脚本与原始运行日志，没有复用执行者的结论作裁定。

## Challenge Dispositions

旧 findings 均为 confirmed；下表记录其整改处置，而不是否认原 finding。

| Finding | 复审处置与证据 |
|---|---|
| BF-1 | 已解决。`Opening_dungeon_selection_after_dressing_reads_the_current_equipment` 调用真实 `EnterSelectDungeonHandler`，依次断言旧衣服、移动后当前衣服、脱下后空装备。运行态封装真实目录，消费的确是更新后的 resident result；P2 删除更新时在实际界面帧的 `Assert.Equal` 失败。 |
| BF-2 | 已解决。行为清单明确新增“TryGet 后代次失效 → ReplaceExact 失败 → 抑制外观、保留成功确认”的发送限制；测试断言单帧、ErrorCode=null、MoveValue=1 和 InvalidOperationException 日志。当前代码与该记录一致。 |
| BF-3 | 已解决。独立 `rg -n 'ReplaceExact\(' Server/src` 得到七个生产调用，另有接口与实现。AGENTS 清单已含 Move；两份历史协议记录加状态更正且保留取证边界。Tutorial 源码第 241/271 行确有更新与 AvailableQuestListMessage。DungeonSelection 原逐字节不变的注释也已更正。 |
| BF-4 | 已解决。P1 精确编辑是克隆 CharacterEntry 成员，原始日志为 `Only_the_inventory_member_of_the_session_copy_is_replaced` 的 Assert.Same 身份不等。已承认复用同一成员的完整构造与 with 等价，未把它伪称为错误。P1–P4 的脚本、JSON 与失败日志吻合。 |
| BF-5 | 已解决。`Dressing_reads_the_inventory_once_for_both_frames_and_copy` 检查一次 LoadAsync，并分别断言即时 CurrentUserStateMessage 与随后真实副本选择帧中的当前装备。源码确实只加载一次，刷新与即时帧复用 reloaded。 |
| BF-6 | 已解决。plan 与行为清单改为 TryGet 成功之后、LoadAsync 等待期间移除/发布不同代次的窗口。目录确实只在 ReplaceExact 时验证代次；已失效的 TryGet 路径静默返回。同代次 ReplaceExact 不被误称为异常来源。 |
| BF-7 | 接受风险披露作为本边界的整改，理由见下。并发覆盖本身仍存在，未被判定为已修复。 |
| NB-1 | 已解决。plan、清单与测试注释包括契约兽槽 24；槽 13 支持扩大刷新门的论证仍成立。 |
| NB-2 | 已解决。失败刷新测试新增成功码、MoveValue 与日志异常断言，拒绝不能再冒充该成功结果。 |
| NB-3 | 已解决。清点补上 SelectCharacterPostEntryServices 的第十个入场使用者。 |
| NB-4 | 已解决。清点明确基准 1d887c59、表格为修改前位置及 Move 原无写回的历史状态。 |

## BF-7 独立边界裁定

直接读取 `SelectedSessionRuntimeDirectory.ReplaceExact` 可确认：锁只保护代次检查和整个 Result 赋值，并不合并字段。Move 在库存加载前捕获 state，加载后写回；其他写者夹入时，其非库存成员可能被恢复为旧对象，其他旧写者也可能覆盖本次库存。`AccountExecutionBehavior.ExecuteAsync` 的租约限于命令流水线；Move 的刷新位于 SendAsync 返回之后。`NetworkMessageDispatcher` 没有提供会话互斥，NetworkPlugins 等待一次 dispatch 本身不足以证明不同 TCP 回调串行。因此不能认定并发窗口已经被约束。

旧 BF-7 明确允许“bound it or list it”，其阻断理由是记录平面否认这条额外行为。此次已在源码、spec、plan、reader-enumeration、behavior-change-list、AGENTS 与历史调查补注中撤销该否认，说明只保留读取时的八个成员，不承诺原子聚合合并。TaskContract 又明确不改旧六个刷新点。基于这两条原边界，本复审接受列明风险的处置；无需把本次审查升级为未获当前合同要求的全局原子更新工程。此批准不代表满足任意并发交错下的快照新鲜度，也不代表 C-5 的无交错成员身份检查证明了并发隔离。

## 验证证据

- 直接检查本目录 `full-suite-review.txt`，七个成功运行段分别为 Protocol 631、Networking 52、Pvf 60、Persistence 333、Infrastructure 259（跳过 2）、Application 1076、Host 6：合计 **2417 / 2 / 0**，相对 **2409 / 2 / 0** 为 +8，全在 Protocol（623 → 631）。初次四个新增用例，加本轮两个 Fact 与一个双行 Theory，数量与测试差异相符。
- 原始全量日志中 `[FAIL]`、`error CS`、`MSB[0-9]` 均无匹配。CommandHandlerArchitectureTests、NotificationHandlerArchitectureTests 和 SelectCharacterAcceptedTranscriptTests 均有通过记录；其源码与字节锚断言未被修改。
- `focused-final.txt` 为 17 通过；P1/P2/P3/P4 分别 1/6/2/1 条预期失败。P1 是对象身份断言，P2 包括界面装备值不等，P3 为 RecordingRuntime.ReplaceExact 的 InvalidOperationException 与 InventoryThatRaises.LoadAsync 的 InvalidDataException 逃逸，P4 为刷新次数 1 对 0。均有原始失败信息与堆栈，不只退出码。
- `focused-initial.txt` 的失败是测试 SelectionVisits 返回类型的命名空间遮蔽：CS0118，继发 CS0738；当前使用完整类型名，后续编译和全量运行通过。不是运行期 SQLite 异常。
- 独立计算当前 `MoveItemSpaceSlice.cs` SHA-256：`93abc8df226df8bd06bf986891769bcccc37cefbfae033e6ef1653ea4c4da4a9`，与每次变异还原值一致。review 未运行并行 build/test 或修改生产文件。

没有剩余阻断项。真实 PvP 画面、TCP 回调完整串行保证及聚合覆盖消除均未被本次证据证明；保留当前材料中的限制。批准后仍由控制器完成最终接受与新鲜验证记录。