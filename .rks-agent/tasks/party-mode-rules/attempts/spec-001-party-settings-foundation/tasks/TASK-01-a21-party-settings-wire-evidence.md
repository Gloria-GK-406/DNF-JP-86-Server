# TASK-01 — 固定 A21 队伍设置 wire 证据基线

## Task Unit Contract

- Revision: 1 (identifies this contract and its detailed plan)
- Outcome and references: 形成能直接约束后续设置模型/codec 的当前 A21 `0x000C` create/edit 与
  `NOTI 0x0009 type=1` 证据基线，覆盖 Plan T-001、RQ-004/RQ-008/RQ-009、AC-003/AC-008。
- Owned boundary: 只读检查 `E:/Project/DNF/ServerS4A21`、当前 A21 客户端及历史抓包；可写本 Task、
  本子规格 `evidence/`、`protocol/patterns/data/traces/` 下本轮新记录，以及既有
  `protocol/game/client-to-server/0x000C.md`、`protocol/game/server-to-client/NOTI-0x0009.md`。
  不改 `Server/src`、`Server/tests`、客户端二进制/配置或其他协议状态。
- Required checks: 固定客户端/字符串库/上游 commit 身份；每个布局结论至少有可复现静态地址、真实帧
  或两项独立来源；逐字节核对长度公式、little-endian 和 raw-dstr 分支；证明未改生产/测试代码；
  `git diff --check` 通过。
- Dependencies: 根接受基线 `d717f1b0`、ready Spec/Plan；无前序 Task 输出。

## Detailed Plan

1. 在 evidence 记录中固定当前输入：`DNFClient-A21-protocol/DNF.unpacked-b1-debug.exe`、`dstr.dat`、
   可用 frame-capture 日志的 SHA-256/大小/时间，及 `ServerS4A21 origin/master` 的精确 commit。拒绝用
   本地落后工作树代替 `git show origin/master:path/to/file`。
2. 从上游 parser、handler、party manager 和 NOTI builder 机械提取：
   - create 12B 与 edit `2 + i32 length + title + 10` 的候选布局和严格长度公式；
   - 无队创建、已有队更新、队长/当前会话/容量校验；
   - 新建 type-0、编辑 type-1 以及 title/info block 的编码路径。
   把它们标成“上游代码事实”，不直接提升为 A21 wire 确认。
3. 精确扫描当前 debug/protocol 客户端的 JSONL 与归档会话，按方向、type/opcode 和 body 保存所有
   `0x000C`、`NOTI 0x0009 type=1` 样本；记录会话关联和缺失事实。若没有样本，不以邻近包猜布局。
4. 用当前仓库已有 PE/反汇编依赖或最小只读探针，从命令表 `0x000C -> 0x01119990` 反向定位响应
   handler，再从 UI 字符串/构包原语及 xref 定位 create/edit 序列化调用。对每条路径记录 VA/RVA、
   指令窗口、writer 调用顺序和长度计算；必要时写入本 Task evidence 下的可复现只读脚本。
5. 从 `NOTI 0x0009` 客户端 parser `0x01172000..0x011726B9` 重新证明 type=1 只消费信息块、
   `info0==0` 的 raw-dstr 分支、12 字节规范设置、`[9]` 类别和 `[10..11]` 目标读取；通过 dstr 文本及
   控制流证明 `0/1/2` 的 Story/Cycle/Normal 分支，不把字符串邻接当成字段证据。
6. 建立逐字段证据矩阵：offset/width/encoding/meaning/source/confidence/conflict。明确标题字节是否独立于
   12 字节块、capacity 的实际 offset、错误/ACK handler 是否读取 body、两种请求是否由同一 UI/状态
   触发，以及仍未知的 `[0..8]` 字节。
7. 新增一份本轮 static/capture trace，并更新 `0x000C.md` 与 `NOTI-0x0009.md`：旧 7B/旧上游描述
   保留为历史冲突，不覆盖；当前证据不足的字段保持 High/Hypothesis。不得修改 index/catalog 状态或
   标 `ClientVerified`，除非发现现成且可归因的真实双向帧。
8. 自检 owned diff、地址可复现性、布局算术和所有引用；运行 `git diff --check`，并记录
   Changed/Why/Checks/Remaining。若 current A21 无法证明足以安全实现的请求形状，Task 结果必须明确
   blocker 与所需最小抓包动作，不得自行放宽 decoder。

## Result

### Changed

- 新增 `evidence/probe_a21_party_settings.py`：固定当前 exe/dstr SHA-256，校验 14 个关键 VA
  精确字节锚点，输出 0x000C 四条构包路径、响应 handler、NOTI parser、类别/目标分支及 dstr 文本。
- 新增 `protocol/patterns/data/traces/a21-party-settings-static-20260912.md`：记录固定输入、上游 commit、
  抓包覆盖缺口、逐字段矩阵、冲突裁决、可实施边界和后继未知。
- 更新 `protocol/game/client-to-server/0x000C.md` 人工区：旧 7B 假说保留为历史冲突；当前 A21 采用
  exact 12B direct / `16+n` edit，并明确标题与 12B 设置块分离。
- 更新 `protocol/game/server-to-client/NOTI-0x0009.md` 人工区：补 type-1 信息块、raw-dstr gate、
  category/target 与 `hasExtra` 边界；状态保持非 `ClientVerified`。

### Why

当前客户端静态构包、当前 parser 与固定上游捕获数组已交叉闭合长度与字段顺序，足以约束后续
模型和严格 decoder；同时当前三个 A21 客户端目录没有帧日志，所以没有把上游数组或 UI 结果冒充
本仓当前实帧。生产代码仍保持不动，避免在证据单元提前写入语义。

### Checks

- `python -X utf8 .../probe_a21_party_settings.py <exe> <dstr>`：PASS；输入哈希匹配，
  `byte_anchors=14/14 PASS`，所有关键 dstr 文本存在。
- 当前三客户端 frame-capture/write-trace 匹配数均为 0；两份旧 frame trace 对
  `type/arg_type/cmd == 12` 的命中均为 0；两份 `ClientHack/runtime/captures` 确认为字节诊断快照而非帧。
- `git diff --cached --check`：PASS。
- owned diff 检查：没有 `Server/src` 或 `Server/tests` 变更。

### Remaining

- T-002/T-003 可消费：规范块 `[2]=capacity`、`[9]=Story/Cycle/Normal 0/1/2`、
  `[10..11]=ModeTargetRaw u16-le`，其余未知字节无损保留；decoder 只接受 exact 12B 与 exact `16+n`。
- `0x000C` response body、type-1 非空标题回显、`hasExtra!=0`、模式目标资料表身份仍未知，不在
  后续基础实现中猜测。
- 当前 A21 create/edit C2S 与 type-1 S2C 的可归因实帧仍是 T-004 完成前置；在此之前协议状态
  不提升为 `ClientVerified`。

## Independent Quick Review — TASK01-QR-20260912-01

```yaml
kind: rks-review
verdict: passed
```

- Reviewer 独立复算固定 EXE/dstr 哈希，探针 `14/14` 字节锚点通过；四条 builder 路径闭合
  direct `12B` / edit `16+n`，规范块 offset 与 little-endian 一致。
- 当前 parser 独立确认 type-1 读取信息块、跳过 roster、继续读取 `hasExtra`，18/22 字节分支算术
  正确；客户端分支与 dstr 共同支撑 `Story/Cycle/Normal = 0/1/2`。
- 上游 `origin/master=c58cd2b479b0357cb6ca78887c9d638b080f4638` 与当前客户端证据分级清晰；三客户端
  当前捕获为零、旧 JSONL 无 type 12，实帧缺口诚实留给 T-004，未标 `ClientVerified`。
- owned boundary 内无 `Server/src`、`Server/tests`、index/catalog 变更；相对 `d717f1b0` 的完整
  `git diff --check` 通过。未发现阻断项。
