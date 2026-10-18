---
kind: rks-verification
decision: pass
---

# 一期实现交付验证（2026-10-15）

范围为TaskContract revision1的服务端实现、内容同步与名称调查；不把实际客户端验收、部署运行或03EA还原算作本次完成。

| 条件 | 当前证据 |
| --- | --- |
| SC-1 共享层重映射／入场回滚 | SaoTowerDomainTests真实PVF用例：请求3699按共享层2进入3601；回滚保留4票且无run发布，成功扣1票、疲劳0 |
| SC-2 共享竞争／100循环／幂等／持久化 | SaoTowerStoreTests真实SQLite双连接竞争仅1胜出；100→下一轮1；旧轮／旧层／重放拒绝；重开文件保留状态 |
| SC-3 全队额外奖励和事务 | SaoTowerDomainTests全队各一次、第二成员失败回滚进度/首通/领取记录；真实PVF入包10004331×1并推进；统一InventoryThenMail接缝，既有ItemDelivery满包邮件回归同次全量通过 |
| SC-4 层数投影 | 03E9八字节顺序测试、通知架构回归；延迟旧run事件仍刷新背包，层数不覆盖另一run；登录/选图接线经独立审查。TryRound映射和时机待实机 |
| SC-5 内容同步 | 每档原始→最终712010表项验证；仅100/102个指定项变化。四开发client+现用server共5份已同步，源/备份/现用SHA经主代理与独立review核对；receipt AllSynced=true |
| SC-6 名称调查 | 100个DGN直接name均“艾恩葛朗特”，无dnf_键；MAP另名“PVP无名”。未取得实际HUD完整键，明确未知，无猜测性替换 |

## 检查结果

- Release解决方案build：0警告、0错误；`artifacts/sao-core-20261015/release-build.log`。
- SAO定向最终13/13通过：`tests/sao-core-targeted-final.trx`。
- 无PVF全量3424通过、393跳过、0失败：`tests/sao-core-no-pvf.trx`及`no-pvf-test.log`。
- 真实PVF串行最终3895/3895通过、0跳过：`tests/sao-core-real-pvf-final.trx`及`real-pvf-final-test.log`。
- 首轮并行回归曾有既有EffectOperationReplay用例5秒等待超时；隔离22/22和串行全量均通过。历史日志保留，不用隔离结果冒充全量。
- 初始Store行为RED与通知遗漏RED均在本次实现中实际复现后修正；通知遗漏由独立审查F-1发现，当前review approved。
- 100层组装检查曾明确失败25/55；PVF精确宽度修复后100层都有有效出生房和敌人，不放宽解析器。
- Persistence最终全量468/468通过：`tests/sao-persistence-final.trx`；新增3表的数量和逐列形状断言通过。
- Infrastructure全量900通过、2跳过、0失败：`tests/sao-infrastructure.trx`。
- `git diff --check`通过；工作分支task/next-round-20261013，接受基线0de58d52417b9101052c855b74f6de1b80ca4c7b；保留无关Tool/WireSmoke/__pycache__/。

## 同步与回退证据

准备清单：`artifacts/sao-core-content-20261015/sync-manifest-v2.json`；实际同步：`artifacts/sao-core-20261015/content-sync-receipt.json`；逐原档备份：`artifacts/sao-core-content-20261015/rollback/`。完整哈希表已持久化到Document/SaoTowerContent-20261015.md。

仅新增3个SAO表；没有破坏性数据库迁移，本次未运行生产schema或改玩家数据。新服务程序集尚未部署到旧runtime，服务器保持停止。上游对照与OneKey发行包保持原状。

实机验收清单见Document/SaoTowerCore-20261015.md：层数/门票/快速重试/首队竞争/100循环/满包邮件，以及25/55战斗。普通翻牌暂留、每日补票和专用奖励下一期补齐。
