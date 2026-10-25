# 历史改写对照表（2026-10-25）

2026-10-25，分支 `task/refactor-session-20261021` 的 `main..` 区间改写了一次：从每个提交里删掉本任务目录下的原始证据，同时给 28 个没带任务前缀的 spec-002 提交补上前缀 `serial-gameplay-executor: spec-002`。做法、证据规则和后续流程见 [process-reset-2026-10-25.md](process-reset-2026-10-25.md)。

- **删掉了什么**：`verification/evidence/` 与 `verification/logs/` 下的非 Markdown 文件，以及 `*.trx`。共 14720 个文件，约 4.0 GB。
- **没动什么**：`Server`、`Tool`、`Docs`、`protocol`、`AGENTS.md`，以及所有 Markdown 记录。逐提交核对的结果是：每个新提交与原提交相比只少了上述文件，父链、作者和时间都不变。
- **原始证据还在本机**：已恢复到原路径，由本目录的 `.gitignore` 忽略，文档里的链接在本机仍然可用。
- **旧提交号**：任务目录里已提交的文档仍写着改写前的提交号。这些文档没有改，因为不少文档记着其他文件的字节数和哈希，改了会自相矛盾。按下表换算即可。改写前的完整历史保留在本地分支 `backup/serial-gameplay-executor-pre-purge-20261025`，只在本机，不推送。

常用的几个：

| 含义 | 改写前 | 改写后 |
|---|---|---|
| spec-001 Spec 基准 | `a02bce46` | `a02bce46`（未变） |
| 根改道提交（登记的 foreign-commit 例外） | `9d146830` | `f4ade320` |
| spec-001 TASK-35 | `0ecb9af3` | `d602592c` |
| spec-001 封存 | `e6f55558` | `2079d98d` |
| spec-002 激活（spec-002 Spec 基准） | `10a7218a` | `51bd1e9c` |
| TASK-32 | `17d924a0` | `da4130c8` |
| Spec B6 / Plan P7 修订检查点（改写前一刻由 Claude 代为提交） | `689da68d` | `0d3777ca` |

## 全表

「删除数」是该提交的树比原提交少的文件数，是累计值，不是该提交新增的数。

| 改写前 | 改写后 | 删除数 | 提交标题（改写后） |
|---|---|---:|---|
| `13c9e2d60f20` | `13c9e2d60f20`（未变） | 0 | online-identity-state-machine: planning |
| `e337660b9e2d` | `e337660b9e2d`（未变） | 0 | online-identity-state-machine: implementation |
| `e94983539b98` | `e94983539b98`（未变） | 0 | online-identity-state-machine: remediation of change review |
| `ea6a15442e7b` | `ea6a15442e7b`（未变） | 0 | online-identity-state-machine: seal completed |
| `b97e6662c221` | `b97e6662c221`（未变） | 0 | session-currency-checks: planning |
| `97c5af5d88b2` | `97c5af5d88b2`（未变） | 0 | session-currency-checks: implementation |
| `7b55d4bf1cd3` | `7b55d4bf1cd3`（未变） | 0 | session-currency-checks: remediation of change review |
| `a29d75863eb7` | `a29d75863eb7`（未变） | 0 | session-currency-checks: remediation of fix review |
| `b01f684162b4` | `b01f684162b4`（未变） | 0 | session-currency-checks: pin the queued experience in the revoked-leave test |
| `a02bce469ec7` | `a02bce469ec7`（未变） | 0 | session-currency-checks: seal completed |
| `b76504bb4edd` | `b76504bb4edd`（未变） | 0 | serial-gameplay-executor: planning |
| `f01dc22603ab` | `9a9e78f89c6d` | 6 | serial-gameplay-executor: TASK-01 baseline and characterization done |
| `53c72543a31e` | `7bd0886e07fb` | 6 | serial-gameplay-executor: TASK-02 event chain and cross-connection order done |
| `5075a283d00e` | `10b26ac5ec98` | 6 | serial-gameplay-executor: TASK-03 measurement tool and baseline done |
| `5a52a13a1f01` | `5751a19058fc` | 8 | serial-gameplay-executor: TASK-04 executor and call-chain components done |
| `0223e646bd4f` | `40c5ede11ec7` | 12 | serial-gameplay-executor: TASK-05 outbox and outbound sender components done |
| `c19b3b7b9e59` | `60d50ff39775` | 17 | serial-gameplay-executor: TASK-06 test helper and first test migration done |
| `422ec64da592` | `36a973df10e0` | 24 | serial-gameplay-executor: TASK-07 application test migration and probe done |
| `690b99268e63` | `7bc67e281468` | 28 | serial-gameplay-executor: TASK-08 executor registration and direct-send deadline done |
| `1eea890fbe02` | `528329fa1e46` | 35 | serial-gameplay-executor: TASK-09 inbound protocol messages on the executor done |
| `ca71eb26091b` | `1a2b43ddfe3d` | 49 | serial-gameplay-executor: TASK-10 connection lifecycle and shutdown on the executor done |
| `648fbc60e27c` | `674e614e8e6b` | 63 | serial-gameplay-executor: TASK-11 timer sources on the executor done |
| `d32f15b4109b` | `b8b1fede7c50` | 80 | serial-gameplay-executor: TASK-12 event deliveries on the executor done |
| `2d8cec4d75d2` | `d3f740c1e46f` | 86 | serial-gameplay-executor: TASK-13 deflake the scheduled-task executor test done |
| `bfbcf012fddc` | `f9ae9656c198` | 103 | serial-gameplay-executor: TASK-14 GM/HTTP entries and startup work on the executor done |
| `6331b23a0e4d` | `c5139a39640d` | 124 | serial-gameplay-executor: TASK-15 content warmup before listening done |
| `c3115f9a57f7` | `fdc31b1b197c` | 124 | serial-gameplay-executor: amend RQ-091 acceptance per user (2 s per CMD) |
| `f13e612a6a9c` | `52506d241f28` | 143 | serial-gameplay-executor: TASK-16 T-004b reconciliation done |
| `6a3bff70a9bb` | `e0572b5e3536` | 164 | serial-gameplay-executor: TASK-17 T-004c1 bus flip, gate assertions and conflict dispositions done |
| `0c9c59c82b67` | `eb48eb126a75` | 176 | serial-gameplay-executor: TASK-18 T-004c2a RQ-061 write assertions, leaked-flow refusal and zero-violation run done |
| `0d8d4fad391f` | `64a520e3385d` | 188 | serial-gameplay-executor: TASK-19 T-004c2b job-host structure judge, AC-003 #3 and T-004 closure done |
| `5f7dba11710c` | `8a65e63a9294` | 188 | serial-gameplay-executor: amend plan T-004 row to the actual split (F-74) |
| `b1959f3ebe96` | `a5bb9fce449a` | 194 | serial-gameplay-executor: TASK-20 M-1 measurement gate passed (2 s acceptance) |
| `09e4983c8cf1` | `1bed5d590aa8` | 206 | serial-gameplay-executor: TASK-21 T-005a protocol-layer gates and retry loops removed |
| `a44224801605` | `87f473c323fe` | 218 | serial-gameplay-executor: TASK-22 T-005b application gates, access modes and multi-account leases removed; T-005 closed |
| `50d7bce487c4` | `434c9553b9d9` | 232 | serial-gameplay-executor: TASK-23 T-006a delivery flipped to the outbox and outbound sender |
| `1ee1c91e42fa` | `fe738edc3a30` | 244 | serial-gameplay-executor: TASK-24 T-006b appendix E carried over, party delivery gates and gate assertion removed |
| `a861c4ded2ba` | `685a493a0899` | 255 | serial-gameplay-executor: TASK-25 T-006c send doubles observe the real outbox; notification bus registers one batch per recipient |
| `8c531c9bdd96` | `02fd3f4f12e5` | 268 | serial-gameplay-executor: TASK-26 T-006d closure written; F-115 routed to TASK-27 before M-2 |
| `f7bf0aa3c37a` | `ce94c79383dd` | 280 | serial-gameplay-executor: TASK-27 party title capped at 256 B (F-115); T-006 complete |
| `06ca5e0f2ada` | `31a76dc1b91f` | 286 | serial-gameplay-executor: TASK-28 M-2 measurement gate passed (2 s acceptance) |
| `9d146830d129` | `f4ade320965d` | 286 | serial-gameplay-executor: reroute to multi-spec (root contract rev 4, map, spec-001 adopted) |
| `bc735cd07d29` | `8f366ff80ecc` | 286 | serial-gameplay-executor: spec-001 amend spec/plan R3 (T-008 handed to spec-002, T-009 narrowed) |
| `a9c79e983da6` | `03f12d64b011` | 298 | serial-gameplay-executor: spec-001 TASK-29 T-007 call chains inherited, depth guard settles and closes |
| `122576d5c3bb` | `dc1ee70d2540` | 311 | serial-gameplay-executor: spec-001 TASK-30 T-009a state census, write hooks and hardened judges |
| `2bdb1afd663f` | `afb2e39f13aa` | 323 | serial-gameplay-executor: spec-001 TASK-31 T-009b log surface and protective cases (code freeze) |
| `a5e3bccbd63b` | `b43908e95d05` | 329 | serial-gameplay-executor: spec-001 TASK-32 T-009c final measurement M-3 passed (2 s acceptance) |
| `51c294faba6d` | `64ad7aaee6d9` | 338 | serial-gameplay-executor: spec-001 TASK-33 T-009d AGENTS.md, architecture notes and appendix reconciliation |
| `9aeb638c0081` | `59f9358f6881` | 351 | serial-gameplay-executor: spec-001 TASK-34 T-009e closure, transition list and A final report |
| `0ecb9af351cf` | `d602592cdc77` | 391 | serial-gameplay-executor: spec-001 TASK-35 absorb timer job failures and reconcile final evidence |
| `e6f555581828` | `2079d98d179f` | 421 | serial-gameplay-executor: spec-001 seal completed after fresh full verification |
| `2e742a473381` | `655120c96564` | 421 | serial-gameplay-executor: root accept spec-001 and evaluate protocol CMD successor |
| `10a7218a7794` | `51bd1e9cb7b4` | 421 | serial-gameplay-executor: root activate spec-002 protocol-level CMD |
| `49ce251a5321` | `1fabc832e79d` | 421 | serial-gameplay-executor: spec-002 planning protocol CMD migration |
| `e0604195b2ad` | `255e87ad9650` | 470 | serial-gameplay-executor: spec-002 TASK-01 owned pools and closed send logs |
| `aa1e732b0af5` | `d67ca7f2a877` | 529 | serial-gameplay-executor: spec-002 TASK-02 currentness guards and deferred composition ban |
| `26d0e4d1bcce` | `ce7944d47767` | 590 | serial-gameplay-executor: spec-002 TASK-03 registration and source migration baseline |
| `5d83ce884b43` | `751386da3f8f` | 746 | serial-gameplay-executor: spec-002 TASK-04 explicit command backbone |
| `276a43389c24` | `3c441dc77516` | 819 | serial-gameplay-executor: spec-002 TASK-05 remaining Channel commands |
| `d2e07630c3b4` | `5da20441825e` | 922 | serial-gameplay-executor: spec-002 TASK-06 LOGIN protocol command |
| `919c59516adc` | `dbdae13f34a7` | 1022 | serial-gameplay-executor: spec-002 TASK-07 roster protocol commands |
| `c57eb0d6f883` | `bdbcd49f9ac4` | 1095 | serial-gameplay-executor: spec-002 TASK-08 character detail protocol command |
| `d1020731bac4` | `35cf4904e1db` | 1199 | serial-gameplay-executor: spec-002 TASK-09 explicit direct effect footprints |
| `2d8081ae2a3e` | `1e047c33634d` | 1381 | serial-gameplay-executor: spec-002 require explicit effect footprints across shared effects |
| `30a173612334` | `858912e29fd0` | 1381 | serial-gameplay-executor: spec-002 clarify committed effect registration failure boundaries |
| `1cf5051a4566` | `137ebcb6c315` | 4157 | serial-gameplay-executor: spec-002 migrate whole UseStackable command with explicit settlement |
| `247a63451c19` | `4bc4b84d49d9` | 4279 | serial-gameplay-executor: spec-002 migrate whole BuyItem command and exact request family |
| `de65601cc9fa` | `81e94a146e3c` | 4386 | serial-gameplay-executor: spec-002 migrate equipment reinforcement disjoint and enchant commands |
| `0d5df6a4288a` | `d80f2b785423` | 4485 | serial-gameplay-executor: spec-002 migrate magic seal forging and outworld commands |
| `f930f26cc681` | `a35fd3f2d615` | 4489 | serial-gameplay-executor: spec-002 approve P3 confirmed rollback result boundary |
| `d7373b9dd331` | `3b82906448e3` | 4790 | serial-gameplay-executor: spec-002 migrate opening protocols with confirmed rollback results |
| `1dc9bfb1f495` | `0a182b5107f5` | 4916 | serial-gameplay-executor: spec-002 migrate quality and random option protocol commands |
| `c03ae3476d52` | `a961e7920dcf` | 5061 | serial-gameplay-executor: spec-002 migrate socket protocols to one explicit command |
| `3118ad0907b6` | `9096fed7fdf7` | 5180 | serial-gameplay-executor: spec-002 migrate avatar compound to protocol command |
| `dba3ede68e15` | `deec8246878f` | 5382 | serial-gameplay-executor: spec-002 migrate warehouse family to protocol command |
| `d6708faf9b54` | `5d9b6bcf842b` | 5382 | serial-gameplay-executor: spec-002 approve collection batch registration compatibility boundary |
| `68f6d572f4b3` | `c03c59faa722` | 5576 | serial-gameplay-executor: spec-002 migrate CollectionBox protocol and preserve guard failures |
| `a6f19c44f0b3` | `4b894ff3d659` | 5729 | serial-gameplay-executor: spec-002 move inventory sorting into protocol commands |
| `7c9f39c1d974` | `9a3afc555871` | 5908 | serial-gameplay-executor: spec-002 migrate item sale and retain refusal rollback |
| `c0e7245cba28` | `a459ad4a3e5e` | 6057 | serial-gameplay-executor: spec-002 consolidate delete item protocol command |
| `9b7302514700` | `78854fc66a13` | 6192 | serial-gameplay-executor: spec-002 consolidate repair item protocol command |
| `6f9917e5f7ee` | `0d196e1ec83c` | 6332 | serial-gameplay-executor: spec-002 consolidate crystal contract protocol command |
| `90b88aae3b07` | `073d3ba9a970` | 6577 | serial-gameplay-executor: spec-002 consolidate inventory movement protocol command |
| `1aae55555ed2` | `73608d7d85c0` | 6726 | serial-gameplay-executor: spec-002 Clarify notification registration outcome |
| `f42efb55beda` | `cc4cb9367f60` | 6791 | serial-gameplay-executor: spec-002 Record legacy Cera purchase failure boundaries |
| `c74650a7869a` | `a16d9a0f1c04` | 6791 | serial-gameplay-executor: spec-002 Define Cera purchase failure migration boundaries |
| `6e21892a9d29` | `35523de4d5c5` | 7063 | serial-gameplay-executor: spec-002 consolidate Cera purchase into protocol command |
| `d3a5fec85b9e` | `26f356fa10f1` | 7220 | serial-gameplay-executor: spec-002 capture mailbox self-settlement and failure boundaries |
| `db4e17c4ad4a` | `355388e03db0` | 7220 | serial-gameplay-executor: spec-002 approve mailbox failure boundaries and self-settlement plan |
| `e878c4ede03b` | `4eba5c9d8a86` | 14140 | serial-gameplay-executor: spec-002 migrate mailbox commands with self-settlement recovery |
| `17d924a06b12` | `da4130c890fd` | 14720 | serial-gameplay-executor: spec-002 capture remaining opening and rental settlement boundaries |
| `689da68dd96f` | `0d3777caaa59` | 14720 | serial-gameplay-executor: spec-002 Spec B6 / Plan P7 amendment |
