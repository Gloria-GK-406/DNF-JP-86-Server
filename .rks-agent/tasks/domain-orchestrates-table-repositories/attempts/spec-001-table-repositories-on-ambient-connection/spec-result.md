---
kind: rks-spec-result
spec: ./spec.md
contract: ./TaskContract.md
change_review: ./reviews/change-review.md
verification: ./verification/completion-evidence.md
outcome: completed
baseline: ea95d491fd9d706bf4f7bc7e1b465040081223ef
---

# spec-001 结果：九个仓储退回单表读写，跨表组装移入领域层

## 交付了什么

九个既跨多表又在里面做业务判断的仓储被拆开，删掉 3143 行；换成 58 个单表仓储，
每个只做增删改查、只提一张表、不自己开连接也不自己开事务。它们通过一个共享的
`SqliteDatabase` 走当前用例的连接：用例存在就加入它的事务，不存在则读走一次性连接、写直接拒绝。

原本埋在 SQL 里的跨表编排上移到领域层，成为三个装配加五个服务，每个入场记录只有一处构造。

被删的九个：

| 行数 | 文件 | 原先跨几张表 |
|---:|---|---:|
| 920 | `Inventory/SqliteInventoryEntryRepository.cs` | 14 |
| 761 | `Characters/SqliteCharacterEntryStateRepository.cs` | 33 |
| 369 | `Characters/SqliteCharacterRepository.cs` | — |
| 303 | `Accounts/SqliteAccountEntryStateRepository.cs` | — |
| 276 | `Characters/SqliteDungeonPersistentEffectRecoveryRepository.cs` | — |
| 240 | `Inventory/SqliteEntryInventoryMutationRepository.cs` | 3 |
| 104 | `Accounts/SqliteAccountRepository.cs` | — |
| 91 | `Characters/SqliteCharacterPvpSkillProfileRepository.cs` | — |
| 79 | `Characters/SqliteExpertSkillContractReconciliationRepository.cs` | — |

`Server/src/Dfo.Server.Persistence/` 现在是 14 个文件。

## 怎么证明行为没变

三份「整条记录写成文本」的金样本，在拆之前对着旧实现捕获，拆完之后逐字通过：
人物入场（33 表、215 行）、账号入场（8 表）、背包（12 表、两条记录 428 个值）。
播种做过失盲校验——同一父对象下不存在两个字段取到同一个值，行的 id 顺序与槽位顺序刻意做反——
所以任意一次同级列对调或任意一处丢掉 `ORDER BY` 都会让它红。

协议侧 203 条全过，改动行中断言 0 条，全树 260 种、910 处字节与十六进制字面量与基线一字节不差。

## 过程中查出并修掉的四处真实回归

拆分本身不产生红灯，所以每一步都靠「与被删代码逐条对照」找问题，四处都是这样查出来的：

1. **64 KiB blob 上限被丢掉**（T-004）——账号侧的存储上限校验在拆分中消失。
2. **账号写入的「必须命中恰好一行」守卫被丢掉**（T-005）——账号行不存在时，
   幸运星会被结算成已发放并写下 `{"granted":true}`，原本这条是死信。
3. **货柜多读了时装明细**（T-006）——账号货柜从不读时装明细表，我让两处载入共用了补明细那一步，
   于是货柜里任何一件时装都会让整次载入抛错。
4. **全新库上选角必抛**（评审阶段）——`dungeon_persistent_effect_outbox` 是全库唯一一张
   生产代码读写却无人声明的表。旧代码先问一句 `sqlite_master` 有没有它、没有就安静返回；
   拆分时以「唯一归属者创建全部表」为由删掉了那一问，而这张表恰是那个前提唯一不成立的。

另修掉一处守卫空洞：建角的 `INSERT … RETURNING` 为了读回 id 走了读路径，
是全仓唯一一条能在用例边界外落地的持久写。

四处都各有新增用例钉住。

## 接受的行为变化（两处，均非对外可观察）

- 出库效果与每日补给随所在用例结算，不再各自一个事务；失败时整次入场被收回。
- 一次失败的选角会带走被顶掉角色的最后站位。

## 经评审授权的规格例外（一处）

补上 `dungeon_persistent_effect_outbox` 的声明，表 52 → 53、列 404 → 423。
不补则「不查表结构元数据」与「选角能成功」二者只能取一。

## 未决事项

并发「先读后写」的第二个用例会等满 30 秒再失败。已写入 `AGENTS.md`，待用户裁决。

另有九条一致性欠账（接口重复、契约归属错位、行类型两种约定等），已在评审记录里批为后续清理单，
不动摇任何 RQ。

## 证据

- 变更评审：`reviews/change-review.md`（三个维度独立核实，approved）
- 完成验证：`verification/completion-evidence.md`（新鲜构建 0 警告，全量 1037 / 2 / 0）
- 八个任务单各自的执行记录与独立快评：`tasks/TASK-01..08-*.md`
