---
kind: rks-review
verdict: approved
---

# GM Web 根交付 F-001 独立重审

本轮目标是已接受基线 `a4195c310a3b11eb5e8c1798cc0680ccdec35a44` 加上当前冻结的两文件局部候选。分支为 `feature/gm-web-admin`，工作区为 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/gm-web-admin`，root_review仍in_progress；本候选尚未被controller接受。本轮未将它称为最终accepted结果。

producer为root，reviewer为原独立审查者 `/root/gm_root_review`，未实施修复。采用 reviewing-work 的 Code 与 Deliverable 判据，本轮仅重审首轮F-001修复及直接回归；完整原基线 `5fbe2dad35bf871a048ce4667055751e973bd844` 至 `a4195c31` 的SC-1..6集成检视及未变证据，沿用 [root-review-r1.md](root-review-r1.md)。该首轮报告SHA256为 `F1DED513609A0AFFE66D1D36ED474A321C2A1C953F63783089DF765158B82A59`，其内容及六个sealed结果未由本轮修改。

已直接核对 [root-rework-products.md](../root-rework-products.md) 及两个当前源码文件。当前LF字节清单SHA256为 `380FE146F3856D25D8D63391572A4624FA391A100446F9FD8AB7BF79DFCB02BF`；实际源码身份均符合清单：

| 当前候选产品 | SHA256 |
|---|---|
| Web/gm-admin/src/OfficialMailView.tsx | D31F01D8D417CB93449283EF15E96FBD9FD6679C3DA4719752708681619FEC56 |
| Web/gm-admin/src/OfficialMail.test.tsx | BC1E459DE155DC16066F785B66C03FC373DF6F786981A47393A8434FD059B49D |

读取实际git diff及当前捕获/恢复源码后确认：当前两文件保持HEAD原LF，完整diff为36插入/2删除。产品功能改动仅为邮件send错误分支优先处理401/403；测试改动为cleanup导入及参数化的两条组合行为回归。工作流文档属于root durable游标，不作为额外运行时产品。

**判定：approved，F-001已解决。** 当前候选与首轮完整检视结合后，无未解决的阻塞缺陷。批准是审查判定，不替代主控新鲜验证、一次接受根rework及接受后根验证。

## 修复与决定性证据

`OfficialMailView.tsx:76` 在401/403时先调用 `refused`，不再落入删除pending的通用4xx分支。因此既有未知请求的UUID和冻结输入仍保存在原管理员的sessionStorage中；`onExpired`或`onDenied`仍沿用AdminApp清除私有页面，未通过保留失权资料来修复恢复问题。普通业务明确拒绝/回滚释放冻结内容、未知结果保留原编号、成功收据恢复后清除pending的既有分支未改变。原alive/generation检查仍阻止过时响应恢复资料。

新增测试实际挂载AdminApp/OfficialMailView，通过用户选择、预览和确认建立pending，首次发送注入未知响应，第二次安全重试注入无outcome的401或403，恰好覆盖首轮缺陷。它们逐项断言：

- 身份失效后邮件编辑与待确认私有界面消失，sessionStorage原字符串保留。
- 切到管理员2并重挂，无法看到管理员1的pending，且不改管理员1存储。
- 恢复原管理员并重挂后可见原pending，发送次数仍为2，没有自动发送。
- 第三次显式安全重试的payload与前两次完全相同，恢复replayed收据，最后清除pending。

这组判据观察现有页面行为与真实存储接口，未复制实现逻辑为另一份判据。当前测试模拟HTTP未知响应和身份恢复，不冒充真实浏览器断网或真实资产到账证明；服务端持久防重/实际提交后恢复的证据仍沿用首轮直接检视。

已只读核对 `artifacts/gm-tests/root/red-mail-auth-retry.log`：原行为401/403两项均在原冻结字符串应保留处得到null并失败，另16项邮件测试通过；`green-frontend-tests.log` 为4测试文件56项全通过；`green-frontend-build.log` 为TypeScript检查与Vite构建成功。这些输出早于纯换行LF恢复，语义和测试输入相同；当前冻结字节仍须主控后续fresh检查，不能将旧字节身份当成最终候选验证。已有明确400/409/已回滚500、404保留pending、未知UUID重试、失权隐藏和乱序保护测试仍在完整前端集合中。上述执行由主控完成，本轮没有运行共享构建/测试/package/浏览器，也没有产品或Git写操作。

## 接受和待验边界

首轮旧zip及其3资产身份属于 `a4195c31`，不能用作当前已改前端候选的最终发布身份。本轮批准允许主控继续对当前冻结候选执行fresh验证、重建/验证GM发布资源，并由controller一次接受完整root rework；之后应基于新的accepted HEAD核对干净候选及根验证。本轮不提前声明最终候选、root terminal或整个任务完成。

首轮残余边界不变：A21官方邮件/附件领取表现、在线提醒接收及重登三币余额仍由GM01..21人工验收；真实浏览器断网恢复尚未实测；Linux/远程CI未执行。服务器/自动测试证据保持CurrentImplementation。现服部署、真实管理员及玩家资产、push/PR/merge/Release仍不在根授权内。这些清楚声明的待验范围符合根合同，不构成本轮新增阻塞。
