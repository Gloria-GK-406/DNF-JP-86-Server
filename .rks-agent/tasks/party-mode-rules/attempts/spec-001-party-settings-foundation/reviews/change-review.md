---
kind: rks-review
verdict: approved
---

# Change Review：队伍设置模型与 `0x000C` 基础闭环

## 审查目标与范围

- Scope：`change`，分支 `task/party-mode-rules`，基线
  `d717f1b0538793c86f347fce784acaaa2eab3c49`，审查对象为当前未提交的 staged、unstaged 与
  untracked 完整交付集合。
- 约束来源：子 TaskContract revision 2、Spec revision 2、Plan、四个 Task Unit 的结果记录、
  `spec-result.md`、协议专页/index/catalog、静态与实机 trace、原始定向帧、测试/TRX 证据，以及完整
  生产与测试 diff。
- 本轮按代码和原始证据重新建立结论，没有把 executor 的结果摘要直接当作事实。

## Verdict

`approved`。当前没有仍需修复的 P1/P2、验收缺口或证据越界；AC-001..AC-010 均有与实际实现相符的
证据。交付可以进入审查后的新鲜 completion verification。

## 审查中发现并已闭合的问题

第一轮发现 `PartyNotificationDelivery` 只用 generation 级 `HasFullState` 和 revision 高水位：当设置
更新越过接受或离队计划时，新成员可能收不到完整状态，迟到计划中的 clear/acceptance 也会被一起
吞掉。第二轮又证明“离队后同一精确会话已重新入队”与“接受后成员又离队”的对称反例，若迟到
side effect 无条件重放，会用旧 clear/acceptance 反向污染最新客户端状态。

最终实现以同 generation 的 `CurrentMembers` 保存最高 revision 精确成员视图，并以
`FullStateMembers` 记录完整投影覆盖面：

- 设置更新遇到尚未收到完整状态的当前成员时提升为最新 snapshot 的完整投影；否则只发 type-1。
- 迟到 snapshot 永不重放；迟到 clear 只发给不在最新成员视图中的精确会话，迟到 acceptance 只在双方
  仍属于最新成员视图时补发。
- 四个确定性乱序用例分别覆盖 settings 越过 accept/leave、rejoin 越过旧 clear、leave 越过旧
  acceptance。当前状态更新发生在同一 generation stream gate 内，判断与成员视图更新之间不存在
  另一 delivery 插入点。

该修复保持通知在领域锁外发送，也没有放宽 decoder、领域规则或 audience 的精确会话校验。

## 验收核对

- **AC-001/002**：`PartySettings` 是防御性复制的 12 字节值对象；规则服务集中拥有默认 Normal、容量
  1..4、类别、准入和 `[1] -> 0` 归一化。领域回归固定了仅 `[1]` 改变、其余 11 字节不变；生产搜索
  未发现旁路写入或 Application 对 Infrastructure 的引用。
- **AC-003/008**：`0x000C` 只接受 exact 12B direct 或 `16+n` inline-title，长度、UTF-8、容量、类别和
  尾随数据均有反例；type-1 的 `info0==0/!=0` 两个编码分支有 byte-exact 向量，18B 实帧与实现一致，
  未把 22B 分支冒认为实机验证。
- **AC-004/005/006**：无队创建、在队原身份更新、队长权限、旧会话、容量/规则拒绝、邀请不被失败
  消耗、并发 revision 单调性均有领域/handler 测试；失败路径不产生状态或通知副作用。
- **AC-007**：创建投影完整 realtime/endpoints/roster，普通更新只向当前精确成员投影一份同 snapshot
  type-1；generation/revision、重连、离队与上述四种乱序均有明确覆盖。
- **AC-009**：当前工作树独立执行 Release solution build，结果 0 warning / 0 error；独立执行完整
  Protocol 为 797/797、`FullyQualifiedName~Party` 为 72/72；staged 与 unstaged `git diff --check`
  均通过。保留 TRX 的有效组合与当前新增四个 Protocol 用例相加为 3082 Passed / 0 Failed，另有 2 个
  PVF 条件 skip，移除 PVF 后对应 2/2 Passed。摘要中的算术和例外说明一致。
- **AC-010**：`0x000C`、`NOTI 0x0009`、catalog/index、样本与 trace 对 direct/type-1 的证据等级一致；
  index 已列 type-1 18/22，静态 trace 已区分服务端默认 target `0xFFFF` 与客户端自动路径 target `0`。
  历史 `[1]=1` 实帧和现行归零策略没有混写。

## 边界与残余风险

无队 direct create、inline-title、非队长设置 UI、`info0==0` 的 22B type-1、`0x000C` CMD response、
三种模式玩法语义及公开队伍目录仍未由当前 A21 实机闭合。实现和文档均把这些项目明确留给后继规格，
没有作为本 Spec 的完成事实。

通知总线仍按既有 best-effort/retry 语义工作；本 Spec 新增的 stream 状态不把网络逐接收者成功率提升为
强一致承诺。此项不改变本次已验收的领域原子性、精确 audience 或乱序抑制结论。

审查前遗留的两个 Python `__pycache__` 生成目录已精确删除；其余 untracked 项均为本次预期的源码、
协议、样本、trace 或 SDD 证据工件。

## Disposition

批准当前结果。后续只需按工作流执行审查后的新鲜 completion verification、更新状态并建立本地接受
提交；无需为本审查再改生产代码或协议结论。
