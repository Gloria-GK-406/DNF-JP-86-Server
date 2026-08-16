# TASK-01 静态分析工具链与权威身份集合

## Task Unit Contract

**交付物**：可重跑的静态分析工具链，产出 Game 端点两个方向的权威协议身份集合、
上游名称合并结果、以及双向 body 字段布局提取结果；并以正对照证明提取器可用。

**Spec 覆盖**：RQ-001、RQ-002（身份集合完整性的数据基础）、RQ-004（名称来源）、
RQ-005、RQ-006（布局与字段）、RQ-008（定位信息）、RQ-009（正对照）、RQ-014（未知分类）。

**边界**：只写 `protocol/patterns/data/` 下的脚本与数据文件。不写协议文档，不改既有文档。

**依赖**：无。

**完成证据**：普查输出的身份统计与既有资料库记载的数字一致；正对照脚本零失败退出。

## Execution Result

产出脚本：

| 脚本 | 作用 |
|---|---|
| `census-identity.py` | 三锚点普查（`MakeHeader` / `Register` / 两个运行时转发器），产出四元组身份 |
| `build-inventory.py` | 合并上游 `CmdPacketType` / `NotiPacketType` 名称，分配文档归属 |
| `extract-layout.py` | 双向字段序列提取，含重复组识别与未知分类 |
| `check-controls.py` | 正对照闸门 |
| `upstream-map.py` | type → 上游实现文件映射 |
| `make-batches.py` | 富化工作包切分 |
| `rebuild.py` | 全流水线重跑 |

身份普查结果与资料库既有记载的比对：

| 指标 | 本次 | 资料库既有记载 | 一致 |
|---|---:|---:|---|
| C→S `MakeHeader` 构包点 | 1082 点 / 604 type | 1082 点 / 604 type | 是 |
| S→C CMD 表 type | 538（max `0x0435`） | 538（max `0x0435`） | 是 |
| S→C NOTI 表 type | 728（max `0x0405`） | 728（max `0x0405`） | 是 |
| 两表共有 type | 389 | 389 | 是 |
| 未解析注册点 | 0 | — | — |

两个 anchor 精确复现：CMD `0x0001` → handler `0x008C7E20`（批次 `0x008D056B`）、
NOTI `0x0001` → handler `0x00914A50`（批次 `0x009301DC`），与
`patterns/game-receive.md` 记载逐字相符。

上游枚举合并：CMD 1086 条、NOTI 1036 条，与客户端自报的 `CommandPacketCount` /
`NotificationPacketCount` 相等。1410 个文档身份中 1408 个取得官方名（99.9%），
未命名的两个是各族 `0x0000`——枚举自 `0x0001` 起，`0` 不是合法 wire type。

布局提取分布：

| 方向 | closed | closed-parametric | partial | structure-unknown |
|---|---:|---:|---:|---:|
| C→S（682） | 317 | 21 | 140 | 204 |
| S→C（728） | 26 | 5 | 586 | 111 |

## Executor Self-check

- 身份普查的每个数字都与既有独立记载核对过，无一不符；这不是自证，因为
  `game-receive.md` 的数字产生于本工具链之前。
- 正对照 7/7 通过，含一个反向对照（`0x0117` 多态载荷必须**不**被误判为闭合）。
- `0x00AB` 复现出 `3 + n×9` 且前缀与重复组逐项匹配手写文档，`n=4` 闭合到 39 字节。
- 一处真实缺陷已修复并留痕：投票窗口 64 字节漏掉 1 个转发器注册点（首个 push 在
  site−67），扩至 128 后 CMD 表由 537 补足为 538，与既有记载一致；扩窗前后输出 diff
  只有该一行，无其他变化。
- 一处真实缺陷已修复：身份 TSV 曾把注册路径用制表符塞进 `all_sites` 列造成列错位，
  已改为独立列。

## Independent Quick Review

见 TASK-06 的完整结果复核；本单元的产出由正对照与跨记载一致性双重约束，
且其全部下游产出都会在完整复核中被检查。
