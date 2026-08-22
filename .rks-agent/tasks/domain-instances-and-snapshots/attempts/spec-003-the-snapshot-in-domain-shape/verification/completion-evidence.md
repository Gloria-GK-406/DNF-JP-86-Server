---
kind: rks-completion-evidence
verdict: pass
spec: ../spec.md
contract: ../TaskContract.md
---

# Completion evidence: The snapshot in domain shape

## 基线事实

| 事实 | 结果 |
|---|---|
| 构建 | 零错误零警告 |
| 全量 | **1180 通过 / 2 跳过 / 0 失败** |
| 上一基线 | `078d762` = 1172 / 2 / 0 |
| 净增 | +8：装载时宽度拒绝的 `[Theory]` 七行，投影原样传递一条 |
| 环境 | `PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf` |

## 成功条件

| ID | 结果 | 证据 |
|---|---|---|
| SC-C1 | 成立 | `Dfo.Server.Application`、`Dfo.Server.Abstractions`、`Dfo.Server.Data` 三处搜索 `Subtype0` / `Subtype1` 均**零命中**；仅存的提及在 `Dfo.Server.Protocol` 内，且是协议段自己的名字 |
| SC-C2 | 成立 | `CharacterProfileStorageRecord` 的每个成员可指出其概念（运营态、设置、PvP、专家职业、派生数值、公会站位，加角色本体四样）；装配器已无按段拍平的方法 |
| SC-C3 | 成立 | `CharacterProfileEntryProjection` **零收窄**；`CharacterEntryStateServices` 全文件 93→27，剩余 27 全属技能/任务/进度/特性四个其他投影；唯一保留在 profile 路径上的一处（名录 manage level）目标类型确为 `byte`，由第三轮评审独立核实。装载时的宽度拒绝由 `CharacterEntryState_RefusesAValueTooWideForItsFieldWhenItIsLoaded` 七行钉住，并断言抛的是两条拒绝中的哪一条 |
| SC-C4 | 成立 | `SelectCharacterPreparationService` 与 `SelectCharacterHandler` 均按名字读（`DerivedStats.Progress1`、`Extras.ManageLevel`、`Pvp.RankPoint` 等） |
| SC-C5 | 成立 | 三份整记录表征用例中两份 `git diff` 为空；`EntryLoadCharacterizationTests` 的 golden 因形状改变而重写，**值多重集与基线逐条一致**（215/215，两轮评审各自独立重做过这个比对）；协议编解码用例期望字节零改动；帧字节基线用例通过 |
| SC-C6 | 成立 | 见上表 |
| SC-C7 | 成立 | 六次自查演示加三轮评审的十余次独立真改真跑，覆盖字段错位、收窄丢失、收窄宽度错、真假反向、概念读错来源、拒绝搬错半边 |

## 三轮评审

- **第一轮** `changes_required`。一条 A 级：`RequireProfileInRange` 少了四条宽度拒绝——那四个字段随重新分组迁出扁平记录，检查失去挂靠点；超宽的行不再在装载时被一句话拒绝，而是在组帧途中抛 `OverflowException`。已补回并新增七行用例，演示拿掉四条恰好红那四行。
- **第二轮** `changes_required`。一条 A 级：我为「四处收窄未下沉」写的理由，**四处里三处不属实**；评审去代码核实并真改真跑证明可去。已按事实去掉三处（而不是改说辞），第四处保留并核实其目标确为 `byte`。
- **第三轮** `passed`。独立复核 A-2 闭合；专门构造负值探针验证 `uint`→`long` 与 `IReadOnlyList<uint>`→`IReadOnlyList<int>` 的加宽**没有让任何负值走到线上**——拦截子句与异常消息与基线逐字相同；六次真改真跑，其中两次揭示了新的覆盖缺口（记为 B 级，非本轮引入）。

## 未解决且已记录

- **B-16**：编解码器侧 NameTag 次序无守卫。第三轮评审给出并验证了一个不碰共享夹具、不碰任何期望字节的写法，本轮未采纳落地，留给后续。
- **B-14 / B-15**：`VisibilityBits` 的来源无人钉住且在入场态存了三份（一份零读者）；`user_state_bits` 的装载拒绝现在是唯一防线却无用例。
- **B-13**：悬尾分号。
- 其余 B 级记录在 `reviews/change-review.md`，三轮评审一致判为不阻塞。
