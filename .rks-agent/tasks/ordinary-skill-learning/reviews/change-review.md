---
kind: rks-review
verdict: approved
---

# 普通技能学习完整变更独立审查

2026-09-09。审查基线 `da623c2d2457cf932e7bb8c5748e760e5703ff57`，范围为当前工作树完整未提交变更，包括未跟踪的领域服务、协议切片、测试、协议样本和任务材料。输入身份采用 `../verification/reviewed-inputs.json` 的 35 个 SHA-256；审查开始与结束均逐文件核对一致。审查未修改实现、规格或控制状态。

独立读取 TaskContract、Spec、Plan、AGENTS.md、生产变更及完整新增测试；对照 RQ-001..006 / SC-1..5，未发现需要阻断的缺陷。没有配置对抗挑战集。

学习以当前会话、角色归属、页权限、职业、等级及存储技能为输入，最终批量事实决定前置与账本；槽位算法与参照 SkillSlotAllocator 一致。全部业务拒绝发生在写入前，更新失败与存储异常向外传播；命令同时声明在线用户及账号排他访问，事务位于两把执行门之内，handler 等提交返回才发送。已有槽位及附加内容通过原行更新保留，新行使用普通槽位及空附加内容。未变更页面和角色不被写入。

独立重跑（真实 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`）：

```text
dotnet test Server/86JP.Server.slnx --no-build --filter "FullyQualifiedName~LearnSkillCodecTests|FullyQualifiedName~SkillLearning|FullyQualifiedName~CommandHandlerArchitecture" --logger "trx;LogFilePrefix=review-learning" --results-directory .rks-agent/tasks/ordinary-skill-learning/verification --verbosity quiet
```

退出 0。直接按 XML 命名空间读取每条 `UnitTestResult`：Application 60 + Protocol 17 + Networking 1 = 78 Passed，0 Failed、0 Skipped；其他四项目各 0 个匹配用例，不算通过测试。记录为 `review-learning_net10.0_20260909151529.trx` 至 `...151535.trx`。同时核对原始领域 43 项、整合 48+17+1 项结果和 Release 构建日志，`git diff --check` 通过。

协议证据独立核验：两份原始日志的 SHA-256 与归档一致；客户端转储 `E:/Project/DNF/DNFClient-A21-debug/dumps/dnf-dump-20260831-083553-079-22620/DNF-memory-rva.bin` 实算 SHA-256 为 `9B24CC177E489CDCAA0A2B27E742ACEF5C45FFBD0C89EDB5AFE8D61C0121E459`。用 Capstone 重新读取 RVA `00026A20..00026ACA`、`00D2D663..00D2D6A5`、`00D2D9AF..00D2DA38`、`00D2DE68..00D2DF67`、`0123BF26..0123BF73`，确认折叠字节和长度门、成功条目与可选附加内容、失败字节读取及收尾路径与实现及记录一致。失败码 03 的语义仍仅为项目兼容选择。

本裁定批准代码与文档变更，不替代最终 fresh verification、交付步骤和证据归档；也不表示本仓已 ClientVerified。真实 TCP 测试覆盖未选角拒绝与连接可继续使用，成功学习的持久化和提交后应答由 SQLite/handler 测试证明；客户端成功展示、失败可见效果仍需按授权边界实测。

## 格式收口复核

同日对 `verification/final-formatting.json` 列出的变更做有界复核，保留 `approved`。五个 C# 文件的当前字节各追加一个 LF 即准确重建原审查清单中的 SHA-256；其余原清单内生产及测试文件哈希不变，因此没有语义或测试行为改动。控制 YAML 的 EOF 清理经追加四个 LF 重建格式化前哈希，当前阶段/状态为正常收口的 `verification/completed`。`git diff --cached --check` 通过。

同时独立读取 `verification/test-results.zip`，核对压缩包 SHA-256 及 completion-summary.json 所列八份最终 TRX 的哈希，逐条读取 UnitTestResult：七项目初跑 2595 Passed、2 NotExecuted；补跑两项均 Passed。按 testId 与 testName 合并后 2597 个唯一用例通过，无未补验跳过或失败。这个最终自动化结果仍不扩大客户端实机证据的边界。
