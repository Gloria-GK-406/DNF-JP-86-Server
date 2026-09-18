---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
target:
  branch: docs/sync-protocol-documentation
  baseline: 1677e4e28b289ed0a49d091cb5a2bde935fa15c3
---

# Spec Result: A21 协议资料同步

完整结果的提交前检查及提交后 Git 状态见
`./verification/completion-evidence.md`；单一产品结果提交为 `6b1147a6`。
下述实现记录保留作为交接摘要，不把代码覆盖统计当作 A21 协议正确性证明。

## Changed

- 新增服务端实现与协议资料库同步检查器，覆盖已注册的 77 个 C2S decoder identity、43 个 S2C CMD identity、82 个 S2C NOTI identity，以及 19 个动态或多载荷映射。
- 重建 `protocol/catalog.md` 的服务端覆盖区，并明确标记该区域只表示实现现状，不代表协议真相。
- 新建 A21 `0x0084 BACK_2_VILLAGE` 手写协议页，补齐其真实帧证据、逆向引用、当前实现映射和未决边界。
- 校正 `0x0003`、`0x002D`、`0x0048`、`0x0117`、`0x0450`、`0x00D0` 等页面的实现状态、验证等级和证据边界；未把未证实字段或分支升级为 ClientVerified。
- 校正当前 A21 census 数字及 A12 历史数字的作用域，保留历史里程碑但不再把它们描述成当前容量或注册数量。
- 把 15 条既有人工摘要迁入生成器 override 输入，并将两条已证实的 A21 机器区字段修正迁入 meanings 输入，避免后续重建覆盖人工修复。
- 为 A21 census/rebuild 外部输入增加环境变量参数和清晰报错，统一生成物换行，验证两份现存 A21 dump 生成相同 identity 结果。
- 新增 `Document/ProtocolDocumentationSyncAudit.md`，记录证据层级、漂移、已修项、已知残余、检查器能力边界和最低成本后续证据。
- 已按用户要求写入一条独立记忆更新说明：服务端代码只能作为覆盖探针，协议文档必须联合 A21 逆向、线上帧和真实客户端行为判别；机器生成区在证据闭环后应直接修正源输入或生成器。

## Why

- 现有资料同时混入 A12 历史 census、A21 静态逆向、服务端实现和局部实机验证，若只按代码同步会把实现假设误写成协议事实。
- 本轮把“是否已实现”“是否在线路上确认”“是否由真实客户端验证”拆开表达，并把生成区的可维护修正放回生成源，降低再次漂移的概率。

## Checks

- A21 全量重建通过；外部 dump 和枚举路径由环境变量冻结。
- 服务端/文档同步检查：C2S 77，S2C 125（CMD 43、NOTI 82），catalog identity 208，override 19，failures 0。
- 同步检查器自测通过；由受控源码走提取与对账，能检出缺失的 C2S CMD、S2C NOTI、
  未解析注册调用和缺失源码根目录，并正确区分 CMD/NOTI family。
- scaffold acceptance：inventory identity 1685，声明例外 5，协议页 1690，未声明额外项 0，failures 0。
- generation safety：human zone、手写页、shard meaning 和目录树保护全部通过，failures 0。
- controls：14/14 通过。
- `git diff --check` 通过。
- 未修改 `Server/` 或 `ClientHack/` 运行时代码。

## Remaining Evidence Boundaries

- 首轮完整审查指出源码注册提取失配时可能伪报通过；失败自测与调用形状哨兵已补齐，
  第四轮完整独立复审已批准。新鲜内容检查及结果提交后置状态已核对；
  合并与工作树清理仍须分别核对。
- A12 residue 报告仍有 a12-only 48、mixed 37、empty 1094；这些是后续逐协议逆向工作的候选，不属于本次“纠正当前覆盖状态和建立同步机制”的完成阻塞项。
- 19 个动态或多载荷 override 已验证注册符号与来源文件存在，但其每个运行时分支的精确 payload 语义仍需按真实帧逐项收敛；审计文档已显式记录此限制。
