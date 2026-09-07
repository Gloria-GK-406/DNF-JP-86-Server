---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 物品记录写成客户端真正读的样子，并且不再能把客户端打崩

## Delivered

- **时装条目不再是 143 字节，与其它物品一样是定长 101。** `GameInventoryItemEntry.Write`
  末尾那 42 字节追加块（`4 + 30(JewelSocket) + 4 + 2 + 2`）移除。A21 客户端按定长 101 跨步读，
  追加会把其后每一条推到错误偏移；这是主背包 `STATUS_STACK_BUFFER_OVERRUN (0xC0000409)` 的
  已证实成因——**对着真客户端来回验过：去掉不崩，加回就崩**。
- **`rec+0x2F` 的计数不再能越界。** 它此前把 `core.EmblemSocketCount`（`byte`，0..255，
  来自无约束的 `item_core[45]`）原样上线；该字段在客户端驱动一条**无上界**的 `rep movsd`，
  拷进固定栈缓冲。现夹到 **2**——本编码器在 `+0x30` 之后实际写出的选项 dword 条数。
  **这关掉了一个服务器此前一直带着的、能打崩任意客户端的向量。**
- **`rec+0x1A` 的上界由构造保证**（`{0,1,2}`），并有判据钉住；权威来源说这条路在客户端走
  自增长容器而非定长缓冲，**不是**崩溃点，这一点也记下了，免得两个计数被当成同一种危险。
- **记载改到与客户端自己的解密调试串一致**，两句已被证伪的旧说法各有修正案：
  「那 17 字节位置不可读」→ 是未知字段而非补零，记录被用到 `rec+0x64`；
  「时装条目在定长区后追加珠宝块」→ 是 A12 布局，A21 上是崩溃成因。
- **判据 2250 → 2268**，新增 18 条，其中 16 条覆盖的是**改动前零覆盖**的那条路径。

## Verification

- `./verification/completion-evidence.md`（`decision: pass`，独立验证方取证）
- `./reviews/change-review.md`（`verdict: approved`，独立评审三轮，含「批准范围确认」一节）
- `./verification/mutation-evidence.md`（五处变异，每次还原并核 `sha256`）
- `./behavior-change-list.md`（正向 3 条；反向：行为变了 2 条 / 语义未变 1 条 / 明确没变 5 条）

## Repository Result

- Changed paths: 生产 1 个文件、判据 4 个文件（含新增 `EntryWriterOptionCountTests.cs`）、
  协议文档 1 个文件，加 `.rks-agent/tasks/entries-are-what-the-client-reads/` 下的流程产物。
- Baseline or accepted result: 基线 `445cb5f7`；接受提交由工作区控制器做一次。
- Parent rollback required: no

## Successor Guidance

- **装扮栏（listType 1）非空仍必崩，与本成果无关，是另一个根因。** 崩点在
  `read_bytes(buf, 0x65)`（`0x27C5380`）内部的一条 `rep movsd`，长度用了 `0x30303030`
  而不是 `0x65`。实测与物品 id、物品种类、容器容量都无关，签名逐位相同。取证见主检出的
  `Document/reply-to-vmp-agent.md`，静态侧由另一个 agent 在 `Docs/a21-vmp1/vmp1-10.md` 续。
  **在那条格式化调用被反出来之前，服务器侧不要动 type=1 的装填路径。**
- **背包里的时装不带珠宝槽与颜色，这是已知缺口不是完成态。** 权威表把客户端那个 30 字节
  消费方放在 `rec+0x1A..0x37`，与珠宝槽长度吻合，但本仓在 `+0x2B` 写 `period`、`+0x2F` 写
  选项计数，两处都有权威表背书；「时装记录让这两处给珠宝数据让位」**没有证据**，长度吻合
  不是证据。**已穿戴的时装不受影响**——那是 NOTI-2 的另一个编码器，仍带珠宝与颜色。
- **`+0x06` 承载时装剩余秒数有实机证据**（客户端显示 4153 天）。权威表把 `+0x2B` 叫
  `period`，**不构成**改动 `+0x06` 的理由。
- **R-1 是静默截断**：存储值 3..255 会被改成 2 送出去，不报错不拒绝。库里存的仍是原值
  （`InventoryItemCore.cs:36` 未动），所以查清 `+0x2F` 真实语义后可无损改法。
  这是本轮唯一一处「让数据变形而不告诉任何人」的地方。
- **`+0x2F` 与 `+0x1A` 的字段名与客户端调试串不一致**（本仓叫 `EmblemSocketCount` 与
  编年史选项数，客户端叫附加选项数与纹章数）。偏移已核实一致，**命名本轮不裁定**；
  跨 Data/Persistence/Protocol 三层改名值得单独一件。
- **`protocol/game/server-to-client/NOTI-0x000E.md:35` 仍写「时装为 `0x7E` 字节」**
  （来源是上游泄漏源），与本轮确立的定长 101 并列易被误读。该文件不在本轮 SC-7 点名的
  三处内，同文件的 A21 实测段落写的正是 101。**由验证方发现，值得单独收口。**
- **取证方法上的一条教训，比本轮任何技术结论都值得留下**：同一类取证错误在本轮发作三次
  ——问错了问题、信了截断的输出、以及**为纠正前两次而新造一条没验过的正向断言**。
  第三次发生在专门为认错而写的段落里。**纠错本身也是要取证的，而且纠错时更容易松懈。**
  最后一条阻塞项不是靠改措辞过关的，是靠**把断言改写成能被一次实验证伪的形状**
  （「插一句非零就抛，看谁红」）再去跑那次实验过关的。经过记在 `behavior-change-list.md`。
