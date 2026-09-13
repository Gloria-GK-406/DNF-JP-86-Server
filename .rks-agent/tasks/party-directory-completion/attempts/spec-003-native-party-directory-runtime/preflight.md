---
kind: rks-preflight
decision: pass
checked_at: 2026-09-13
accepted_baseline: 365baed09ee5a19c08550a4f5dbdaecdafca7c8f
---

# Runtime preflight

## Repository and idle-state boundary

- Canonical worktree：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/party-directory-completion`；branch
  `task/party-directory-completion`；HEAD 与 accepted baseline 均为 `365baed09ee5a19c08550a4f5dbdaecdafca7c8f`。
- 当前未提交内容只有父控制器和 spec-003 过程工件；没有接受提交后的产品代码漂移。
- `DNF`、`Dfo.Launcher`、`Dfo.Server.Host` 进程均为 0。
- `7001/10010/10011/17001/18080/20010/20011` 均无 TCP listener；没有需要终止或接管的既有 owner。

## Fixed client identity

两份实际运行目录为 `DNFClient-A21-protocol` 与 `DNFClient-A21-protocol2`。下列文件在两目录逐项 hash
一致；两份 `DNF.exe` 是不同 File ID 的独立文件，但字节身份相同，因此可并行且不会共享被修改的二进制。

| File | SHA-256 |
|---|---|
| `DNF.exe` | `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD` |
| `DNF.unpacked-b1-debug.exe` | `CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14` |
| `dstr.dat` | `60E9857773831B341BAE0D490FCAEAF0AF184B9776717E2FCF293E1BC7F92F10` |
| `Script.pvf` | `19F5FA85FF73CCE94E299E0902269DE2F0164934128F15B8A7B99249C881BE1A` |
| `az.dll` | `4EE35A9257913C936A885F68837D4BDA844F198F4DF931804D928D312E48AA5E` |
| `ijl15.dll` | `41D37BFB0A39017497E0AA373DC01F71AC9C5046C75F7CD269BEE52112A5F3A1` |
| `ClientHack.ini` | `40B97549F44ABEAE5B960C29C148FA0FB342A3B3A5A9097847B12796F7145579` |
| `Launcher.ini` | `BC00C0C154C074D33AC0CE3020FAD9312B7813A183E74F4F18075CE3A02F547A` |
| `Dfo.Launcher.exe` | `1440FE14057C610759C8A0326C25D2348B1C50115B5C6F7FE7B8F1333F2A6EC6` |
| `Dfo.Launcher.dll` | `B60DDF3B57EAD2D3CEFBEB2BC5B49889E12883540CCB47C594E78EB1F5F12DFB` |

当前 static authority 使用 `DNFClient-A21-debug/DNF.unpacked-b1-debug.exe`，SHA-256
`DC322D8FB2A8A33FCE31065CEEAC5801ED854F34409190FE257AACF2663306E9`。它与两份 protocol B1 在本任务
需要的四个 anchor 上逐字节一致：

| Anchor | RVA | Bytes / fact |
|---|---:|---|
| Image | — | PE32 x86, ImageBase `0x00400000`, SizeOfImage `0x07475000` |
| ID `0x11` final vtable | `0x02DFFFD4` | 首项 `50 19 F7 00` → `0x00F71950` |
| `flushPartyList` | `0x00B73A10` | `55 8B EC 6A FF 68 10 6C D5 02 64 A1 00 00 00 00` |
| row renderer | `0x00B71950` | `55 8B EC 6A FF 68 BC 6A D5 02 64 A1 00 00 00 00` |
| vector length/read | `0x00B71A69` | begin=`this+0x18C`, end=`this+0x190`, `(end-begin)/4` |

## ClientHack and launcher boundary

- 两目录均加载相同的 `0007/0010/0020/0030/0050/0110/0130/0140/0150` DLL；SHA-256：

| Plugin | SHA-256 |
|---|---|
| `0007_FaultRecorder.dll` | `CC42E7D1CD632C00B31418F7D8381DE6423348AEEFDD2132AED283C6B2CE90C1` |
| `0010_DisableGameGuard.dll` | `7EADD7C7423E592DBA7B37B0C0B7DA9BC0796D1B40E12AB9FEC0989AC0775EB1` |
| `0020_ClientCompatibility.dll` | `0DBB72E85D17D8B80E43819197ADE56A985AE3487E03181481C2EA4D4F409F44` |
| `0030_MultiInstance.dll` | `36C0875E3710652FC9DA57687C8BC190158E8670974B888CFFCC238C033468E8` |
| `0050_A21RuntimeFix.dll` | `B3773B243149DB570165E6C5E47F5BE3CBCF6B8F42FD83EBBAEEB2E4EF394306` |
| `0110_ServerRedirect.dll` | `9F91D9050183F20AF2EDB9FACC9DB4EF6791C9E30F58187FBCE8D783500F758F` |
| `0130_ExpNoticeProbe.dll` | `42AB0C3AE2D7CAC7625C8231D64BB48DC7520D993F7554E357817A378CB3F525` |
| `0140_ProfileReadFix.dll` | `74AD0DD6D2F67E42251FE5831277C02EA8809B9D5069C7693D44E885335AD66D` |
| `0150_CeraTicketFailureFix.dll` | `8EBA4EBCEC12C39DA3FF4FF4549E00F659EF4A0D6B9B7ACC63053E8974CC9532` |

- `0030_MultiInstance` 已存在；`0100_NetworkHook` 不存在，避免其与 `0050_A21RuntimeFix` 的已知冲突。
  因此本轮使用外部定向 TCP proxy，不改 ClientHack 配置或插件。
- 两份 `Launcher.ini` 都指向 `http://127.0.0.1:18080/`；`ServerRedirect.Enabled=0`。两份 preferences 各有
  一个非空且互异的 login ID，均为 remember 模式；证据未记录 login ID 或密码。
- 当前两个 login ID 在主 Release 数据库中各精确匹配 1 个 account，且各有 1 个 character。

## Host and data boundary

- 当前 Release Host 关键产物：

| File | SHA-256 |
|---|---|
| `Dfo.Server.Host.exe` | `6B109BF430F8B77D57B28861F882147F9383DE532B1BB8FD41248829ABECE3DE` |
| `Dfo.Server.Host.dll` | `775721CDF4FF3B5DF90593E685B56A85DBAE4A8592D5EAB71073EDF3C8FE613A` |
| `Dfo.Server.Protocol.dll` | `A1D666199C15A7ACFA19CC7C9DF38DEC0967FE11D0D3359E38A8D10341D2A8BD` |
| `Dfo.Server.Application.dll` | `14DCBB0C32D26410692F362405C122603D036B881D6727F7A6019A63517039F0` |
| `Dfo.Server.Data.dll` | `6A01D1066A6237DD036FA6AD78B1706EF0480CE0D026ACFAA32FED07F8EFFE00` |

- 主账号数据库 `dfo-server.db` 为 389120 bytes，SHA-256
  `E06DFFBA863FB5FFFF89DB62D73584F7F4DB3A34F6C22387E39C07A5CF2F5BA7`，`PRAGMA integrity_check=ok`；
  当前 WAL 为 0 bytes。实现阶段将在无 Host writer 时复制主 `.db` 到仓库外唯一临时目录，Host 只打开副本，
  因此原数据库不承受本轮角色/位置写入。
- 注意：用 `mode=ro` 验证账号与完整性时，SQLite 刷新了已有 `dfo-server.db-shm` 的协调元数据；主 `.db`
  hash 未变、WAL 仍为空、没有表数据写入。后续不再打开主库，只复制稳定 `.db` 文件并在 postcheck 比对
  主 `.db` hash。

## Observation and recovery feasibility

- PowerShell/.NET 只读 P/Invoke 自检通过：对本进程 `OpenProcess(QUERY_INFORMATION|VM_READ)`、
  `ReadProcessMemory` 读取 8/8 bytes，sentinel `0x1122334455667788`；`VirtualQueryEx` 返回 48-byte x64
  region metadata。实现可使用相同最小权限读取 32-bit DNF 地址空间。
- 当前没有 cdb；它不构成前置条件。主路径为 external read-only scanner；必要 fallback 为任务临时内存
  snapshot + offline scan，不注入、不写目标进程。
- 本轮无需改客户端文件。Host/proxy/probe 都必须产生 PID receipt，只回收 receipt 中的 owner；raw Host 日志、
  隔离数据库和临时 snapshot 均置于仓库外唯一 runtime 目录，仓库只接收 allowlist/sanitized 证据。
- preflight 结论：所有必需输入与恢复边界可用，可以进入 implementation；用户动作仍须等 proxy、Host 和 probe
  均完成实际 readiness 后再请求。
