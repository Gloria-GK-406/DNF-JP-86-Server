---
kind: rks-verification
decision: pass
---
# Completion Evidence

2026-10-14；TaskContract revision1，基准df570d8700b369632aa819878511f650d22a9419。声明：新物品期限生成修复、1123授权限时物品测试改期和部署已完成；没有声明用户实机登录验收通过。

## Frozen Target

artifacts/item-grant-expiration-20261014/source-manifest.json中27个代码/测试/说明文件SHA逐项重读匹配；发布identity snapshot.0c66b709624d4bef。独立完整审查change-review.md为approved，三项发现修正后通过，受控数据操作也独立核对。

## Checks

| Check | Result | Evidence |
|---|---|---|
| Application完整 | exit0，3875/3875，无跳过 | application-verified.log/.trx |
| Infrastructure完整 | exit0，900通过，2条件跳过 | infrastructure-reviewed.log/.trx |
| Persistence完整 | exit0，468/468 | persistence-final.log/.trx |
| Protocol完整 | exit0，1519/1519 | protocol-verified.log/.trx |
| Host组合/启动 | exit0，11/11 | host-verified.log/.trx |
| 只读模板审计 | 167个真实模板，0未知 | lifetimes.json、lifetimes-pvf.json |
| 历史受控改期 | 19目标；仅期限改变，无删除/其他表变化 | historical-change.json、两个停服快照 |
| 部署 | PID52176、ready、GM200、31监听无缺失、数据库完整性ok且结构未变 | deployment-verification.json |

所有原始日志位于artifacts/item-grant-expiration-20261014。测试环境PVF_ARCHIVE_PATH=artifacts/avatar-socket-text-20261011/Script.server.pvf的绝对路径。Infrastructure两项跳过是刻意只适用于“无PVF环境”的缺档测试：PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog、PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message；本次期限必需的真实4例和非法3例均通过。

并行回归中两项计时超时保留application-reviewed.trx；隔离timing-isolated.trx为2/2通过；未改测试或预算，单独完整application-verified再运行3875/3875通过。初次夹具/陈旧golden问题与修正见implementation.md，未当作最终通过证据。

## Contract Coverage

- SC-1：三种PVF样例、六点前/正点/后及跨年、组合期限最早、显式期限/永久/装扮字段；实际开包组合和商城连续购买回归。
- SC-2：实际领域/SQLite投递、溢出邮件、领取后deadline不变、同日合并/跨日不合并、容器旧0期限不误合并；生产生命周期Create和调用面审查。
- SC-3：用户选择仅1123；按core真实模板ID生成19目标。前后快照由审查者逐表核对：78个非items表、schema、slot完全一致，items UID/行数保持，仅19个授权实例41..44到期字节变为1791820800。162个永久/无期限角色槽位和1个虚拟金币记录保留；没有直接删除。
- SC-4：发布版本0.2.5-item-expiration.20261014+snapshot.0c66b709624d4bef；新exe路径/程序集/PVF配置哈希、监听/就绪均核验。启动后19目标仍在1123下供用户登录；自动领域清理回归随Persistence通过，未新增定时清理回调。

## Result Boundaries

保留旧runtime、改前DB备份与改后启动前完整快照。固定日期旧活动仍然过期；这次不修改PVF时间。不做永久物品、其他角色、账号仓库、邮件、称号簿的测试改期。原有Tool/WireSmoke/__pycache__/不纳入提交。用户接下来正常退出再登录1123完成实机清理验收。
