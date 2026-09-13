---
kind: rks-verification
decision: pass
contract:
  path: ../TaskContract.md
  revision: 1
review: ../reviews/change-review.md
verified_head: 365baed09ee5a19c08550a4f5dbdaecdafca7c8f
verified_at: 2026-09-13
---

# Completion Evidence

## Exact claim

Spec-003 在接受基线之上的当前 mixed result 中，以两个固定 A21 exact sessions 证明同一
`VisibleTownArea` 的非成员队伍在原生 ID `0x11` 主目录完成 add/update/delete。结论同时由 wire/Host、
CNUser/CParty 三时点只读对象、用户原生 UI 和协议样本支持；没有新增目录意义 `0x023C` 或修改生产代码。

## Review gate

- `reviews/change-review.md` 最新 Round 2 verdict 为 `approved`。
- 首轮 F-001 已由 `partyId=3` 的 add/update/delete 三时点对象证据闭合；F-002 已把 `0x000C`
  `16+n` 状态收窄为 inline-title 创建，未观察的 inline-title update 仍为 static High。

## Fresh checks after review

- Release build：`dotnet build Server/86JP.Server.slnx -c Release --no-restore`，exit `0`，
  `0 warning / 0 error`。
- Helper regressions：Python `14/14`；PowerShell receipt UTC tick test passed；`git diff --check` passed。
- Runtime/object assertion：supplemental raw event `29/31` add、`32/34` update、`35/37` clear 全部命中
  `partyId=3`；三份 snapshot 证明同一 CParty `0x563BB820` 的 `titleIndex 0 -> 1`，clear 后 CNUser
  association 与对象消失，B/partyId=2 保留。
- Sample assertion：六份新增 `.hex` 去除空白后与首轮 raw JSONL 的 event `8/20/29/18/25/27` body
  逐字节相同，长度分别为 `17/17/2/66/61/5`。
- Runtime identity：补证 manifest/frame/Host/stop hashes 与外部文件一致；两个 exact DNF 已正常退出；
  receipt PID/path/UTC 身份均不再存在，七个任务端口无 listener；历史 proxy PID 后来被 Codex 的
  `node.exe` 复用，按 exact identity 判定不是任务残留，未停止该无关进程。主数据库 hash 保持
  `E06D...F5BA7`。
- Production boundary：相对 `365baed0` 的 `Server/src` diff 为空；未新增目录 wire/store `0x023C`。
  `0x000C` page/catalog/index 均不存在“inline-title 创建/更新”之类越界措辞。
- 全量串行 Release 测试：9 份 fresh TRX 的每个 `UnitTestResult` 均已解析，
  `3127 Passed + 2 NotExecuted + 0 Failed + 0 Other = 3129`；逐项目统计见
  `completion-test-summary.md`。
- Protocol controls：布局正向/反向 controls `14/14`，A12 residue scan 正常完成。仓库级
  `check-acceptance.py` 与 `check-regeneration-safety.py` 仍因既有未登记文档
  `game/client-to-server/0x0075.md`、`game/server-to-client/CMD-0x0040.md` 返回 `1`；接受基线
  `365baed0` 以相同参数得到完全相同的两项失败及统计，证明本 Spec 未引入该目录债务。

## Success-condition mapping

| SC | Decision | Decisive evidence |
|---|---|---|
| SC-1 | PASS | 两次 runtime manifest 固定 DNF/Host/PVF/DB 和 listener；两个客户端、proxy、probe、Host 的起止与 exact identity 可复核。 |
| SC-2 | PASS | 首轮 observer `0x0002 -> 7 ms -> 0x0009`；补证 add snapshot 将远端 CNUser、`partyId=3`、CParty 地址和原生 A 行绑定。 |
| SC-3 | PASS | 同一 CParty 的 `titleIndex 0 -> 1` 与 observer refresh、用户同一行更新一致；clear 后 association/object 和原生 A 行删除，B 控制行保留。 |
| SC-4 | PASS | 接受基线的 scope/replacement/full-party 自动化由 3129 条全量回归保持；本轮 exact audience 未泄漏，未把未运行的附加 UI 场景升级为 ClientVerified。 |
| SC-5 | PASS | Round 2 approved；fresh build/helper/full-suite、raw/sample/hash、协议状态、零生产 diff/`0x023C` 与安全清理均验证完成。 |
