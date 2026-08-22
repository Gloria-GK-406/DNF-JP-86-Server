---
kind: rks-plan
state: ready
spec: ./spec.md
---

# Plan: The snapshot in domain shape

五个有序任务。两次搬运之间必须回到全绿，否则错位将无从定位。

## Ordered tasks

### T-001 — 存储侧按概念切开

在 `ICharacterEntryStateServices.cs` 立六个概念记录：运营态、设置、PvP、专家职业、派生数值、
公会站位；角色本体自己的几样（技能树序号、名牌物品与到期、管理等级）直接进
`CharacterProfileStorageRecord`。删掉两个段记录。

`CharacterEntryAssembly` 的 `Subtype0` / `Subtype1` 两个拍平方法删除，改为每张表映射成它的概念。

`CharacterProfileEntryProjection` 的读取端跟着改（`storage.Subtype0.Stamina` → `storage.Condition.Stamina`），
**产出端此步不动**——它仍产出段记录。

`EntryLoadCharacterizationTests` 的断言按新形状重写，**期望的数值一个字节不改**。

判据：三份整记录表征用例全绿；全量全绿。

依赖：无。

### T-002 — 入场侧按概念切开，收窄下沉到编解码器

`CharacterPublicEntryState` 改为持有同一组概念（用领域类型：0 与非 0 变成真假）。
删掉 `CharacterSubtype0EntryState` 与 `CharacterSubtype1EntryState`。

`CharacterProfileEntryProjection` 那段 45 字段的按位复制消失：真假转换留在这里（那是含义），
位宽收窄跟着字段进入编解码器的 `writer.Write(...)`。

编解码器里 `t.X` / `s1.X` 改为按概念取值，并在写的那一行收窄。

判据：协议编解码用例的期望字节一字不改而全绿；三份整记录表征用例仍全绿。

依赖：T-001。

### T-003 — 领域服务按名字读

`SelectCharacterPreparationService` 与 `SelectCharacterHandler` 改为从概念上读
（`Progress1`、`Progress2`、`ManageLevel`、`EquippedCreatureLevel`、`PvpRankPoint`）。

依赖：T-002。

### T-004 — 收旧路与文档

删除因搬运而无调用方的东西，**删前逐条核对**。
在 `Docs/Refactor/DomainInstanceDesign.md` 记下本成果确证或修正的条目，
并把 R-5 的「段布局归协议」由裁定改为已落地。

依赖：T-001..T-003。

### T-005 — 可证伪演示与全量

至少四次真改真跑：把某个字段错位一格；去掉一处位宽收窄；把一处真假转换反向；
让装配从错误的概念取值。每次确认变红且只红被点名的那些，复原后逐字节一致。

依赖：T-001..T-004。

## Boundaries

| ID | Boundary |
|---|---|
| BD-1 | 下发帧任何字节与字段次序不变 |
| BD-2 | 三份整记录表征用例与协议编解码用例的期望值不改 |
| BD-3 | 不改持久层形状 |
| BD-4 | 不动物品侧与账号侧快照 |
| BD-5 | 不改客户端错误码 |
| BD-6 | 不产生任务级提交 |
| BD-7 | C# 文件 CRLF、无 BOM UTF-8 |

## Checks

| ID | Check | Where |
|---|---|---|
| CK-1 | T-001 后三份整记录表征用例期望值未改而全绿 | T-001 |
| CK-2 | 装配器中已无按段拍平的方法 | T-001 |
| CK-3 | T-002 后协议编解码用例期望字节未改而全绿 | T-002 |
| CK-4 | 全仓已无以协议段命名的记录类型（搜索证明） | T-002 |
| CK-5 | 收窄语句全部在 `Dfo.Server.Protocol`（搜索证明） | T-002 |
| CK-6 | 两处领域调用点按名字读 | T-003 |
| CK-7 | 删除的每处旧路径都已证明无其他调用方 | T-004 |
| CK-8 | 至少四次可证伪演示 | T-005 |
| CK-9 | 构建零警告零错误；全量通过 | T-005 |

## Requirement coverage

RQ-1 → T-001；RQ-2 → T-002；RQ-3 → T-002；RQ-4 → T-003；RQ-5 → 每一步的判据。
