# TASK-01 — 两窗口拆分与主目录数据链裁决

## Task Unit Contract

- Revision: 1
- Outcome: 以固定 A21 静态证据纠正旧窗口归属，给出 ID `0x11` 主目录可实施合同。
- Owned boundary: 本 attempt 任务/证据文件；外部 `Document` 报告只读引用。无生产、测试、协议或客户端修改。

## Result — Round 1 (`done`)

### Changed

- 三个聚焦调查分别闭合 matching-info、wire/窗口归属与主目录真实数据源；服务端审计已按纠正结论重写。
- `evidence/input-manifest.md` 固定五份报告、当前 B1/dstr 和接受基线身份。
- `evidence/blocker-verdict.md` 将主目录 blocker 收敛为一个后继实现结果，并隔离 ID `0x14` 未完成项。

### Why

原综合报告把 ID `0x14` 的 `0xF89xxx` 逐行消费者并入 ID `0x11`，导致错误地要求主目录实现
`0x023C`。按窗口构造边界重查后，主目录只遍历 `CNUserManager` 并从 CNUser 关联的 party object 渲染。

### Checks

- 当前 B1、dstr、五份报告和服务端 HEAD 均已现场取哈希。
- ID `0x11`: factory `0x11`、constructor `0xF74790`、final vptr `0x31FFFD4`、flush `0xF73A10`、renderer `0xF71950`。
- ID `0x14`: constructor `0xF89240`、vptr `0x3201ABC`、`0xF88CA0/0xF89520`；`0x023C` manager 归此窗口。
- 生产树未修改；后继实现不得新增 `0x023C` codec 或目录仓储。

### Remaining

- 当前 Spec 的独立审查与 completion verification。
- 后继 Spec 实现 town-visible observer `0x0009` 投影；之后再做两客户端 UI 验收。

