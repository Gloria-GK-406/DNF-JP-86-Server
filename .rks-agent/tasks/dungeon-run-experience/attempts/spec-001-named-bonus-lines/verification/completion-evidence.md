---
kind: rks-verification
decision: pass
---

# 终验：每一笔加成走到客户端为它画的那一行

**被评价的主张**：`spec-001-named-bonus-lines` 的七条成功条件全部成立，改动可以验收。

**新鲜度**：以下证据全部取自本次改动的最终状态（含评审 NB-6 的注释修正之后），不是评审期间那一版。
命令在 `.worktrees/dungeon-run-experience/Server` 下执行，`PVF_ARCHIVE_PATH=/d/project/DNF/DNFClient-A21-protocol/Script.pvf`。

| 条件 | 证据 |
|---|---|
| SC-1 九个可用偏移各有一个种类，且只有这九个 | `MonsterDeathCodecTests.The_declared_lines_are_pair_for_pair_the_measured_ones`：逐三元组比对生产表 `NamedLines` 与测试锚 `Measured`（后者逐条抄自 `Docs/ExperienceNoticeSlots.md` §4），数量、每个 kind 单命中、偏移、客户端叫法四项全等；并断言除 `Unlisted` 外每个枚举成员都有一行。独立评审另跑了逐字符 diff：doc / 生产表 / 测试锚三方九行同序 byte-identical |
| SC-2 付了某种类则该偏移带该金额，未付则为零 | `Every_named_line_is_written_at_the_offset_the_client_draws_it_from`：遍历 `Measured`（不是遍历生产表），逐种类付 77 并断言该偏移为 77、其余十三个加成偏移为 0 |
| SC-3 同种类多笔相加 | `Two_reasons_on_one_line_are_added_together`：两笔 `SeriaBlessing` 3 与 4，`+0x1A` 读出 7 |
| SC-4 无处可报的不写槽，金额仍进总数 | 前半：`A_bonus_belonging_to_no_line_writes_nothing`，一笔 `Unlisted` 之后十四个槽全零。后半：`CharacterExperienceGrantTests.ContributionsAreAddedAndCarriedBackNamed`（`CharacterExperienceGrantTests.cs:116`），一笔默认 `Unlisted` 的 contribution 被计入 `Gained` |
| SC-5 `+0x2F` 与 `+0x33` 恒零且无种类指向 | `The_slots_that_do_not_balance_are_never_written`：对每一个枚举成员各付 55，两处均为 0。映射侧由 SC-1 那条测试的 `DoesNotContain` 两行断言 |
| SC-6 除加成槽外线级字节不变 | `The_experience_frame_carries_the_four_fields_that_have_a_value_and_zero_elsewhere` 的断言未经修改（只换了入参形式），仍断言帧长 83、四个有值字段的偏移与取值、以及全部非 valued 偏移为零 |
| SC-7 全套通过、构建零警告零错误 | `dotnet build 86JP.Server.slnx`：0 警告 0 错误。`dotnet test 86JP.Server.slnx --no-build`：Networking 51、Protocol 306、Pvf 60、Host 4、Application 706、Infrastructure 139(+2 跳过)、Persistence 240 —— 合计 **1506 通过 / 2 跳过 / 0 失败** |

## 判据强度是量出来的，不是声称的

这一组测试的价值全在「一个被转置的配对能不能被抓住」，而这件事只能用变异来测量。第一轮独立评审正是用变异证伪了上一版：把生产表与写入端一起转置成同一个错误配对，九个测试全绿。修正后重跑变异：

* 生产表 + 写入端一起转置 → 3 红（第一轮的阻断项消除）
* 只改测试锚 → 2 红
* 只交换客户端叫法、偏移不动 → 1 红（把叫法放进生产代码换来的实际收益）
* 表侧指向 `+0x2F` → 1 红；写入端真往 `+0x2F` 写 → 2 红；双侧同删一行 → 2 红；写入端静默漏写一行 → 1 红

**如实标注一处边界**：把生产表、写入端、测试锚三份副本一起改成同一个错误配对，仍然全绿。测试防不住「有人重写判据」，这不是这层能覆盖的对象——测试文件里已经写明该表只允许由一次新的实测来改。不要把它当作第三层防护。

## 未由本次终验覆盖的

`spec.md` 的 EV-004（一次实机击杀截图，明细各项之和加基础值等于宣布的总数）不在本子契约的成功条件里，属根契约 SC-1 的实机验收，由用户在三个子成果全部完成后一次性执行。本次终验不代替它。
