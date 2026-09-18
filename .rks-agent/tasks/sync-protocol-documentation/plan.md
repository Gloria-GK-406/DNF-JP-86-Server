---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan: 证据化同步 A21 协议资料库

## Planning Choice

采用 `direct`。成果只有一个接受边界：资料库对当前接入协议的覆盖、证据边界与可重复检查同时恢复一致。各部分共享同一份 inventory 与阶段判定，拆成独立 Task Unit 反而会重复维护判断口径；单一详细 Plan 能保持代码清点、协议页、catalog、流程页和验证同步推进。

## Approach and Shared Decisions

- 先建立机械 inventory，再解释差集。inventory 只从当前源码提取四元身份、decoder/encoder 类型和调用线索，不赋予协议正确性或成熟阶段。
- catalog 是“正在做/已做”的进度入口，不扩写成第二份全量静态索引。当前服务端已接入的 identity 必须有行；确需排除时使用可审计的显式排除项，而不是让检查器静默忽略。
- C→S 页以注册 decoder 对应的类型号核对；S→C 同号的 CMD 与 NOTI 分别核对，禁止只按 type 合并。
- 证据裁决顺序服从 `protocol/evidence.md`。静态客户端结果、抓包/发送日志、运行时读取跟踪、当前服务端实现和上游参考分别标示，不用一个来源代替另一个来源。
- 对机器区，先判断是否属于生成器可见身份。如果是生成数据错误，改输入或生成逻辑；如果像 `0x0084` 一样属于静态普查不可见但已有闭合证据的例外，使用生成器明确支持的手写页机制，并让索引/检查识别它。
- 不修改运行代码。发现 server wire 编号、布局、响应或测试与 A21 证据不一致时，写入审计风险与下一证据，不顺手修复。

## Work and Dependencies

### T-001 固化 inventory 与同步检查（RQ-001、RQ-002、RQ-007）

1. 从 `ProtocolModule.RegisterInboundDecoders` 提取已注册 Game C→S decoder，并关联 `GameProtocol` 常量和 decoder 的 message 类型。
2. 从直接 encoder、状态同步 encoder 集合及补充发送路径提取 S→C `(command,type)`；对无法静态可靠发现的动态集合，以靠近源码的显式清单作为输入并记录理由，而不是隐藏在检查代码中。
3. 解析 catalog 的 endpoint/direction/command/type 和 Markdown 链接，检查未登记 identity、缺失协议页、无效链接及 CMD/NOTI 同号混淆。
4. 为检查器增加自测试或受控 fixture，证明至少一项缺失会导致非零退出。

依赖：当前源码和 catalog。输出将作为后续文档修改的客观差集基线。

### T-002 建立证据矩阵并裁决目标条目（RQ-003、RQ-004、RQ-005）

1. 以 T-001 差集为行，收集 A21 静态身份/构包或 handler 入口、现有 trace/capture、真实客户端记录、当前实现与测试、上游参考。
2. 对每行分别判定“身份/布局/流程/实现/实机结果”，不使用单一总状态掩盖不同层面的证据强弱。
3. 优先闭合已知矛盾：`0x0003`、`0x002D`、`0x0048`、`0x0117`；随后处理其他精确搜索发现的“未实现/未知”与源码接入冲突。
4. 无法闭合的语义保持 `unknown` 或较低阶段，并记录最便宜的下一证据。

依赖：T-001 inventory。此步骤只产出判定依据，不先批量套状态。

### T-003 修复协议页、机器生成资料和流程记录（RQ-002–RQ-006）

1. 为 `CMD 0x0084` 创建完整手写 A21 记录，引用副本准入 trace、当前 decoder/response 路由与静态普查盲区。
2. 按 T-002 裁决更新目标协议页的人工区；需要改变机器结论时，修改 `patterns/data` 输入或生成逻辑并重新生成，确保人工区不丢失。
3. 更新受影响流程页，只记录触发、响应顺序和客户端结果；协议字段事实留在协议页。
4. 精确搜索并清除已被证据推翻的旧结论，不保留真假两套说法。

依赖：T-002 的逐项证据矩阵。

### T-004 重建 catalog 与维护说明（RQ-001、RQ-005、RQ-008）

1. 为 inventory 中尚未登记的 C→S/S→C identity 增加可链接行，按证据分别填写角色、阶段和备注。
2. 修复现有重复、矛盾、陈旧总计和过时说明；保持 catalog 与全量静态 index 的职责边界。
3. 在 `Document/ProtocolDocumentationSyncAudit.md` 记录提取口径、最终覆盖统计、阶段判定、自动检查盲区、代码疑似风险和下一步逆向/实机验证建议。
4. 在 README/方法页中补充“当前实现只能证明实现现状”和同步检查入口，防止未来回归。

依赖：T-001 最终 inventory、T-002 判定、T-003 页面路径。

### T-005 写入长期记忆原则（RQ-010）

使用 `apply_patch` 在 `C:/Users/Gloria/.codex/memories/extensions/ad_hoc/notes/` 新增唯一一条时间戳 Markdown 备注，记录三方判别、代码非真相、未知不猜和机器区可在完全探明后修正的长期规则；不直接修改 MEMORY.md 或既有记忆。

### T-006 集成验证、审查与本地提交（RQ-006、RQ-007、RQ-009）

1. 运行新同步检查及其失败自测试、现有 protocol acceptance/A12/control/regeneration 检查和 Markdown 链接检查。
2. 执行重新生成比较，确认生成器稳定且人工区未丢失。
3. 检查目标矛盾关键词、所有改动路径和 diff，确认没有运行代码或无关改动。
4. 对完整成果按 Spec/TaskContract 做结果审查；阻断项修正后重跑相关检查。
5. 写入 completion evidence，并在验收成立后创建一个本地结果提交；不 merge、push 或清理工作树。

## Verification

| Check | Expected result | Covers |
|---|---|---|
| 新同步检查正常运行 | C→S、S→C 未登记差集和缺页差集均为零，退出码 0 | AC-001, AC-002 |
| 新同步检查自测试/fixture | 注入缺失登记或缺页时退出码非 0，并指出精确 identity | AC-007 |
| `check-acceptance.py`、`check-a12-residue.py`、`check-controls.py`、`check-regeneration-safety.py` | 全部退出码 0 | AC-006, AC-011 |
| 重新生成前后 `git diff` 比较 | 除有意生成输入/产物外无新增漂移，人工区保持 | AC-006 |
| catalog Markdown 链接检查 | 所有本地链接存在，S→C CMD/NOTI 路径正确 | AC-002, AC-005 |
| `rg` 精确矛盾审计 | 目标页不再同时出现相反实现结论；未知项表述不越级 | AC-004, AC-005 |
| 路径范围检查 | 无 Server/ClientHack/客户端运行文件改动 | AC-009 |
| 记忆备注检查 | 只有本任务新增的一份合规备注，内容覆盖长期原则 | AC-010 |
| 完整成果审查与新鲜验证 | 无阻断项，所有 SC/AC 有证据，工作树提交后干净 | AC-011 |
