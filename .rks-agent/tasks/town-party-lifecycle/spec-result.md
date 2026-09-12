---
kind: rks-spec-result
outcome: partial
terminal_stage: review
---

# Spec Result：A21 城镇组队生命周期

## Delivered

- 同 listener、共享城镇区域中的当前会话可完成组队邀请、接受、稳定名册与主动离队；自邀、跨域、
  过期、重复和旧会话代际均由 Command/领域边界拒绝。
- `SET_UDP_IP_PORT` 严格解码真实 A21 前缀；队伍端点投影使用当前会话上报，缺失时采用结构安全回落，
  不再向客户端发送 `0.0.0.0:0`。
- 非空 `PARTY_INFO` 修正为 A21 实际解析的 65 字节形状；同一 party generation 的 revision 投影串行化，
  不允许迟到旧名册覆盖新状态。
- 形成序列在完整名册后向双方补发同 snapshot 的实时摘要，两端头像无需各自先查询即可立即显示 HP。
- PVF `[gate]` 区域按每角色私有 audience 处理，`[normal]` 区域恢复共享同屏投影。
- `GET_USERINFO mode=3` 只向同 listener、唯一且仍为当前代际的在线目标返回详情，原生详情窗口可用。
- 协议样本、专页、索引、总清单及 A21 UI 实机证据已同步；完整逐帧证据仍待补齐。

## Verification

- [`verification/completion-evidence.md`](verification/completion-evidence.md)：`decision: manual_required`。
- [`reviews/change-review.md`](reviews/change-review.md)：post-live 完整变更审查 `verdict: changes_required`，
  阻塞仅为缺失契约要求的 S2C 逐帧证据，未发现新代码缺陷。
- `.rks-agent/tasks/town-party-lifecycle/verification/premerge-20260912`：合并前新鲜 10 份 TRX，
  3050/3050 `UnitTestResult` Passed。
- [`a21-town-party-detail-20260912.md`](../../../protocol/patterns/data/traces/a21-town-party-detail-20260912.md)：
  当前 A21 双客户端 UI 与 Release Host 会话关联。

## Repository Result

- Baseline: `355aaf5cb6714044b4781185ec3118007bc97bec`。
- Result: `task/town-party-lifecycle` worktree 中的当前未提交变更集；功能/UI 实机通过但协议证据仍为
  `manual_required`。用户已明确授权先合并并清理 worktree，以当前结果作为后续队伍模式任务的基线。
- Parent rollback required: no。

## Successor Guidance

- 单人队伍提示的“保留”不发包，“移除”发送空 body `0x000D`；当前任务已验证普通成员离队，单人
  “移除队伍”的 UI 回摆与真实 S2C body 可在后续组队设置任务补测。
- A21 `PartyInfoBlock[9]` 是队伍类型：`0=剧情`、`1=循环`、`2=普通`；`[10..11]` 是随类型解释的
  目标标识。当前 DNF-JP-86-Server 尚未实现 C2S `0x000C` 的解码/处理，也未建立三种类型的领域规则；
  现有 `NOTI 0x0009` 只投影任务范围内的默认 12 字节设置块。
- 缺失端点回落的客户端消费、真实战斗 P2P/UDP 中继和全部 S2C body 归档均属后续任务。
