# 守护者祭坛交付边界与验证

适用功能：#168，A21守护者祭坛DGN3511–3523。用户2026-10-28明确接受原生死亡倒计时页面并授权PR收尾。
当前分支`codex/168-guardian-altar`已基于主干`3e3aea4ab94f1411a33eeace003aa8ef74ca2e55`整合；
合并前的审核及CI必须绑定PR当前提交，以GitHub上的实际回执为准。

## 用户验收

明确反馈覆盖：原生入口、首关简单玩法、中等出怪、友军召唤，以及复活币显示恢复正常；
升级／设置反馈为初步正常。用户选择接受通用死亡倒计时，不通过篡改死亡状态或删除0020隐藏界面。
倒计时触发与协议边界见[死亡界面](death-countdown.md)。

深层及生存全程、跨日、重启、满包与50秒自动回城没有逐项实机确认，继续保留该边界；
自动验证覆盖对应服务端状态及持久化行为，不将它升级为ClientVerified。
测试步骤与实际连接配置见[验收清单](acceptance.md)。

## 当前主干整合验证

验证使用隔离测试库，实际档案通过`PVF_ARCHIVE_PATH`传入，不对玩家库执行测试夹具。
日志均在`artifacts/guardian-altar/`。以下项目范围有交集，不相加作为独立用例总数。

| 验证 | 结果 | 日志 |
|---|---|---|
| 完整Release构建 | 0警告、0错误 | closeout-build.log |
| 服务端CI同分类Unit | 1629通过，0失败、0跳过；分项37+792+23+20+757 | closeout-unit-final.log |
| 祭坛、路由、死亡复活、房间与架构集成 | 270通过，0失败、0跳过 | closeout-application.log |
| 协议全量，提供实际PVF | 2073通过，0失败、0跳过 | closeout-protocol-final.log |
| 祭坛实际PVF内容 | 2通过，0失败、0跳过 | closeout-content-final.log |
| Schema归属 | 3通过，0失败、0跳过 | closeout-persistence-final.log |
| GM前端 | 13个文件、158项通过 | closeout-gm-test.log |
| 测试报告守卫 | 3通过 | closeout-report-guard.log |

主干的新鲜度检查由`GameRequestFreshness`统一提供；祭坛在唯一命令入口检查一次。
`DungeonOwnerRoutingSourceTests`将0392–039A入口按具名位置与次数登记，
反例继续拒绝MutateAsync内重复判断，不对祭坛目录作宽泛放行。
主干巨龙路由、DungeonLocator参与状态以及死亡处理拆分保留；祭坛死亡工作单元显式接收原命令执行上下文。

## 运行与集成约束

当前实机候选产物来自变基前提交be35aad6；变基后的对应编号修正提交为d60a15e6。
实际运行程序、PID、数据库、备份和就绪状态由部署manifest记录，不能从当前源码HEAD推断运行身份。
本次PR准备未切换运行服务。运行产物、玩家库和有效取证资料须在任务工作区清理时保留。

源码版本与最新主干均为0.2.10，PR不引入VersionPrefix变动；收尾不创建tag或Release。
PR通过当前提交Codex审核及CI后才以merge commit合并，随后按
[主干收尾规范](../../server-ci-release.md#合并与清理)处理分支与工作区。
本记录归档本地验证与验收范围，外部审核、CI和最终合并身份以PR实时记录及最终收尾结果为准。
