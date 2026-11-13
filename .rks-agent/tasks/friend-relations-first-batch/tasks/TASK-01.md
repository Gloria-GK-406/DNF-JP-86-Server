# TASK-01 — A21 客户端好友与黑名单证据

## Task Unit Contract

- Revision: 1
- Outcome and references: 回答 Spec RQ-001 – RQ-005，结论写入研究记录与协议页人工区（AC-001、RQ-070 的证据部分）；Plan T-001。
- Owned boundary:
  - `.rks-agent/tasks/friend-relations-first-batch/research/client-evidence.md`（新建）
  - 以下协议页的人工区：`protocol/game/client-to-server/0x0122.md`、`0x0123.md`、`0x0076.md`、`0x0077.md`、`0x0078.md`，以及 `protocol/game/server-to-client/NOTI-0x0111.md`、`NOTI-0x0112.md`、`NOTI-0x0002.md`、`NOTI-0x0006.md`（后两页只有涉及时才改）
  - 结论推翻参照时，在 `spec.md` 的 Amendments 追加记录
  - 不改任何代码；机器区不得手改
- Required checks:
  - 每条结论注明证据标签与来源：handler 或读取点的 VA/RVA、关键指令或调用、字符串表编号；
  - 研究记录中的地址可以在 `DNF.unpacked-b1-debug.exe` 上复核；
  - `git diff` 只涉及上述文件，机器区（`rks:machine` 标记之间）没有变化。
- Result review: required — T-003 至 T-005 的布局、上限、推送顺序都建立在这些结论上，错了会被后续单元放大；静态分析结论没有自动测试能够发现错误。

## Detailed Plan

**输入**
- 客户端：`E:\Project\DNF\DNFClient-A21-debug\DNF.unpacked-b1-debug.exe`（SHA-256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`，先核对哈希）。
- 地址换算：VA = 协议页 RVA + 0x400000。
- 工具：capstone 5、pefile（`python -I`）。脚本与中间产物放会话 scratchpad，不进仓库。
- 做法与格式参照 `.rks-agent/tasks/chat-routing-first-batch/research/conversation-re-20261112.md`；读取原语（ReadU8/U16/U32/DStr 等）的识别方法见 `protocol/patterns/game-receive.md`，构包原语见 `protocol/patterns/game-frame.md` 与 `protocol/patterns/data/probes/a21-census-anchors.md`。

**问题与入口**

1. **RQ-001 应答与通知的读取布局**：
   - CMD 应答 handler：`0x0122` RVA `0x00D19880`；`0x0123` `0x00D19950`；`0x0076` `0x00D1F630`；`0x0077` `0x00D243E0`；`0x0078` 见其协议页。
   - NOTI handler：`0x0111` `0x00D61580`；`0x0112` `0x00D6F000`。
   - 逐个读出字段宽度、顺序、分支条件，以及各错误码对应的提示字符串编号与文字（只记编号与简短含义）。
   - 与参照布局对照，参照布局见 Spec RQ-030 – RQ-039。
   - 同时核对请求构包点 `0x0162D31C`（0x0122）、`0x0162D4C6`（0x0123）、`0x0162D684`/`0x0162D72F`（0x0076/0x0077），确认请求字段与参照一致。
2. **RQ-002 上限**：在好友与黑名单界面添加流程（构包点上游的调用者）或列表容器中，找条数比较常量；同时查对应提示字符串。
3. **RQ-003 末尾字段**：`0x0111` 全量形态中，每条最后 4 字节读到哪里、之后被谁使用（例如作为查找键、存到好友条目结构的某个偏移）。
4. **RQ-004 黑名单显示**：`0x0111` subcmd 0 的处理是否清空共享名单容器、是否连黑名单节点一并清掉；与 `0x0078` 应答写入的是否同一个容器。
5. **RQ-005 同频道在线显示**：好友条目的在线状态由什么决定。例如 ch≠0 表示其他频道；ch=0 时是否去查本频道的角色表（USERINFO 建立的表）。还要查明：只为显示在线而发 NOTI 0x0002 USERINFO，会不会让该角色出现在接收者当前场景；若会，有没有不入场景的形态（subtype）可用。
6. **下线通知与 USER_LEAVE 的先后（RQ-051）**：`0x0112` ch=0 的处理是否依赖该角色仍在本频道表里。

**产出**
- `research/client-evidence.md`：按问题列出结论、证据、置信度，最后一节汇总「与参照不同之处」。
- 协议页人工区：各自协议的结论摘要，并链接研究记录。
- 有推翻参照的结论时，在 Spec Amendments 追加一条，写清影响哪些 RQ。

**执行方式**：由一个独立 worker 完成静态分析。worker 只写上述文件，不提交；主代理检查后再写 Result。

## Result

- Outcome: done
- Round: 1，worker 静态分析，未提交的改动即本单元全部 owned change（见 Changed）。
- Changed:
  - 新建 `research/client-evidence.md`（RQ-001 – RQ-005 及下线顺序的结论、证据地址、与参照差异）。
  - 协议页人工区：`0x0122`、`0x0123`、`0x0076`、`0x0077`、`0x0078`、NOTI `0x0111`、`0x0112`、`0x0002`、`0x0006`。只在 `rks:machine:end` 之后追加，机器区与 A12 区未动。
  - `spec.md` Amendments 追加 AM-1（单独作为修订提交）。
- Why:
  - 合同里写的 SHA-256 `1F8244D3…` 是原版 `DNF.exe` 的哈希；分析对象 b1 解包 exe 实际是 `D7618A10…0A99A`，研究记录按 b1 登记。
  - RQ-005 中「subtype 0 发给不同区域接收者是否入场景」静态无法判定，记为待实机，没有猜测。处理方案由 TASK-04 决定。
- Checks:
  - `git diff --stat` 只涉及 owned 文件（10 个文件，+275 行）；
  - 协议页 diff 中没有 `rks:machine` 标记的增删；
  - 每条结论都带 VA 与证据标签。反汇编脚本 `scratchpad/task01/lib.py` 可在 b1 exe 上复核。
- Remaining:
  - `NOTI-0x0111.md` 的 A12 区按迁移规则应在 A21 重做完成后整区删除，留到 TASK-04 实现完成时处理；
  - `catalog.md` 随实现单元更新。
