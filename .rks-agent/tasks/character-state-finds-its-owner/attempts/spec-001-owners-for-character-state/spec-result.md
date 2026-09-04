---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 角色状态各自找到主人

## Delivered

- **身份**（槽位、名字、职业、成长类型）由角色实例回答，每次现读不缓存。连带结果：角色实例对会话
  运行态聚合的依赖**整个消失**——主构造器由 10 参降为 8 参，`ISelectedSessionDomainService` 与
  `ISelectedSessionRuntimeDirectory` 两个参数因 CS9113 被强制摘掉
- **坐标**由 `ICharacterPositionDomainService`（5 个成员）持有并回答；6 处读与 2 处整体写回全部改指；
  `Protocol/` 层 `.Position` 成员访问归零；**落库时机一字未改**（仍挂在会话生命周期上）
- **设置**（热键槽、选项位图、可见位）由 `ICharacterSettingsDomainService` 回答，从既有两张表读
- **暗帝连招页**由 `IDarkKnightComboDomainStore` 与实例 `IDarkKnightComboBook` 持有，按页号寻址，
  建在既有行网关之上、不新建表；恒为 null 的单数残留 `ActiveCharacterState.DarkKnightCombo` 与其类型
  `DarkKnightComboState` 全仓退役
- **任务**：入场之后那一处读点改问已有的任务簿实例，未为任务新增任何生产类型
- **疲劳**：`ICharacterFatigueDomainService` 接缝 + 不记账的占位实现 `Unaccounted...`；进本处真的调用；
  不建 store 不建表；帧上仍报 188/0
- **判据**：基线 2024 → **2069**（净 +45 全部为新增用例），**既有断言一字未改**

## Success Evidence

| Condition | Evidence |
|---|---|
| C-1 | `verification/completion-evidence.md`：实例成员与实现；身份读取点全部枚举，逐个归类为入场序列内或经实例 |
| C-2 | 同上：坐标领域服务成员；8 处调用点去向枚举；角色实例无坐标成员；落库接缝 diff 为空 |
| C-3 | 同上：设置服务成员与答案来源；对着真 SQLite 库的判据；**生产调用方实际数目 0，如实报出** |
| C-4 | 同上：store/session/实例成员；对真库判据 15 条；两个残留标识符已跟踪内容计数 0 |
| C-5 | 同上：该读点去向；本成果新增任务相关生产类型清单为空 |
| C-6 | 同上：接缝成员与占位实现；「不记账」在代码里六处直说；进本处调用与所报的数的来源 |
| C-7 | 同上：验证者自跑两次变异（`1`→`2`、`SpendAsync`→`RefundAsync`），各自指名报红判据，还原后 SHA-256 逐字节相同 |
| C-8 | 同上：全量 2069/2/0；测试 diff 零条 `Assert.` 被删、零条 `[Fact]` 被删；协议编解码器不在改动清单 |
| C-9 | 同上：判据 12/12；判定谓词与豁免清单所在文件 `git diff --stat` 输出为空，豁免仍只有 `TimeProvider` |
| C-10 | 同上：领域层含 `store` 类型逐个分类；新增实例四个类型名字均不含 store |
| C-11 | 同上：验证者逐个复算 `AGENTS.md` 引用的每个数字（9 成员、6 注入点、6 处 `ReplaceExact`、Protocol 下 0 处、48/19），全部精确吻合 |

## Repository Result

- Changed paths: 已跟踪 35 个文件（+776/−268）加 18 个新文件（不含 `.rks-agent/` 流程产物）
- Baseline or accepted result: 基线 `427331bcb07f60121c97920287ac341c9cb14dee`
- Parent rollback required: no

## Successor Guidance

**必须保留的事实与已移交的责任：**

- **会话运行态聚合仍然存在，几乎原样存在。** `SelectCharacterResult` 仍是 9 成员，其文件根本不在改动
  集合里。入场投影的公开态（15 处读）、进度（11 处）、特性（8 处）三块内容未查清，所有者要求本轮做完
  再定。**任何后续记载不得把本轮写成「拆掉了大聚合」**
- **`ActiveCharacterState.Position` 已成死字段**：只有一处写（入场构造），六处 `ReplaceExact` 里五处
  原样搬回，生产侧读者归零。已写进 `AGENTS.md` 并警告新代码不许从它取坐标
- **`CharacterEntry.Quests` 自入场起就是陈的。** 这推翻了本 Spec 原先的一句前提（见 `spec.md` 末尾的
  订正留痕）。所以任务那处读点重指**实质是修了一个潜在缺陷**；今天仍逐值不变，仅因
  `DungeonLayoutRuleService.SelectMaze` 刻意不读 facts。**等 maze 筛选规则逆向出来，读陈值就会给出
  错的迷宫**
  - 验证阶段另查到一处订正未列的写入点 `SelectCharacterPostEntryServices.cs:57-63`（寻物触发值同步），
    它在 publish 之前、属入场序列内，**不推翻「自入场起是陈的」**这个结论，但订正的枚举不完整，记于此
  - 订正里「它只改 `World.Subject` 与 `World.Content`」说的是**字段**不是路径；该服务实际位于
    `Server/src/Dfo.Server.Application/Quests/`。验证者曾读作路径笔误，特此澄清
- **疲劳占位消耗写死 `1`，与档案已知冲突**：教程本（`TutorialDungeonId = 10000`）被既有判据钉着
  `ConsumesFatigue = false`，按写下的规则该副本进本消耗是 **0**。**spec-002 记真账时调用点必须先问
  档案该副本扣不扣**
- **「每日 6 点重置」这个时刻仓内无出处。** 代码只写 `reset daily`；`Docs/DungeonEntryNotes.md` §八有
  基础 156、黑钻 +32、抗疲劳药剂，独独没有时刻。spec-002 实现每日边界前需要所有者给一个可追溯落点
- **设置与连招今天零生产调用方**，这是所有者要求的归位而非疏漏，已在 `AGENTS.md` 写明「刻意暂无
  消费方，待改键协议」。**不要当死代码删掉**
- **协议 handler 层普遍缺判据**：本轮实测 48 个 handler、19 个类名在 `Server/tests` 全域一次不出现
  （口径边界：线级集成用例经 DI 解析 handler，故不外推成「19 个无判据」）。本 Spec 三次撞上同型缺口，
  上一轮 multi-spec 一次。已写进 `AGENTS.md`
- **既知偶发的判别依据是「异常类型 + 栈根 + 复跑即绿」，不是用例名。**
  `ObjectDisposedException: 'SQLitePCL.sqlite3'`，栈根
  `CharacterDatabaseSchema.KeepWritersAndReadersApart → SqliteCommand.ExecuteScalar`，
  已在 `OutsideTransactionTests` 与 `DarkKnightComboBookTests` 两条不同用例上出现过

## Artifacts

- `./TaskContract.md`（revision 1）
- `./spec.md`（含末尾的前提订正留痕）
- `./plan.md`
- `./reviews/change-review.md`（verdict: approved）
- `./verification/completion-evidence.md`（decision: pass）
- `./tasks/TASK-01..09-*.md`（九个 Task Unit 的执行记录与 12 轮独立快审）
