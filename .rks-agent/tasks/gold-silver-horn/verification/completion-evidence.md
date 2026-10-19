---
kind: rks-verification
decision: pass
---

# 新·金角银角完成证据

核验对象：TaskContract revision1与部署候选`d5b04b34d3f6eab488594425e442eeab2b6175bb`。本pass仅表示授权的调查、实现、自动验证、部署和验收清单已交付，不表示用户已实机验收。

## Success Conditions

| Condition | Result and decisive evidence |
|---|---|
| SC-1 | 042B精确4B与4123第二u16由A21 loader/入口消费及当前PVF闭合；购买材料与开启金币语义闭合。见Document/GoldSilverHorn-Research-20261019/{protocol,rewards}.md。 |
| SC-2 | 真实PVF专项完整15通过，覆盖六MAP、三Boss候选、准入矩阵、三次及建图故障；关联Application109通过覆盖既有活动与开盒/商店；原生观察项列入交付清单。见gold-silver-complete.trx、related.trx。 |
| SC-3 | 专项覆盖事务回滚、持久重载、角色隔离、06:00日切；五商品通过实际购买后零碎片开启，传说罐金币不足/发奖故障回滚与700万成功扣费。Protocol105通过，含042B独立向量、在线日切和失败重试。见protocol-related.trx。 |
| SC-4 | 2026-10-19 17:25:07 +08:00核验PID26172，唯一Host，版本0.2.7+d5b04b34d3f6eab488594425e442eeab2b6175bb；12程序集版本/哈希匹配，31TCP/2UDP匹配原监听，Launcher/GM ready，stderr0。108既有表内容不变、无新增表，integrity ok/FK clean。旧运行和最新停服备份保留。见final-verification.json、deployment-receipt.json、post-start-comparison.json。 |
| SC-5 | Document/GoldSilverHorn-20261019.md提供资格、次数、重登、三Boss、掉落兑换开启、退出、GM开关与日切清单；最终交付消息附入口及重点验收步骤。 |

原始证据目录为`artifacts/gold-silver-20261019/`。解决方案构建0警告0错误；无PVF完整回归8项目7553项，6671通过+882资源条件跳过+0失败。失败基线6 fail，修正后同组6 pass；跳过项不当作真实PVF覆盖。实现之后仅研究文档空白修正，不改变受测生产代码。

## Independent Review

`../reviews/change-review.md`为approved，绑定上述精确候选；本核验补齐审查时尚未执行的实际部署后条件。没有在审查之后修改生产代码。

## Data and Workspace

运行目录`artifacts/gold-silver-20261019/runtime/`，数据库沿用原现服。online-backup.db、stopped.db及旧`artifacts/sealed-treasure-acceptance-20261019/runtime/`保留。未改客户端/PVF或角色资产；18个任务外文件SHA256保持不变。分支task/gold-silver-horn-20261019，未推送或合并，#155仍待用户验收。

## Human Acceptance Boundary

原生UI次数、实际帧时序、Boss攻击AI及掉落体验等仍由用户测试，不升级为WireCaptured/ClientVerified。此边界是任务明确保留的下一步，不是本次服务端交付缺项。
