---
kind: rks-verification
decision: pass
---

# GM Web 根完成验证

2026-10-02，完成声明仅为根合同修订1的开发实现和自动/隔离验证，真实游戏人工验收另列。接受baseline f63b7c67473f2f4d8f7593f704c9e15d4f1c5e38，branch feature/gm-web-admin。独立完整根审查r1及F-001重审当前approved（185410C5），无未解阻塞。当前接受代码与审查冻结2产品字节一致，根rework只增加邮件失权重试保护，不重开六sealed结果。

| 根条件 | 当前accepted-baseline证据 |
|---|---|
| SC-1 独立后台/启动/发布 | 当前Host开关/独立路由矩阵随Spec006完整109 Host测试通过，Server源码与该测试时逐Git树未变；clean f63b7c67原生Windows标准package实际HTTP3资产SHA/MIME、initialized=false、private401/ownprocess cleanup成功，默认3off。 |
| SC-2 管理员/权限/追踪 | 当前独立完整根审查实际检视认证/CSRF/限流/当前权限/成功审计事务；完整HTTP/SQLite/分层测试在同一未变Server源码5373通过，前端56检查私有界面失效/乱序与身份恢复隔离。 |
| SC-3 查询/真实在线 | 全服分页/字面过滤/无角色/姓名诊断、实际会话/频道位置测试及页面检查成立；未变服务端当前回归、完整前端56及root review实际接缝检查。 |
| SC-4 邮件完整闭环 | 既有实际PVF/SQLite邮件与装备UID/共享材料/金币领取、共同事务/持久重试及当前提醒测试；root F-001新增401/403组合RED/GREEN并独立approved，当前56前端包含异管理员隔离/原UUID与输入不变/成功释放。本机Chrome实际编辑预览报告spec004 browser-chrome.md。 |
| SC-5 账号三币 | 当前真实账号无角色/三币CAS/有序租约/共同事务/商城竞争/持久收据/故障恢复和当前会话13B通知测试；实际Chrome单/明确批量/正常刷新记录/跨页选择及只读SQLite独立核对spec005 browser-verification.md。 |
| SC-6 集成与验收材料 | 当前独立完整审查approved；服务端5373通过/0失败/2deliberate skips，构建0警告/0错误；当前UI56/TSVite通过，accepted-baseline Python11通过；CI固定SHA/Windows-Linux矩阵/npm顺序当前核对；最终Windows包与全部说明/GM01..21完整。 |

新鲜执行和身份范围：Spec006完成full .NET8项目5373/2skip后，接受和root局部修正没有任何Server源码变更，当前Git树核对完全一致；不为同一源码无理由重复已通过全量。root修正后完整前端56及TS/Vite、Python11、标准候选actual publish/HTTP重新验证；接受后clean新HEAD再npm ci/build/publish/HTTP并核对最终包。当前root verifier另运行accepted Python11及全部cursor/code/CI/source/PVF/archive身份核对。没有将旧54/UI旧资产用作新修复证据。

最终交付包artifacts/release/DNF-JP-86-Server-0.2.0-win-x64.zip，53211115字节，SHA256 bee83af188bcfe8f5ff0a2999a6ac18bf2cedea0ebec18b8cd55e4538305e45b，校验文件一致/CRC完整。release.commit精确f63b7c67；3资产与新dist/zip/manifest字节一致，已含F-001修复；8说明链接存在且7源文档逐字节相同，默认GM/Launcher/Gamefalse、无DB/PVF/log/.env/Node。

全部约束aligned：Application/HTTP分层由当前架构回归和root检视支持；真实资产写入使用领域/共同事务/持久防重且通知不改变持久事实；文本文字/装备生命周期/实际账号范围明确；一工作区、六unique completed child、active=null，所有accepted commit顺序内容保留，根rework一次接受另列。source main仍5fbe2dad且clean，PVF SHA3B2FE3DA115FEE949282558A864A319FC9CA197E1D1F22E656E74316A0F04B06未变。变化仅根durable收尾记录，不含未接受产品。

证据：artifacts/gm-tests/root/final-verification-proof.json、accepted-package.log、accepted-package-proof.json、accepted-release-tests.log、fresh-code-proof.json及fresh-frontend-*；Spec006 fresh-test-counts.json/full-tests/build；任务reviews/root-review.md/r1、root-rework-verification.md和workspace.md。fresh日志在ignored artifacts，durable结论完整保留MD。

验证前cursor为root_verification/in_progress、last_transition review-approved；六attempt唯一/全completed、active null、SC-1..6 satisfied、constraints全aligned的断言通过。允许一次原子verification-passed封存completed/completed。

真实A21邮件展示、领取体验、在线提醒及重登余额；真实浏览器断网；Linux/远程CI尚未验，均在合同明确交用户边界，未标ClientVerified。本机Chrome连接/自动化路径已实测可用，内置浏览器造成Codex退出的根因尚未确认。真实部署、管理员初始化/玩家发放、PR/push/merge/Release未执行，分支和工作区保留待用户选择收尾。
