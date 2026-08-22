---
kind: rks-task-contract
revision: 2
state: ready
parent: ../../TaskContract.md
contributes_to: [SC-1, SC-6, SC-7, SC-8, SC-9, SC-10]
depends_on: []
---

# TaskContract: The snapshot in domain shape

## Context

### Source Request

父控制器在 `child_modeling` 阶段派生的第三个、也是最后一个子成果。根授权：
「既然你有更好的想法，那就按你的跑吧。重构结束后再找我」。

### Current Situation

角色快照今天有**两族以协议段命名的记录**，都在协议工程之外：

1. **存储侧**：`CharacterSubtype0StorageRecord`（45 个字段）与 `CharacterSubtype1StorageRecord`（33 个）。
   `CharacterEntryAssembly.Subtype0` 从 `CharacterConditionRow`、`CharacterSettingsRow`、
   `CharacterPvpRow`、`CharacterExpertJobRow` 四张表的行**按段序拍平**成一个记录；
   `Subtype1` 从 `CharacterStatsRow` 与 `CharacterEntryRow` 拍平成另一个。
2. **入场侧**：`CharacterSubtype0EntryState` 与 `CharacterSubtype1EntryState`，
   由 `CharacterProfileEntryProjection.Build` 从第一族转来——一段 45 字段的**按位复制**，
   每个字段做一次 `checked((byte)…)` 之类的位宽收窄。这段复制之所以存在，
   只是因为两端都是包的形状。

后果是三条：领域里有以协议段命名的类型；把值收窄到线上宽度这件事（也就是「值超出该字段就拒绝」
这条真行为）发生在不知道线上宽度的一侧；而 `SelectCharacterPreparationService`
这样的领域服务要读 `entry.Public.Subtype1.Progress1` 才能拿到一个本该有名字的事实。

上一块（spec-002）在建模时核实并推翻了此前的一处描述：`ActiveCharacterState` 已经是领域形状，
不是包形状。根契约与设计文档 R-5 均已按此订正，本成果针对的是上面两族。

## Goal

角色快照成为一组领域记录：存储侧按概念承载（运营态、设置、PvP、专家职业、派生数值），
入场侧的段布局——字段的选取、排序与位宽收窄——移到协议工程。
领域里不再有以协议段命名的记录类型。

下发帧一字节不变。

## Scope

### In

- 存储侧：`CharacterProfileStorageRecord` 改为承载按概念切的领域记录，不再承载两个拍平的段记录；
  `CharacterEntryAssembly` 停止拍平。
- 入场侧：段记录**删除**，其装配（含位宽收窄）落到写字节的地方。
- 领域服务改为按名字读事实，不再经由段记录取值。
- 随上述改动而来的测试改写。整记录表征用例的**期望值一个字节不变**，只改产生它们的那条路。

### Out

- 下发帧的任何字节、任何字段次序。
- 物品侧与账号侧的快照。
- 持久层形状。
- 客户端错误码。
- 物品侧与账号侧那两条入场路径上的段形状记录：本成果只治角色侧。

## Constraints

- **C-1** 下发帧一字节不变。位宽收窄的行为逐条保留：今天会因超宽而抛的，改后仍在同一处抛。
- **C-2** 三份整记录表征用例的**期望值一个字节都不得修改**。断言的表述方式可随形状改变，
  期望的数值不可。
- **C-3** 协议编解码用例的期望字节不得修改。
- **C-4** 不得改动持久层形状。
- **C-5** 领域侧不得保留以协议段命名的类型。
- **C-6** accept 时构建零警告、全量测试通过。
- **C-7** 删除任何测试文件前逐条核对其断言证的是不是即将消失的东西。
- **C-8** 沿用根契约 C-7 的既有项目约束。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-C1 | 领域侧没有以协议段命名的记录类型 | 全仓搜索 `Subtype0` / `Subtype1` 形状的类型，命中只在 `Dfo.Server.Protocol` 内 |
| SC-C2 | 存储侧快照按概念承载 | `CharacterProfileStorageRecord` 的成员能逐个指出它来自哪个概念；`CharacterEntryAssembly` 不再有按段序拍平的方法 |
| SC-C3 | 位宽收窄发生在知道宽度的一侧 | 收窄逐条位于 `Dfo.Server.Protocol`；一条用例证明超宽仍被拒绝 |
| SC-C4 | 领域服务按名字读事实 | `SelectCharacterPreparationService` 等不再经由段记录取值 |
| SC-C5 | 下发帧一字节不变 | 三份整记录表征用例期望值 `git diff` 为空；协议编解码用例期望字节 `git diff` 为空；全量通过 |
| SC-C6 | 结果是绿的 | 构建零错误零警告；全量通过 |
| SC-C7 | 可证伪 | 对本成果新建立的每一处保证各演示一次先红后绿 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 根契约 `task` 行；父控制器在 `child_modeling` 判定本成果落在其授权范围内 | 在共享任务工作树内实施本成果；经评审与新鲜验证后由父控制器做一次已接受结果提交 |

## Assumptions

- **修订说明（revision 2）**：本契约 revision 1 的 Scope/In 写的是把入场侧段记录「移入
  `Dfo.Server.Protocol`」，Out 里还留了一句「移进协议工程即止」。实施时判定移动是错的做法：
  那两个记录的全部内容就是「把概念摆成段序并各自收窄」，而摆序与收窄现在发生在 `writer.Write(...)`
  那一行——留着它们等于在编码器前面多一层只为复述包形状而存在的类型。因此**删除**而非移动。
  规格的 Outcome、RQ-3 与 AC-3 三处本就写的是删除（「不是搬走，是没有了」），
  独立评审亦判此不算超范围；契约据此对齐。

- 入场侧段记录的消费者只有协议编解码、选人处理器、两个领域服务与四个测试文件。
  已核实：`CharacterSubtype0EntryState` / `CharacterSubtype1EntryState` 的引用出现在
  `CharacterEntryStateServices.cs`、`CharacterModels.cs`、`StateSynchronizationPayloadCodec.Entry.cs`
  与两个测试文件；`.Subtype0` / `.Subtype1` 的取值另出现在 `SelectCharacterHandler.cs`
  与 `SelectCharacterPreparationService.cs`。此假设由 SC-C1 的全仓搜索钉住。
