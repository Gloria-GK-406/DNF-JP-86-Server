---
kind: rks-verification
decision: pass
---

# 最终验证证据（2026-10-01）

精确声明：装备基础内容与统一规则重构通过自动回归、独立完整审查和用户E01–E08实机验收；具备本地结果提交条件。边界内交付完成，增幅升级交易和晶体x0仍为后续fix。

基线38f71bd2，分支task/solo-play-roadmap。本次包括通用装备信息/严格内容源、统一升级规则服务、四条消费者迁移、启动准备、相关自动测试及任务文档。未改变客户端/PVF/数据库schema；用户单独授权的验收保护券补给记录另存artifact，未混入业务代码。

## 条件与证据

| 条件 | 当前结论与证据 |
|---|---|
| SC-1 | reviews/change-review.md approved；旧装备基本类型/CanReinforce/费用索引/三个旧规则接口无生产残留；四类消费者迁移闭合 |
| SC-2/SC-3 | Infrastructure84/84；真实手镯100310498/项链100310755/戒指100320520完整且MaxDurability null；武器101010437=45、肩甲100170123有耐久；缺失/0/65535/错误/重复/越界/索引错误和重复失败读取均通过 |
| SC-4/SC-5 | Application326/326、Protocol24/24；用户E01–E05、E07/E08通过；E06末次反馈两次失败分别回到+10、+0，装备不损毁，两券各消费一张。非武器锻造的实机拒绝位于客户端选取阶段，服务端拒绝由自动用例覆盖 |
| SC-6 | 命令Error/无ACK/资产不变/再次抛出用例通过；内容读取在结算前，既有commit异常用例另证明结算后回滚；Host26/26含非法索引非零退出、端口关闭、readiness=false |
| SC-7 | Human-Test-List.md保留用户原文和全部项目结果，E01–E08全部通过，E09记录闭合；自动/已部署/人工三个证据层未混同 |

## 自动验证与最终核对

Release全解决方案构建0错误/0警告。真实环境PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf；测试-c Release --no-build --no-restore。相关过滤：Application Equipment或CommandHandlerArchitecture；Infrastructure PvfEquipment；Protocol Equipment/Outworld/Forging/UpgradeItem；Host全项目。

证据根目录：E:/Project/DNF/DNF-JP-86-Server/artifacts/equipment-definition-rules-20261001。

- tests/application-equipment-final.trx：326/326。
- tests/infrastructure-equipment-final.trx：84/84。
- tests/protocol-equipment-real-pvf.trx：24/24。
- tests/host-full-final.trx：26/26。

全部460项无失败、无跳过。最终收尾重新解析TRX计数和Passed状态，核对已部署Abstractions/Application/Infrastructure/GameServer DLL与当前构建SHA256一致；生产/测试代码在审查后无更改，未重复运行相同测试。最终核对记录见verification/final-audit.json。git diff --check通过。

较早无真实PVF跳过记录及host-full.trx 25/26保留为历史证据：旧夹具在掉落准备阻断，而新版先准备装备索引，假PVF提前拒绝导致夹具Entered超时；夹具迁移至首个准备源后focused1/1、Host全项目26/26通过。

## 部署与人工证据

已发布到上述artifact/runtime。沿用原配置及正式DB/PVF；独立smoke.db/独立端口启动HTTP200/status ready，smoke进程已结束。正式启动前确认旧服务与客户端均不运行，pre-deploy.db备份quick_check=ok。PID6048监听7001/10010/10011/18080，装备索引118479条；最终bootstrap仍ready。完整发布DLL哈希见runtime-dll-sha256.json。

用户连续反馈原文与结果见Human-Test-List.md，保护券实际发放依据用户新授权为各1张，expire=0；备份/回执在artifacts/equipment-acceptance-one-each-20261001。最终只读核对UID994/995不再有背包槽引用，符合两券已消费反馈。正式服务日志也记录item101040127两次penalty=3：old11→calculatedLevel10、old10→calculatedLevel0，两次consumeProtection=True、Downgraded；节选在artifact/accepted-protection-failures.log。

## 后续范围

克伦特增幅等级提升交易与设计图晶体x0显示继续待fix，不属于本次验收。不声称全仓所有测试通过，不修改历史协议证据为新的实帧捕获结论。当前连续开发分支与工作区保留供后续工作，不进行push/PR/merge/清理。
