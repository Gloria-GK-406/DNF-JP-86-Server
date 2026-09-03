---
kind: rks-verification
decision: pass
---

# 完成验证：两笔钱各自叫对名字

## 被评估的主张

spec-001 的完整变更集满足 TaskContract revision 1 的 CSC-1..CSC-6：两笔钱各自叫对名字、与之有关的仓库记述与代码一致、主背包前三个虚拟槽位可从代码读出、且服务器行为逐值不变。

变更集：24 个已跟踪文件 +274/−287，加 3 个未跟踪新文件。基线 `1946c012`，本次验证时未提交。

## 条件到证据的映射

| 条件 | 证据 | 结果 |
|---|---|---|
| CSC-1 `purse` 不再指代任何一笔钱，判据的类名/字段名/方法名/用例名/临时文件名一并归位 | 全仓大小写不敏感检索 `purse`（`Server/`、`Docs/`、`protocol/`，`.cs` 与 `.md`）：**0 命中**。检索有效性另经 `.rks-agent/` 内的命中反证。原 79 处（源码 17 / 判据 62）全部归位，含 `FakePurse`→`FakeCargoGold`、`class Purse`→`class CargoGold`、`StartingPurse`→`StartingCargoGold`、四个用例名与临时库前缀 `dfo-purse-`→`dfo-cargo-gold-` | pass |
| CSC-2 `Gold` 只出现在指代金币之处 | 逐条判定 `Server/` 全部 `Gold` 命中：或为金币（邮件附件、掉落槽位、牌面金额、卡牌价格、携带上限），或为档案自有的黄金牌用语（`GoldCard*`）。六个魔方碎片字段已改为 `BlackCube`…`GoldCube`，唯一消费点同步，DB 列 `cube_*` 未动。`ClearRewardBlankCard.Gold` 已改为 `.GoldCard`。`Money` 仅余 3 处对参照服务端类型名 `AccountCargo.Money` 的引用 | pass |
| CSC-3 金库那笔钱的名字与记述都表明它是金库的，不再声称自己是玩家显示的钱包或全账号共用 | `IAccountMoneyService`/`AccountMoneyService` → `IAccountCargoGoldService`/`AccountCargoGoldService`（含实现、DI 注册与两个消费方）。以 `git show HEAD:` 取旧接口逐句对照：`CInventory::get_money` 与「客户端显示的钱包」整段删除，「Every character on an account draws on one purse」改为按账号取值的说明。新注释经独立评审逐句查证，未发现新的不实陈述 | pass |
| CSC-4 与两笔钱有关的记述与代码一致，不把未实现的记作已有 | 四处修正并经独立查证：`Docs/新功能接入规范.md:185` 改为分述（落库路径仍然没有；变更通知帧有但无存储支撑）；`Docs/新版背包架构业务接入规范.md` 加抬头声明其主语是参照服务端——六个点名类型在本仓 `Server/src` 全不存在、在 `ServerS4A21` 全存在；`CharacterDatabaseSchema.cs:35` 记录 `character_gold_limits` 已折进 `character_stats`（schema 中确已无该表，`character_stats` 含两列）；`Docs/Refactor/CharacterEntryStateCoverage.md:33` 归属改为随身金币的携带上限 | pass |
| CSC-5 主背包前三个虚拟槽位各自承载什么可从代码读出 | `InventoryRuntimeValueState`（`Server/src/Dfo.Server.Data/Inventory/InventoryModels.cs`）载明槽 0 是角色随身金币（item id 0）、槽 1 复活币、槽 2 胜点，真实物品自槽 3 起故不冲突，并载明槽 0 只读不写、收入现进账号金库。`InventoryEntryAssembly` 与 `StateSynchronizationPayloadCodec.Inventory` 两处反向指引该类型 | pass |
| CSC-6 行为逐值不变，且未修改任何断言或期望值 | 本次新鲜运行 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`：`52+60+488+248+4+215+926 = 1993 通过 / 2 跳过 / 0 失败`，与基线逐程序集一致。2 个跳过出自档案守卫用例，是档案已加载的证据而非静默跳过。编译 0 警告。判据 diff 中每一处 `Assert.*` 改动均只是标识符改名，期望值 300 / 10299 / 100 / 1357 / 44 / 1000 与实参、用例数未变；删除并改名的那个判据文件以 `git show HEAD:` 取旧版逐条比对，3 进 3 出、断言一字未动 | pass |

## RQ 覆盖

RQ-001→CSC-1；RQ-002、RQ-003、RQ-008→CSC-3（RQ-008 另经核实：两个成员仍强制传 `AccountId`，未被顺手简化）；RQ-004→CSC-2；RQ-005→CSC-5；RQ-006、RQ-007→CSC-4；RQ-009→CSC-6。AC-001..AC-009 全部通过，其中 AC-004 按 Constraint 口径（`GoldCube` 保留 `Gold` 字样有用户原话背书）。

## 范围核验

`protocol/` 无改动；`AccountWallet*` 未动；未新增任何随身金币写路径、存储或协议改动；携带上限链只改注释与文档，功能性改动留给后续结果。

## 与评审的关系

评审的 `approved` 只证明其自身范围。上表每一行的证据均为本阶段新鲜取得或可当场复现，未以评审结论替代验证。
