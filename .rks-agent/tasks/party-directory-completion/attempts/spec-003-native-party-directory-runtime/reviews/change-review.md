---
kind: rks-review
verdict: changes_required
scope: complete-result
reviewed:
  baseline: 365baed09ee5a19c08550a4f5dbdaecdafca7c8f
  head: 365baed09ee5a19c08550a4f5dbdaecdafca7c8f
  worktree: task/party-directory-completion
  target: mixed code+deliverable diff, including untracked result and protocol fixtures
contract:
  path: ../TaskContract.md
  revision: 1
spec: ../spec.md
plan: ../plan.md
---

# Change Review: A21 原生 party directory runtime

## 结论

`changes_required`。双客户端 exact-session 的 wire/Host 时序、用户原生 ID `0x11` 操作报告、删除后
CNUser/CParty 最终状态、raw evidence hashes 和六个 protocol sample 均能相互对齐；final-vptr 未命中也不
构成独立缺陷，因为 Spec 明确允许等价客户端容器。但当前等价对象快照只覆盖删除后的最终状态，没有在目标
`partyId=1` 可见或更新时记录它的 CNUser/CParty association/目录状态，未达到 AC-002/AC-003 和本 Spec
自定的 `ClientVerified` 最低证据组合。此外，`0x000C` 的状态摘要把未观察到的 inline-title 更新也写成了
`ClientVerified`，超过实际 raw frame 覆盖。

## Blocking Findings

### F-001 [P1] 目标 party 的 add/update 没有客户端对象或等价目录状态证据

`spec.md:33-34` 要求 add 时 `this+0x188`/等价容器包含该 partyId，update 时客户端容器与可见行共同改变；
`spec.md:59-60` 又把“客户端对象/目录状态”列为 `ClientVerified` 的最低组合之一。当前 final-vptr probe 的
`directory-state-enhanced.jsonl` 只记录两个代码区候选且 `instances=[]`，这个失败被如实保留，本身没有问题。
问题在于替代证据也没有覆盖目标对象：

- raw frame event 17/18 证明 observer 在 `10:22:46.617Z` 收到 leader user `2` 的 `0x0002`，7 ms 后收到
  `partyId=1` 的 `0x0009`；event 23/25 证明 direct mutation 与 observer 61-byte refresh；event 27/28
  证明 `partyId=1` clear。
- 两份入库 typed snapshot 均采于 clear 之后（`11:04:51Z`、`11:11:22Z`）。它们只含 observer 自己的
  `CNUser 1 -> partyId=2`，`CNUser 2` 的 party pointer 已为 null，并且不存在 `partyId=1` CParty
  (`client-party-association-after-delete.json:10-34`、`client-party-association-ui-screenshot.json:10-34`)。
- 截图摘要和用户报告支持原生 UI 的 A/B、A 更新及删除，但最终截图只显示删除后的 B；它不能替代 Spec
  另外要求的客户端对象/目录状态层。

因此现有结果能证明服务器投影和用户报告的 UI 行为高度可信，也能证明 delete 后没有目标 CParty；却无法从
客户端状态把 add/update 的目标 `partyId=1` 与同一原生行独立归因。把“删除后的 B 状态”当成“A 的
add/update 等价容器”会放宽已经写入治理 Spec 的验收边界。

最小修复：在同一 hash/同一 exact-session 场景中，于远端 A 行存在时、mutation 后、clear 后各取一次只读
客户端状态。若仍无法定位 final vptr，可继续用 CNUser/CParty 作为等价对象，但 add 时必须记录
`user 2 -> partyId=1`，update 时需回读能体现该 mutation 的目录/CParty 字段（或找到实际 ID `0x11`
容器），clear 后再记录 association/object 消失；将三时点与同一 frame timeline 和用户 UI 时点对齐后再升级
`NOTI 0x0009` 的 ID `0x11` add/update/delete 为 `ClientVerified`。若无法取得该层证据，应保留 runtime
行为记录但降低状态，不能宣告 AC-002/AC-003 已通过。

### F-002 [P2] `0x000C` 把未实测的 inline-title 更新写成 ClientVerified

外部 raw frame log 中两条 `16+n` 样本只有 event 8/20：character `2`、character `1` 分别从无队状态创建
标题 A/B。实际更新是 event 23 的 12-byte direct body `010104000000000500020000`；没有一条
`16+n` inline-title update。可是以下三处都写成了“12B direct 与 `16+n` inline-title 创建/更新”已
`ClientVerified`：

- `protocol/game/client-to-server/0x000C.md:65`
- `protocol/catalog.md:49`
- `protocol/game/index.md:36`

这违反 AC-006 的“协议页/catalog 只升级已观察范围”，也会让后续读者误以为 inline-title 的在队更新路径已被
当前 A21 验证。最小修复是把状态精确写成“12B direct 创建/更新；`16+n` inline-title 创建”，并保留拒绝及
inline-title update 待实帧；或者补做并归档真实 inline-title update 后再保留现有措辞。

## 已确认成立的部分

- Manifest 所列 `runtime-manifest.json`、frame log、Host log、enhanced state、两个 vptr window/diff 和
  `stop-result.json` 的字节数与 SHA-256 均与外部 runtime 文件一致；DNF.exe、Host、PVF 及主数据库当前
  hash 也与记录一致，主数据库 before/after 保持 `E06D...F5BA7`。
- 六个新增 `.hex` fixture 去除空白后均逐字节匹配 raw JSONL body：两个 `0x000C` 创建、一个
  `0x0006` leave、以及 `0x0009` add/update/clear；类型、方向和长度一致。
- `0x0002 -> 0x0009` 的 7 ms 顺序、两个建队接受、settings mutation、leave/clear 与 Host 接受日志一致；
  `NOTI 0x0006` 谨慎保持 `WireCaptured`，没有误升其独立 UI 移除行为。
- final-vptr miss 被明确披露，没有伪造成 `this+0x188` 成功；CNUser/CParty reader 使用
  `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ`，没有申请写入或注入权限。
- `stop_directory_runtime.ps1` 只处理 receipt 中的 PID，并同时校验 executable path 与 UTC start time；
  `Convert-ReceiptUtcDateTime` 对 `ConvertFrom-Json` 得到的 typed `DateTime` 直接 `ToUniversalTime()`，修复了
  丢失 `Z` 后再次 Parse 的 8 小时漂移，同时保持 identity mismatch 时不停止进程的 fail-closed 行为。
- 独立运行 Python helper suite 为 14/14 passed；PowerShell UTC tick regression passed；`git diff --check`
  passed。
- 相对 accepted baseline 没有 `Server/src` 或其他生产代码变更，也没有新增目录 wire/store `0x023C`；
  新增的唯一 `0x023C` 测试引用是 proxy allowlist 的拒绝样例，不构成目录路径。

## Re-review 条件

复审只需检查两项：F-001 的三时点客户端等价状态是否与同一 exact-session/UI 闭环，及 F-002 的状态措辞是否
收窄到已观察形状。helper、sample/raw 对账、stop 修复与零 `0x023C` 结论无需在输入身份未漂移时重新展开。

## Round 2 独立复审

```yaml
kind: rks-review
verdict: approved
```

本轮只复审首轮 F-001/F-002 的修复及其直接回归面。最新 verdict 为 `approved`，取代首轮对这两项的阻断
结论；不代替随后 fresh completion verification。

### F-001 — resolved

补证 runtime `C:\Users\Gloria\AppData\Local\Temp\dnf-party-directory-reviewfix-20260913-1930` 的
`directory-targeted-frames.jsonl` SHA-256 为
`4B641862D68444CCE475DCD035C4060C3534B7017DB1BDE117B0BEE5B0ACDAF6`，与 manifest/trace 一致。
raw event 29–37 精确形成第二次目标生命周期：

- event 29/31：leader 以 17B inline-title 创建 A，observer 在 `11:58:35.284Z` 收到 66B
  `partyId=3` type-0；
- event 32/34：12B direct `titleIndex=1` mutation 后，observer 收到同 `partyId=3` 的 61B refresh；
- event 35/37：leader `0x000D` 后，observer 收到 5B `partyId=3` clear。

三份仓库 snapshot 的 bytes/SHA-256 与 manifest 完全一致，并严格落在对应帧之后、下一次 mutation 之前。
它们来自同一 exact observer PID `62412`、同一 executable/module identity：add 时
`CNUser 1 -> CParty 0x563BB820 -> partyId 3, titleIndex=0`；update 时 CNUser、CParty 地址和 partyId 不变，
`titleIndex=1`；delete 时 CNUser 1 的 party pointer 归零且 partyId 3 对象消失，而控制对象 B/partyId 2
仍保留。用户在该补证运行中确认 A/B 同时可见、A 同一行切换为预设标题、clear 后 A 消失且只剩 B。

`titleIndex` 读回也有独立代码依据：固定 B1 SHA-256 `DC322D8...3306E9` 在
`0x01172206` 以 CParty 为 `ecx` 调用 `0x01A04FF0`；后者返回 `ecx+4`，随后
`0x0117220F mov [eax+4],edi`，即写入 CParty `+0x08`。当前 probe 从该 offset 读取 u32、约束为 wire u8
范围并在 typed JSON 中输出，synthetic-memory test 覆盖该字段。独立重跑 helper suite 为 14/14 passed。

因此 final-vptr 仍未命中不再构成证据缺口：规格允许的 CNUser/CParty 等价客户端状态已在 add/update/delete
三个时点与同一 raw timeline、同一原生 UI 对象闭环，AC-002/AC-003 的最低组合满足。

### F-002 — resolved

三处状态现均与 raw 覆盖精确一致：

- `protocol/game/client-to-server/0x000C.md:65,136-147`：12B direct 创建/更新为 ClientVerified；
  `16+n` inline-title 只把创建路径升级，更新继续为 static High/待实帧；
- `protocol/catalog.md:49`：同样区分 direct 创建/更新与 inline-title 创建；
- `protocol/game/index.md:36`：同样明确 inline-title 更新待实帧。

本轮没有把 event 29 的 inline-title create 或 event 32 的 direct update 外推为 inline-title update，满足
AC-006 的“协议页/catalog 只升级已观察范围”。

### 复审检查与最终边界

- remediation manifest 所列 runtime manifest、frame log、Host log、directory-state 和 stop-result 的
  bytes/SHA-256 均与外部 raw 文件一致；主数据库当前 hash 仍为 `E06D...F5BA7`。
- 机器化断言确认 event 29–37 的 direction/type/body length/body hex、三份 snapshot 身份与三时点状态全部
  精确匹配；`git diff --check` passed。
- PowerShell UTC receipt regression 继续 passed；F-001/F-002 的修复没有触碰生产代码或新增 `0x023C`
  目录路径。
- 本轮未发现 F-001/F-002 修复引入的直接回归或新的阻断 finding。原 final-vptr 生命周期问题可留作独立
  逆向研究，不影响当前 Spec 按其明示等价容器边界通过。

#### Format-only hash confirmation

提交前将三份 reviewfix2 JSON 从 CRLF 机械规范化为 LF 后，独立复核确认它们仍可解析，PID、时间、CNUser、
CParty、partyId、对象地址、titleIndex 和 add/update/delete 语义均未改变，且不再包含 CR byte。当前身份为：

- add：1030 bytes，SHA-256 `A85564BB1D623D3A00006771C116768F19383804B63DD4EB21DCFC79C11ADF92`；
- update：1030 bytes，SHA-256 `78259BAEDB0B958B1FE0D5D1F45ABD3AB0D6E680F824B75BFBA0FE99016DFAAF`；
- delete：841 bytes，SHA-256 `324553C49641177320E1FBBBE3DAA68390824B5703B8673E66B5A00532866702`。

`runtime-evidence-manifest.md` 已逐项同步上述 bytes/hash，`git diff --cached --check` passed。该变更只有文本
行尾与派生身份变化，不影响 Round 2 的 F-001/F-002 disposition；最新 verdict 继续为 `approved`。
