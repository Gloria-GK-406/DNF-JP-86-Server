---
kind: rks-verification
decision: pass
---
# 固定等级券完成验证
2026-10-01；基线c8f25b6ce2cd6bce01a00fe672405788c9d69641。精确主张：固定等级券已实现、自动检查及独立代码审查完成，新服务就绪供用户继续实测；不主张实机已通过。

| 条件 | 决定性结果 | 证据 |
|---|---|---|
| SC-1 | 四种真实券target/percent/fixed/-1正确，合成坏声明显式抛异常，未知合法模式拒绝而无NPC回退 | final-Dfo.Server.Infrastructure.trx、final-Dfo.Server.Application.trx、../reviews/change-review.md |
| SC-2 | 成功覆盖指定等级、90%边界失败保留、0%失败；仅扣一张券，保护/金币/无关字段及UID保持；handler实际提交后ACK与槽通知、重新加载持久化一致 | final-Dfo.Server.Application.trx 中Fixed_tickets、Ticket_handler等；source-fingerprint.json |
| SC-3 | 错类型、锁定、过期、无气息/未辨识/零值、封装、禁用和耐久拒绝无支付；SQLite失败回滚；重放不重抽/扣券，新sequence才再消费，NPC与保护回归正常 | final-Dfo.Server.Application.trx；独立审查表 |
| SC-4 | approved当前源码，完整Release与Probe构建无警告/错误；八项目回归通过5236（80+2626+26+50+750+86+392+1226）、失败0，Infrastructure另跳过2个missing-archive用例；静态字段访问审计通过；数据库备份、新运行目录、四监听与API ready、资产和PVF一致，人工清单已交付 | final-checks.json、test-summary.json、final-*.log/TRX、deployment.md/json、../human-test-list.md |

首轮完整Application有一项墙钟耗时失败，已保留first-full-Application及first-final-checks；通知代码和测试无改动。原样隔离复验通过，随后全套复验全部通过。未放宽断言、未隐去失败，调度原因仅为支持的解释而不是确定诊断，细节见implementation-evidence.md。

当前1567个源码输入的哈希与独立审查目标全部一致，发布Dfo*.dll匹配当前Release构建。Git接受门要求HEAD仍为基线并只包含本任务变更；通过后创建一个完整结果提交，提交成功及工作区/源checkout身份由外部Git结果确认，最终封存以该成功操作为准。
