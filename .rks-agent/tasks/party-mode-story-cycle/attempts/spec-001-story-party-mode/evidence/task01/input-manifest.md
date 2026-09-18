# TASK-01 固定输入清单

## 当前 A21 客户端

| 输入 | 大小 | 修改时间 | SHA-256 |
|---|---:|---|---|
| `E:/Project/DNF/DNFClient-A21-debug/DNF.unpacked-b1-debug.exe` | 94,199,808 | `2026-09-12T22:26:36.5834478+08:00` | `DC322D8FB2A8A33FCE31065CEEAC5801ED854F34409190FE257AACF2663306E9` |
| `E:/Project/DNF/DNFClient-A21-debug/dstr.dat` | 3,173,376 | `2026-02-04T14:51:25.4340524+08:00` | `60E9857773831B341BAE0D490FCAEAF0AF184B9776717E2FCF293E1BC7F92F10` |

EXE 为 PE32，ImageBase `0x00400000`。本单元所有地址均写成 VA；换算规则固定为
`RVA = VA - 0x00400000`，探针按 RVA 读取 PE 字节。当前 EXE 是 2026-09-12 重建后的 B1，虽然哈希不同于
既有 Party 设置证据使用的 `CA7E...8D14`，但本单元 19 个语义窗口逐字节通过；因此没有把旧哈希直接
当作当前身份。

## 接受基线与既有证据

| 输入 | 身份 / SHA-256 | 本单元用途 |
|---|---|---|
| Story/Cycle successor 接受基线 | commit `9a3a0b492da11f48525d1225d02513e0dd1cd458` | 已接受的 12-byte `PartySettings`、严格双形状 `0x000C`、revision/generation 通知边界 |
| `client-debug-lab` | commit `c8f7497e99800bcd72e0ef88161f58367e99eaf7` | 当前 A21 地址/RVA 与 NetworkHook 能力来源 |
| `protocol/patterns/data/traces/a21-party-settings-static-20260912.md`（旧任务接受结果） | `B897D449DE4B28E95F8FA618F420064E270B93B8F512DF0A8968340D871714F2` | 共享构包/读取顺序和类别映射起点 |
| `probe_a21_party_settings.py`（旧任务接受结果） | `4876E49A50B02F65FF484D6003A7190020BB64551EA68397C11D4CDBDA2406BF` | 已接受字段宽度与 parser 锚点 |
| `probe_normal_party.py`（旧任务接受结果） | `A88C7D9A256C3FB7131D1DBD4595B7995584C5104DA4BEC73C44238395E99B7E` | Normal fallback/生命周期对照 |
| `capture_normal_proxy.py`（旧任务接受结果） | `1C04BC0B0139B4EED09AF9B93D372DB1602145A754FB9CA86DD455459A3862B0` | 定向捕获的流重组与敏感数据白名单参考；本单元不执行 |

旧证据文件位于只读对照 worktree
`E:/Project/DNF/DNF-JP-86-Server/.worktrees/party-mode-rules/`；本单元没有修改该 worktree。

## 固定上游

上游工作树当前 HEAD 为 `2db3a135...`，会漂移；本单元只读取固定
`c58cd2b479b0357cb6ca78887c9d638b080f4638` 的 Git blob：

| commit 内路径 | 字节数 | SHA-256 |
|---|---:|---|
| `Server/DfoServer/Network/Parsers/Party/SetPartyInfoRequest.cs` | 2,032 | `1FE9E0FFCF429806E5FF9573ADE0677D573162331631805B010095FEE2E35605` |
| `Server/DfoServer/Network/Handlers/PartyHandler.cs` | 106,076 | `19D4938762568504724C509B783588884B5BFB3179FC58E81F26610F20F2DEC5` |
| `Server/DfoServer/Game/Party/Party.cs` | 8,759 | `5E7179FF7E0BF7D289DDFEDD71EFF49685F35CF1EBBD180BBA0E1839252209C4` |
| `Server/DfoServer/Game/Party/PartyManager.cs` | 35,849 | `F5EB4A596E5B147A5B45B946CCE535E4685D282202A729A2DCEF276803F97073` |
| `Server/DfoServer/Network/Builders/Party/PartyInfoNotiBuilder.cs` | 4,141 | `DE20E3A8AF412F21B8F88529B2A6C2FB903F61242A8126470609020842C85CD3` |

探针使用 `git show <fixed-commit>:<path>` 读取 blob 并重算 SHA-256，不读取上游 checkout 的同名文件。
