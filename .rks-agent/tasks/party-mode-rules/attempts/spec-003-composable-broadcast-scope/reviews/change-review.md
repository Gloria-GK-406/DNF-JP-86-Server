---
kind: rks-review
verdict: changes_required
---

# Change Review：可组合广播作用域与附近角色频道隔离

## 审查目标与标准

- Target：分支 `task/party-mode-rules`，接受基线
  `73a36166dfd2e6f2da85c8754a91ba724c2eab8f` 到当前 staged、unstaged、untracked 的完整 Spec 003
  混合交付。
- Scope：`change`，同时应用 `reviewing-work` 的 Code 与 Deliverable 标准；治理来源为根 TaskContract
  revision 2、multi-spec map、子 TaskContract revision 1、Spec、direct Plan、implementation report 与
  `sdd-execution.yaml`。
- 核心判据：scope 合法组合与非法锚点、presence exact session/generation、Scoped/TheseCharacters
  audience 语义、六个附近角色查询点的同频道隔离、payload/audience 冻结一致、Party 精确 audience
  回归、Out 边界以及构建/测试声明。

## Verdict

`changes_required`。scope 值对象、查询过滤和普通同/跨 listener 用例本身正确，但冻结通知仍会在一个
真实的 session replacement 窗口中被 `TheseCharacters` 重定向到另一频道的新会话。这直接破坏
AC-003/AC-004 的 exact-generation 与跨频道不可见结果，必须修复后重新审查。

## Blocking Finding

### [P1] 冻结的角色 ID 会被重定向到另一频道的 replacement session

**缺陷。** 六个生产可见性入口虽然都先用 `VisibleTownArea(...)` 查询同 listener/town/area，但需要
后续通知的路径把查询结果降成 `CharacterId`，并使用 `NotificationAudience.TheseCharacters`：

- cleanup 在 `SelectCharacterLifecycleServices.cs:43-64` 冻结 `PeerAudience`，交付端在
  `SupplementalSynchronizationMessages.cs:261-266` 解析为 `TheseCharacters`；
- 选角双方发布在 `SelectCharacterLifecycleServices.cs:164-186` 冻结角色 ID，
  `SelectCharacterHandler.cs:276-288` 以 `TheseCharacters` 发布；
- 跨区 old/new 与区域内移动在 `TownProjectionSlice.cs:285-293`、`:335-352`、`:474-484` 使用相同形状。

`TheseCharacters` 的既有且必须保留的语义是“发送时该角色当前可达的会话”
（`INotificationServices.cs:48-56`）；resolver 在 `NotificationAudienceResolver.cs:58-65` 只按角色重新查询
当前 selected session，不检查当初命中 scope 的 `SessionPresence.Session`。这与新增 `Scoped` 分支在
`:39-55` 对 presence exact identity 和当前 selected identity 做相等校验形成实质差异。

**可发生时序。** 选角流程在 `SelectCharacterPhaseCommands.cs:91` 先把 replacement 写入 selected-session
目录，再于 `:97-98` 清理 displaced session。旧 presence 到
`SelectCharacterLifecycleServices.cs:111` 才执行 `WithdrawExact`，此前还可能在 `:90-101` 等待位置持久化。
因此存在确定性可构造窗口：角色 B 的旧 presence 仍属于 listener 10011，而当前 selected session 已是
listener 10012；10011 上角色 A 的 position/arrival/departure 查询会命中旧 presence 并冻结 B 的角色 ID，
随后 `TheseCharacters` 却把通知发给 10012 的 replacement。跨频道角色因此仍可能收到另一频道的城镇
通知，且 payload 所依据的旧 presence generation 与实际收件 session 不一致。

**为什么现有 GREEN 没有捕获。** `NotificationAudienceTests` 证明了 `Scoped` 会拒绝 mismatch，但当前
生产代码没有一个 `NotificationAudience.Scoped` 调用。六点回归只构造“presence 与 selected session
一致”的同/跨 listener 数据，未覆盖“旧 presence 尚在、selected 已替换到另一 listener”后再执行实际
发布/解析。

**最小充分修正。** 对从一次 presence snapshot 构造 payload 与 audience 的路径保留 exact
`SelectedSessionIdentity`，并让通知边界仅在该 identity 仍为当前会话时投递；不得改变
`TheseCharacters` 的顺序、重复或 replacement 语义，也不得把 Party 的既有精确角色 audience 改成动态
scope。可以增加独立的 exact-session frozen audience，或采用等效且不重新查询出不同群体的设计。
至少补一条确定性回归：scope 查询命中 listener 10011 的旧 generation，发布前同角色被选择到 listener
10012，断言 replacement 不收到 arrival/departure/position，并断言 body 与实际 exact audience 一致。

## 已通过的检查

- `SessionPresenceScope` 对未知 flags、缺失/多余 listener/town/area、无 town 的 area 和非法端口均
  确定性拒绝；Global、独立维度、交集、排除、稳定排序和 replacement presence exact identity 的实现与
  用例一致。
- `Server/src` 旧 `.InArea(` 为 0；`VisibleTownArea(...)` 恰有六个生产调用点，均包含实际 listener、
  town、area、standing/shared 与 self exclusion。
- `NotificationAudience.TheseCharacters` 分支代码未改，顺序、重复、离线忽略和当前会话解析回归仍通过；
  `Scoped` resolver 对 presence/selected exact identity mismatch 的拒绝逻辑正确。
- baseline 到当前的 Party 生产与测试目录 diff 为 0；公开队伍目录、Story/Cycle、P2P、战斗和奖励均未
  越界实现，也没有协议 body 变更。
- 独立 Release `--no-incremental` solution build：0 warning / 0 error；独立 Application 聚焦 74/74、
  Protocol 聚焦 86/86；`git diff --check` 通过。
- 独立解析九份留存 TRX：3106 Passed、2 NotExecuted、0 Failed；两项是提供真实 PVF 时按设计跳过的
  缺失档案反例，与 implementation report 的 `3106 Passed / 2 Skipped` 声明一致。

## Disposition

停止进入 completion verification。由 controller 修复上述 exact-session 冻结缺口并加入替换会话的
跨 listener 行为回归后，提交当前完整 target 进行独立复审；未发现需要扩大到公开队伍目录或改变 Party
语义的理由。

## Round 2

```yaml
kind: rks-review
verdict: approved
```

### 复审范围与结论

- Target 仍为接受基线 `73a36166dfd2e6f2da85c8754a91ba724c2eab8f` 到当前 staged、unstaged、
  untracked 的完整 Spec 003 混合交付；复读了修订后的 Spec、Plan、implementation report Round 2、
  根治理文件、完整生产/测试差异以及首轮 P1。
- 首轮 finding 已关闭。`NotificationAudience.TheseSessions` 与 recipient `ExpectedSession` 在 resolver、
  每个接收者发送前复检及 retry queue/drain 全程保留完整 `SelectedSessionIdentity`；replacement 不会收到
  旧 generation 的冻结通知。普通 `TheseCharacters` 仍按当前角色会话解析，原有 replacement/retry 语义、
  顺序与重复行为未被改变。
- 六个 `VisibleTownArea(...)` 生产查询点均在形成结果时验证
  `presence.Session == current selected session`。相邻角色 body 直接排除 stale generation；选角发布、
  cleanup、跨区 old/new audience 与区域内 position audience 均冻结 exact sessions。需要同时形成 body 与
  audience 的进入/跨区路径在同一筛选循环中追加 participant state 与对应 exact session，未发现旧 body
  被投递给 replacement 或 payload/audience 分叉的新窗口。
- `ISessionPresenceDomainService` 在 Application 组合根中先于 notification dispatching 注册；完整容器会为
  resolver 注入 presence，而保留的可选依赖仍支持仅使用 character/exact audience 的最小测试宿主。
- 接受基线到当前的 Party 生产与 Party 测试目录 diff 为 0；公开队伍目录、Story/Cycle、P2P、战斗和奖励
  均未越界实现，协议 body 也没有进入本 Spec 的改动范围。

### Round 2 独立证据

- 独立聚焦测试：Application 101/101、Protocol 13/13；覆盖 exact audience replacement、同 connection
  不同 generation 的逐接收者 `Gone`、exact retry 不追随 replacement、普通 retry 保持追随，以及
  stale presence 在相邻名单、选角发布、cleanup、跨区与移动广播中的排除。
- 设置 `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol\Script.pvf` 后独立完整运行：
  Application 1406/1406、Protocol 801/801，均 0 failed / 0 skipped。
- 独立 Debug solution `--no-incremental` build：0 warning / 0 error；`git diff --check` 通过。

### Disposition

当前 Round 2 target 在本次完整 change review 范围内没有未解决的 blocking defect，可以交还 controller
进入独立 completion verification。此 `approved` 仅批准当前 Spec 003 target，不覆盖首轮历史裁决，也不
替代 completion verification 或后继 Spec 的公开队伍目录验收。
