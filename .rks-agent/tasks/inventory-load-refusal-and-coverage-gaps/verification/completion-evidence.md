# Completion Evidence

- TaskContract: `../TaskContract.md`（rev 1）
- 变更评审：`../reviews/change-review.md`，裁定 `approved`
- 新鲜验证判据基线：HEAD `25dccec6` ＝ 2237 通过 / 2 跳过 / 0 失败，退出码 0

## 新鲜验证（在**将要提交的那棵树**上跑，最后一次文档改动之后）

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf \
  dotnet test Server/86JP.Server.slnx --nologo --logger "console;verbosity=detailed"
```

**退出码 0；2252 总 / 2250 通过 / 2 跳过 / 0 失败。** 全量输出中 `[FAIL]` 与 `失败 ` 行数为 **0**。

逐项目（括号内为 HEAD 基线）：

| 项目 | 通过 | 跳过 |
|---|---:|---:|
| `Dfo.Pvf.Tests` | 60（60） | 0 |
| `Dfo.Server.Networking.Tests` | 52（52） | 0 |
| `Dfo.Server.Protocol.Tests` | 528（524） | 0 |
| `Dfo.Server.Host.Tests` | 6（6） | 0 |
| `Dfo.Server.Persistence.Tests` | 329（320） | 0 |
| `Dfo.Server.Infrastructure.Tests` | 236（236） | 2 |
| `Dfo.Server.Application.Tests` | 1039（1039） | 0 |

加法逐项可重算：**60 ＋ 52 ＋ 528 ＋ 6 ＋ 329 ＋ 236 ＋ 1039 ＝ 2250**，项数 7。
新增判据 **4 ＋ 4 ＋ 4 ＋ 1 ＝ 13**，项数 4；**2237 ＋ 13 ＝ 2250**。
`Dfo.Server.Application.Tests` 一条未增未减，是「既有判据未被改写」的旁证。

**两条跳过与基线是同两条**：`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`、
`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`。二者因档案**确实加载了**而跳过，是 PVF 就位的标志，不是缺陷。

**全程未遇到已知的 SQLite 偶发失败**：七次全量运行（基线 2 次 ＋ 各单元 4 次 ＋ 最终 1 次）失败数均为 0，`ObjectDisposedException` 零命中，因此无需动用「异常类型 ＋ 栈根 ＋ 复跑即绿」的判别。

## Success Conditions

| ID | 判定 | 证据 |
|---|---|---|
| SC-1 | 满足 | 角色／货柜／堆叠物三条拒绝判据；删去两处 `RequireOneNameEach` 调用后三条同时变红（执行侧 CK-001，评审侧独立复现：3 红 / 326 通过，且无既有判据变红）。 |
| SC-2 | 满足 | `AnInventoryThatNamesEachThingOnceIsLoadedUnchanged` 断言两条装载路径的槽号、模板号、取值；`Dfo.Server.Application.Tests` 1039 条一条未动；`git diff -- Server/tests` 全部删除行 ＝ 6 行文档注释 ＋ 1 行方法签名。 |
| SC-3 | 满足 | `AnItemThatIsNotAnAvatarMayNotCarryAvatarFacts`；删去 `GameInventoryDelivery.cs:50-51` 后变红（CK-002 变异 A，评审独立复现）。 |
| SC-4 | 满足 | `AnAvatarWhoseJewelSocketIsNotThirtyBytesIsRefused`（29 与 31 两例）；守卫失效后两例同时变红（CK-002 变异 B′）。 |
| SC-5 | 满足 | 三条判据分别对应长度、`Color1`、`Color2`；逐个删除子句时**只有对应的那一条**变红（CK-003：三次均为 1 红 / 8 通过）。断言精确异常类型，因而不会被随后的 `checked((ushort)…)` 抛的 `OverflowException` 冒名顶替。 |
| SC-6 | 满足 | `AnAvatarWhoseRowIsRebuiltIsHandedItsSixFactsAgain`：断言新行 `item_uid` ≠ 原行，并逐个断言六个值。令 `CarriedAvatarsAsync` 恒答空表 → **2 红**（新判据 ＋ `TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding`），而改名后的 `Relocate_LeavesASparedAvatarsRowStandingWithItsFactsUntouched` **仍绿**——同一变异在 HEAD 上只红 1 条，缺口确已闭合。 |
| SC-7 | 满足 | `SelectCharacterAcceptedTranscriptTests.cs` SHA-256 `1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69`，与 `git show HEAD:` 相同；`git diff` 为空。 |
| SC-8 | 满足 | 见上：退出码 0，跳过仍为 2，通过数 ＝ 2237 ＋ 13。 |
| SC-9 | 满足 | AGENTS.md 改动逐句经两轮独立复核（变更评审第 5 节 ＋ 追加的 N-3 六条逐条判定，全部为真）；删去的两条缺口条目均已实际闭合，保留的缺口条目一条未被误记为已闭合。 |
| SC-10 | 满足 | `CommandHandlerArchitectureTests.cs` SHA-256 `ab9107bc2371d4a3d4e5f898b3e1bf29995a8ffd315c35ffad06abbdb29d9714`，与 HEAD 相同；该用例在全量中通过。 |

## 一处对派工前提的更正（已由所有者独立复核确认）

派工描述的两个锚点与代码不符，按实测走：

- `RequireUniqueSlots` **确实**在两条装载路径上被调用（`InventoryEntryAssembly.cs:75`、`:103`），只是不在 `ReadCharacterItemsAsync` 的循环里。因此 AGENTS.md 原句「入场读只校验 (容器, 槽) 不重复，不校验 `core_uid` 不重复」按「入场读」这一粒度**是准确的**，未作更正；真正缺的只有 `core_uid` 那一半。
- 保存侧的拒绝在 `:224-229` 的 `claimed.Count != settled.Count`，不在 `:145`——后者的 `.Distinct()` 只喂 `DeleteOwnExceptAsync`。这一点是承重的：真正的拒绝比的是**世界行身份**，堆叠物也在内。据此把装载侧也做成数世界行，并用 `APileNamedInTwoPositionsIsRefusedAlthoughItCarriesNoIdentity` 单独钉住；实测把拒绝改成跳过堆叠物后，**只有该条变红**（TASK-01 第 4 轮）。

## 未解决的风险

1. **称号书是这条不变量的盲区。** `list_type` 19..23 的位置行不经入场读，也不经保存侧的 `DeleteOwnExceptAsync`／`ClearOwnPositionsAsync`，所以一条称号书位置行与一条别处的位置行指名同一个世界行时，两侧都看不见。**本次未修**（跨查询关联属于设计改动，在边界外），已如实记入 AGENTS.md。
2. **`SqliteWorldItemTable` 仍是既有缺口**，AGENTS.md 原条目原样保留：它写得出「kind 8 但六列为空」，今天生产零调用方；谁第一次给它接上生产调用方，谁就得同时补守卫与判据。
3. **「一件东西只在一个位置」的理由现在写在两处**（AGENTS.md 的 `item_slots` 小节与 `RequireOneNameEach` 的 `<remarks>`），两处今天都为真，但按 AGENTS.md「要引就引，不要抄」这是一个漂移点。变更评审记为不阻塞（N-1），未改。
4. **`Docs/Refactor/TableOwnershipRuling.md:776` 仍写着改名前的判据名**，未动。该文件是带日期的裁定稿、开头已自陈为历史，且同一句里 `SqliteItemStoreTests` 与 `SqliteItemStoreSession` 在本次改动**之前**就已全仓零命中；只更正其中一个名字反而会暗示另两个是现行的。判断与依据记在 `../tasks/TASK-05-agents-md-records.md`。
5. **spec 的 AC-013 措辞与 AC-011 字面冲突**（前者说改名后该文件用例总数与 HEAD 相同，后者要求在同一文件新增一条，19→20）。是 spec 行文瑕疵，不是实现缺陷；改名本身确实未增删用例。变更评审记为不阻塞（N-2）。
