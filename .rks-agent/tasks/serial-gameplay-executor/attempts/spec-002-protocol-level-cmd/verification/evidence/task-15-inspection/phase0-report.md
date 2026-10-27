---
kind: rks-verification
decision: pass
claim: TASK15 Phase 0 old-route protocol oracle only; not Task15 completion or Phase1 approval
---

# Phase 0 — 旧 opening route 完整协议基线

HEAD `f930f26cc681982daf1f7663867c7ef7e653f877`，branch `task/refactor-session-20261021`。Task r1 SHA `5b25469205b5c7ab3f8acf14d9fc073c1e98a9a87c6c19446a9a65ec802e66c0`。仅三个授权测试/fixture源变化，`phase0-source-freeze.json`逐SHA；19 production/AGENTS路径（含尚未新建者）与HEAD精确相同，12799非owned tracked路径SHA未变。未stage/commit，无产品/route/core/ledger写入。

## 真实判据与实际结果

- Fresh ConfigureServices-only capture：`capture-before-01`，173logical/203cmd，18Protocol+1Internal+184Legacy，156Tx/34Direct/9RO/4Workflow。没有BuildProvider、宿主或游戏启动。
- 完整协议：`EffectRequestTransport`用原all-frame counter一次Observe、真实body/decoder/dispatcher/legacy fine/真实SQLite/原outbox，无第二provider、fakeparticipant或孤立provider替代。两具名fine wrapper调用原handler，只读同transaction FK/奖励SQL/receipt，并保存原transaction实例。ledger观察只读原私有CWT，没有Admit或写状态。
- Package action SQL：selector1/type160/seq7/body5000/Ordinal1；真实奖励INSERT触发19/1811，原action未返回receipt，原fine logging和Package handler均Error且ReferenceEquals同cause。原失败CMD frame精确保存，失败body `0000FFFFFFFFFFFFFFFF000000000000000000000000`。transaction与ledger ConfirmedRollback，projection为空。
- Batch native Commit：selector1/type1104/seq7/body005000E80300007800D00700000100/Ordinal1。实际participant连接FK=1，原action返回receipt后同连接可见奖励3数量2、deferred child2、事件6/delivery12（before2/4）；随后真实`SqliteTransaction.Commit`抛19/787，完整stack见observations/console。原fine logging和Batch handler均Error且同一原cause。原失败body0002。transaction与ledger ConfirmedRollback；Freeze已经发生，projection真实保留，不伪造清空。
- 两条失败均核源2/材料5/奖励0/gold100000/luck0/事件2/delivery4与before相等，冷却未开启；失败没有成功batch/通知、仅单失败ACK，无fence与source-close。
- 删除各自trigger，再发同seq/raw：同entry和原Ordinal1，仅一次成功并Committed；Package源1/奖励2/材料仍5，Batch源1/材料4/奖励3数量2+6000数量1/luck1；冷却真实开启，事件/投递记录成功增加。第三次同请求只current-state replay，实际fine action总2次，ACK总2（失败+成功），资产与事件不再增加，无fence/source-close。
- DDL、raw/hash/selector/type/seq/Ordinal、ACK fullframe、原cause stack、同cause+Error级别、资产/事件/retry均在`phase0-observations.json`和oracle04完整console，直接断言而非仅日志字符串。

## 检查与诊断身份

每个native步骤有独立caffeinate、command/cwd/HEAD/UTC起止/exit/elapsed/resources及完整console；均自然结束，无TERM。

- capture-before-01：exit0。
- oracle-01：exit1，新增测试缺`using Dfo.Server.Abstractions.Networking`导致CS0246；未运行case，属于测试编译修正，**不是行为RED**。原console/meta保留。
- oracle-02：exit0，两完整旧route oracle 2/2通过。
- oracle-03：exit0，新oracle2＋原PackageOpeningTests31，共33/33通过，0skip；原Store_failure_does_not_start_the_opening_cooldown及原Throws/SQL/并发/奖励/冷却目的保持。
- oracle-04：exit0，主控预检要求日志级别具体化后最终源码两case 2/2通过；Error级别+同cause已断言并输出。没有生产实现，因此本阶段未声称TDD RED/GREEN。

`phase0-sources`保存最小三个旧路线oracle/fixture/driver的实际版本，`before`只精确owned原byte；非owned仅SHA，无全仓快照。新Transport四shape编码只供本簇，旧Body分支/seq/replay未改；OpeningFixture仅可选configure钩子，原31case保持。

## 阶段边界

此pass仅证明Phase0产品前门的旧route真实完整协议前提；尚未实现/验证WriteResult、来源事实/guard/保因、新四whole或迁fine，尚未跑本Task最终八full/DFO/Golden/Char/N-D/闭账。Task15未完成，Task/ledger未追加Result或关闭行。所有共享写与native已停止，等待主控核gate并正式授权Phase1。
