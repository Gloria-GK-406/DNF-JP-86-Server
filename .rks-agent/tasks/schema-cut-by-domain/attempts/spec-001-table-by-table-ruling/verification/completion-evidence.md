---
kind: rks-verification
decision: pass
---

# Verification — spec-001 全表归属裁定与目标形状

## 被评价的主张

「改动前的每一张表与每一列，在目标形状中的去向都已有据可查地确定；后续实施只需照此执行，不再需要临场判断边界。」

交付物：`Docs/Refactor/TableOwnershipRuling.md`（1392 行）。
本 Spec 不改动生产代码、测试或库形状声明，因此主张的范围**只覆盖这份记录本身**，不覆盖任何运行时行为。

## 证据来源

全部检查在当前工件上重新运行，未复用任何一轮评审的结论。清点类检查由脚本从**代码与文档两端各自解析后比对**，不采信文档写下的任何数字。

## 逐条条件与证据

| 条件 | 证据 | 结果 |
|---|---|---|
| SC-1 53 张表全部有裁定，无遗漏、无重复、无矛盾归属 | 从 `CharacterDatabaseSchema.cs` 的 `TableDeclarations` 解析出表集合，与「Per-table ruling」一节解析出的行逐一比对 | 53 行；编号 1..53 连续；零重复；`in schema not doc` 与 `in doc not schema` 皆空 |
| SC-2 每处合并写明三条判据如何成立，每处「相近但未合并」写明哪条不成立 | 通读 M-1..M-7 与 X-1（合并与拆分）、S-1..S-15（未合并）；交叉引用可解析性由脚本检查 | M-1..M-5 各三条逐条；M-6 给三张角色表彼此的三条；M-7 用一张表给五处折入各自的三条；S-1..S-15 各写明所依托的依据，并在表前声明拒绝可依托的三种依据（观测判据、红线、明记的证据不足）；全部 M/S/C/D/X 引用零悬空 |
| SC-3 420 列全部有去向 | 附录逐行解析后与声明比对；不变表清单与声明比对；两集合求交与求并 | 附录 229 行、229 个互不相同的源列、零双落点；23 张源表**逐列与其声明完全相等**；不变表 30 张 191 列逐张相符；两集合不相交，并集 = 53；191 + 229 = 420；其中 45 退役、184 落点 |
| SC-4 邮件附件的归属有明确结论与理由 | 通读「Mail attachments」一节 | 三条判据逐条判断（键与生命周期均标为「未观测」，写入者标为「推断」），拒绝依托的是观测到的四列来源出处而非推断；并登记该表是上一代分解式形状在生产读取路径上的最后一个实例（D-1） |
| SC-5 金币上限的归属有明确结论，并由此确定实施先后 | 通读「The gold limit, and the ordering it settles」与「A second coupling」两节 | 归 `character_stats`；两处读点均具名；由此得出「角色本体先行、物品侧跟随」，并另给出同型的第二处耦合（`PvpSkillProfileService`）与同向结论 |
| SC-6 目标形状可清点，且无一张表以通用键值行承载多个概念 | 目标形状各表标题与附录去重落点比对；红线一节逐张检查 | 标题合计 165 = 161 有源 + 4 无源，逐表相符；红线表 43 行，与目标形状 43 张一一对应，21 张无候选、14 张判 Clear、8 张判 Unresolved（各自登记 Deferred），**无一张判违反**；原本唯一一张违反者由 X-1 拆掉 |
| SC-7 每一条依据事实都注明可复核位置 | 「Where the facts can be checked」一节 30 行；脚本验证文档引用的每一条仓库路径存在；抽查五条事实到代码 | 25 条含路径的引用全部存在（含通配一条可展开）；抽查：称号册 `category` 的 0..4 校验在 `CharacterEntryAssembly.cs:385` ✓；`TitleBookGeneral = 19`..`TitleBookEvent = 23` ✓；`CharacterSettingsEntryState(HotkeySlots, OptionBitmap, VisibilityBits)` ✓；`StackableItemCore` 的 `Value = count` ✓；subtype-1 的建角色插入恰好 21 列 ✓ |

## 一致性检查

十轮评审反复出现的失效是「同一个数字改一处漏一处」。该失效已做成机械检查，16 项，本次全部通过：

```
appendix has no duplicate source column                    ok
every reallocated table matches its declaration            ok
unchanged column counts match declarations                 ok
unchanged and reallocated partition the schema             ok
unchanged + reallocated columns = 420                      ok
target-shape headings agree with the appendix              ok  headings 165, sourced 161, declared new 4
ledger paragraph agrees with the appendix                  ok
the character_condition defence states its real width      ok
red-line sweep has one row per target table                ok  rows 43, claimed 43, target tables 43
red-line tally matches the rows                            ok  clear 14, unresolved 8
deviation count agrees everywhere                          ok  rows 5, claims ['Five', 'Five']
every cross-reference resolves                             ok  []
deferred entries are numbered without gaps and in order    ok  [1..16]
the equipped-creature count is stated one way              ok  {'five'}
writer census adds to 53                                   ok  14 + 39
every cited path exists                                    ok  []
FAILURES: none
```

## 约束

| 约束 | 证据 | 结果 |
|---|---|---|
| 不改动生产代码、测试或库形状声明 | `git status --short` | 工作区只有两处未跟踪内容：`.rks-agent/tasks/schema-cut-by-domain/` 与 `Docs/Refactor/TableOwnershipRuling.md`。无任何已跟踪文件被修改 |
| 全程不产生任务级提交 | `git rev-parse HEAD` | 仍为接受基线 `fc6467af30eb42bfca8319da8fc238082e00116f` |
| 不为既有数据设计回填、修复或迁移路径 | 通读交付物 | 记录中唯一涉及迁移的一处是说明退役两张上一代物品表会连带删掉那道「要求迁移」的守卫，并明写这是项目「擦库不迁移」下的有意损失；未设计任何迁移路径 |
| 红线：目标形状无通用键值行 | 红线一节 43 行 | 无一张判违反；8 张未决全部是原样带过的不变表，各自登记并写明「若成立则照 X-1 同样拆」 |
| 每条裁定依据来自当前代码并注明可复核位置 | 见 SC-7 | 通过 |

未运行的检查：无。全量测试套件另行运行，其证据属根任务而非本 Spec 的验收范围——本 Spec 未触碰任何代码，`git status` 是更直接的证据。

## 结论

`pass`。七条成功条件各有当前证据，四条约束均成立。
