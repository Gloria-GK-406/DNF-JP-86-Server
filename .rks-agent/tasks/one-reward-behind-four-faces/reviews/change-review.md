---
kind: rks-review
verdict: approved
---

# 完整变更评审：翻牌奖励按线上真实结构重做

评审范围 `change`。基线 `a5d164af`，变更全部未提交（跟踪 6 个 + 新增未跟踪 1 个）。
结论独立得出，未采信执行者的任何判定；下列每一条都自己跑过或自己比对过。

## 一、判据的断言对象（EV-005，本次唯一新增硬约束）

**成立。** 关于「客户端能读到什么」的每一条判据都建立在解码结果上。

`Server/tests/Dfo.Server.Protocol.Tests/Game/CardPanelFrameReader.cs` 与协议记载逐段对齐：

| 记载（NOTI-0x0023「帧的布局」/ 0x0047「一条记录」） | 读取器 |
|---|---|
| `4×i32 + u8 + 23×i32`（到 +108，109 字节） | L88–L90 `Skip(4*4)` / `Skip(1)` / `Skip(23*4)` |
| `u8 A + A×(u32,u32)`、`u8 B + B×(u32,u32)` | L92–L93 `SkipCountedPairs` ×2（**消费计数**） |
| `u32 ×2`、`5×i32`、`4×i32 评分四元组` | L94–L96 |
| `u32 N + N×(u32,u32)` | L98–L100（读 N，由 N 决定后续位置） |
| `u8 M + M×15B` | L102–L103 |
| 组 0 → `i32 付费花费` → 组 1 → 组 2 | L105–L108，顺序与记载一致 |
| 一个 slot = `u8 c + c×(i32,i32)` | L160–L172 `ReadEntries`（由 c 决定位置） |
| 22 字节尾（`4+4+1+1+4+4+4`） | L110–L112 `TrailerLength = 22` |
| 应答 = `0x01` 信封 + 8×(`a`,`b`,`c`,`8c`,尾) | L124–L146 |
| 走不到帧尾即拒绝 | L114–L116、L148–L150 `Require(at == body.Length)`；越界读由 L174–L192 的 `Require` 拦下 |

没有判据绕过它。两个测试文件里剩余的字节偏移断言只有三处，且都不承担牌面语义：
`DungeonClearCodecTests.cs:97–111`（帧前段固定字段与三个计数位）、
`:126–130`（**全帧非零字节集合**，是加强不是绕过）、`:449–450`（比对卡尾之前的整段）。
牌面内容、行列归属、条目次序、应答八条记录，全部只经读取器取值。

### 变异复核（自己挑的 4 个，全部报红且都不是编译失败）

命令一律 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --nologo`。

| # | 变异 | 结果 |
|---|---|---|
| 1 | `DungeonClearSlice.cs:348` 把装备 id 写进金币条目的 id 格 | 失败 1 / 通过 490，`The_gold_entrys_identifier_is_not_the_pieces` |
| 2 | `:288/:290` 免费行与黄金行对调（长度不变，编码器自洽） | 失败 10 / 通过 481 |
| 3 | `:336/:346/:427` 牌面铺到四列，**并同步改 `ClearRewardBodyLengthFor` 使编码器内部自洽** | 失败 1 / 通过 490，`Only_the_position_the_flip_claims_reaches_the_wire`——这一条是纯解码断言抓到的，不是长度自检抓到的 |
| 4 | `CardRewardPayloadCodec.cs:222` 记录 4–7 的 `c` 写 `0xFF`（照抄参照服务端） | 失败 10 / 通过 481，读取器走不到帧尾 |

另复核 `OwnSeat` 由 `0x00` 改 `0x01`：失败 7 / 通过 484。

第 3 个是关键：它模拟的是「一个自洽的错实现」，长度自检抓不到，只有按客户端读法解码才抓得到。
这正是上一轮失守的那一族错误，现在被判据覆盖了。

**全部变异已还原并 sha256 校验：**

```
d9a002784508479e72d5ebbbc4bc7fb2c18f1720c3f9c92a6d73f3bf542313d7  DungeonClearSlice.cs
3fc1acf8a26216a4318f7642abe83c1772f490453078869e5eb4ce2df85b7732  CardRewardPayloadCodec.cs
```

还原后重跑 491/491 通过；`git status --porcelain -- Server/` 与 `git diff a5d164af --stat -- Server/`
与评审开始时逐字相同；全仓 `grep -rn "MUTATION"` 无残留。

## 二、RQ 逐条覆盖

| RQ | 判据 | 核对 |
|---|---|---|
| RQ-001 | `The_reward_is_declared_on_the_free_row_as_gold_then_the_piece`、`Only_the_position_the_flip_claims_reaches_the_wire` | 取 `tail.FreeRow.Columns[0]`；读取器的行序即记载的组序，行归属真被检验（变异 2 报红 10 条） |
| RQ-002 | 同上 + `A_card_with_nothing_behind_it_declares_only_its_gold`、`The_gold_entrys_identifier_is_not_the_pieces`、`The_gold_on_the_card_is_what_a_flip_of_it_pays` | 条目 0 = 金币且 id 恒 0、条目 1 = 装备、无装备时只一条，均逐值断言 |
| RQ-003 | `The_gold_and_black_diamond_rows_are_declared_empty` | 断言两行各 8 列 `Entries` 为空（`c = 0`），与「存在但不付」的 `c = 1` 明确区分 |
| RQ-004 | `The_frame_grows_by_one_entry_when_a_piece_sits_behind_the_card`(274/282)、`Every_face_produces_a_frame_that_can_be_walked_to_its_end`、`A_face_changes_the_card_tail_and_nothing_in_front_of_it` | 帧长与声明一致由读取器的 `at == body.Length` 保证，不是由常量对常量 |
| RQ-005 | `The_answer_declares_every_column_including_the_untouched_ones` | 解出 8 条 |
| RQ-006 | `The_answer_hands_over_nothing`（两种牌型 × 四列）、`The_whole_answer_can_be_walked_to_its_end` | 全部 `c = 0` |
| RQ-007 | `Only_the_column_that_was_turned_over_carries_a_seat`(0/1/2/3)、`The_seat_this_server_answers_as_is_zero`、`A_flip_names_its_own_row_and_leaves_the_other_alone` | 翻开列写 `0x00`、其余 `0xFF`、另一行不被误写；四列全跑，排除了「每列都写 0」 |
| RQ-008 | `An_answer_carrying_nothing_is_the_length_the_capture_had`(33) + 读取器拒绝走不到帧尾 | 变异 4 证明这条真拦得住 `0xFF` |
| RQ-009 | 实机 | 见第五节 |
| RQ-010 | 见第四节 | |

十条都有判据，且判据检验的正是该条要求。

## 三、实现与协议记载一致性

逐条比对 `NOTI-0x0023.md`「卡尾」「一个 slot 长什么样」「组、slot、条目各是什么」「帧长」与
`0x0047.md`「应答 S→C」「一条记录」「参照服务端怎么写」：

- 三组 = 三行：`DungeonClearSlice.cs:288/290/291`，顺序 组 0 → 付费花费 → 组 1 → 组 2 ✓
- slot = 列，每行 8 列：`WireCardColumnCount = 8`（`:392`），三处循环 ✓
- 条目 0 是金币且 itemId 不被读：`:348` 恒写 0，`:349` 写金币 ✓
- 条目 1 是装备：`:352–353` ✓
- 黄金行与黑砖行写空：`WriteUnturnedRow` 每列写 `c = 0`（`:367–371`），与参照服务端同形 ✓
- 帧长 `210 + 8N + 8E`：N=0、E=8 → 274；E=9 → 282，与记载表里「本仓 上游形状 + 一件装备 = 282」闭合 ✓
- 应答八条状态快照：`CardRewardPayloadCodec.cs:215–224` ✓
- `a`/`b` 是座位号：`OwnSeat = 0x00` / `NoSeat = 0xFF`，集中在两个常量上（契约 Assumptions 要求的「集中在一处」满足）✓
- 免费翻牌 `c = 0`：`:222` ✓
- 填充记录 `c = 0` 而非 `0xFF`：全部八条都是 0，比参照服务端更严 ✓
- 记录尾字节 0：`:223`，与参照一致 ✓

另核了一遍旧实现为什么会显示 NONE，作为「这次真的改对了」的反证：基线在有装备时写
`marker=2, itemId@161, gold@165`，客户端按 `c=2` 读会把第二条读成 `(1, 0)`（即下一列的计数字节），
牌面画成 id=1 数量 0，且其后错位 8 字节。新写法把 `(0, gold)` 与 `(itemId, quantity)` 各占一条，
帧增长 8 字节。**空牌面帧与基线逐字节相同**（`Nothing_else_in_the_reward_breakdown_carries_a_value`
未改仍绿即证），差异只在有奖励时出现——这与记载给出的病理完全对上。

## 四、规则层未动（RQ-010 / 契约 Non-Goals）

- `git diff a5d164af --stat -- Server/src/Dfo.Server.Application Server/src/Dfo.Server.Abstractions Server/src/Dfo.Server.Infrastructure` → **空**。
- `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs` sha256 =
  `93fe547557fefb027b70d25087cdb28edd3b9b92ff5641a76ac3c12c7f171ef2`，与探针还原后的宣称值一致，
  且对基线无 diff。
- 变更集只有 4 个源文件 + 2 个判据文件 + 1 个新增判据支撑文件，全部在 `Dfo.Server.Protocol`
  与其判据项目内。`Dfo.Server.Application.Tests`（906 条）对基线零 diff，SC-5「规则层判据的 diff
  不改任何断言或期望值」按定义成立。

## 五、全量判据与实机（独立复核）

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
自己跑了一遍：

```
Networking 52 / Protocol 491 / Pvf 60 / Host 4 / Persistence 248 /
Infrastructure 215（跳过 2）/ Application 906
合计 通过 1976，失败 0，跳过 2
```

与宣称一致。两条跳过确为 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`，
即「缺档案时应报错」的用例，跳过数停在 2 说明档案确实加载。

（附带确认：不带 `PVF_ARCHIVE_PATH` 时 `ItemPathLiveTests` 会失败，所以那条环境变量是必需的，
不是可省的装饰。）

实机日志 `C:/Users/Gloria/AppData/Local/Temp/live-rq009.log` 存在，第 418 行为
`Select card Game:01-00-00-00 character=1: Free position 0 taken, 4 positions revealed, bag told, charged 0, paid 1`，
与宣称逐字相符；上下文可见同一局的 `SetPlayResultHandler` 与 `ScoreScrollStateHandler`。
屏幕观察部分只能采信使用者确认，评审者无法独立复现。

## 六、分层与仓库约定

- 切片内不跨协议引用：`Game/DungeonClear/` 只经领域服务 `ICardRewardDomainService` 触达翻牌，
  未引用 `Game/CardReward/` 的任何类型；反向零引用。共用的 `ClearRewardCardGold` 位于
  endpoint 层 `Game/GameProtocol.cs:45`，符合「要共用就上提到 endpoint 层」。
- 新增的 `CardPanelFrameReader.cs` 放在判据项目的 `Game/`（endpoint 层）而非任一切片子目录下，
  命名空间 `Dfo.Server.Protocol.Tests.Game`——两个切片的判据共用它，位置与同一条规则一致。
- 命令处理器约定：`SelectCardHandler` 仍只做「解身份 → 只读查询 → `commandBus.SendAsync`
  → sender 发送」四类允许动作，本次只把发出的 message 换了参数，未在 handler 里就地做业务。
- CMD/NOTI 边界：`SelectCardResponse` 仍是 `IGameCmdMessage`，经 `GameCmdMessageEncoder` 出站，
  接线点仍只有 `ProtocolModule.cs:500`。

## 七、多余或缺失的行为

没有超出 Spec 的改动。逐项核过：

- 22 字节尾从「一堆匿名零」改写成「七个具名字段」——**字节完全相同**（4+16+22 与旧的
  4+16+4+1+1+4+4+8 逐字节等价，全零），是记法变更不是行为变更。
- `SelectCardResponse.Slots` → `TurnedColumn`：`message.Index` 而非 `result.Slots`，与
  「客户端不上报点了哪张、恒为 0」一致；解包器仍把 `> 3` 的请求丢弃，未放宽。
- 无探针残留：变更的四个源文件与新增判据文件里没有 `Console.`、`TODO`、`HACK`、`Debugger`、
  `Thread.Sleep`，没有硬编码物品 id（`31001` 等只出现在判据里作为样本值）。
- 仓库根目录那三个 0 字节文件未被触碰。
- `Docs/` 里提到翻牌的三处（`ClockService.md`、`DropRates.md`、`新功能接入规范.md`）讲的都是
  规则层与费率表，不含帧结构，本次无需回改；协议记载三份已按输入更新并具名。

## 八、非阻断观察

以下都不影响裁决，留给后续顺手清理。

1. **代码注释里仍留着已被推翻的旧读法。** 影响的是「下一个人照着注释理解帧」的准确度，
   而上一轮的失败恰恰始于一份错的注释被当成事实：
   - `Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs:210` 摘要仍写
     「274 bytes」，而帧现在是 274 或 282；`:220–224` 仍按旧模型描述参照帧
     「the free-card pair at 240 and 245, seven nine-byte card seats」——按新读法 240 是条目数、
     241/245 是条目 0 的 `(itemId, amount)`，「pair 在 240 和 245」错开一格。
   - `Server/src/Dfo.Server.Protocol/Game/CardReward/CardRewardPayloadCodec.cs:15–16`
     「the answer has a single position byte that no live client has confirmed」——那个位置字节
     已经不存在了。
   - `Server/src/Dfo.Server.Protocol/Game/CardReward/CardRewardHandlers.cs:46`
     `SelectCardHandler` 摘要仍是「answers with every position of the face it belonged to」。
   - `Server/tests/.../DungeonClear/DungeonClearCodecTests.cs:13–18` 类注释仍说
     「354 bytes there against 274 here」「The offsets asserted below…」；`:106` 的行内注释
     `// no item behind the free position` 现在描述的是条目数为 1。
2. **免费行的 7 个未用列写 `c = 1`（一条 `(0,0)`）而非 `c = 0`。** 这是照抄参照服务端的形状，
   本次不算缺陷；但按 `NOTI-0x0023.md` 的记载，`[panel+0x1E4]` 由组 0 的非空 slot 数累加，
   于是它等于 8，而面板只画 4 列，客户端的「这一局翻完了」判定（`0x21EEE70`）永远不成立。
   参照服务端有同样的性质，实机也没暴露问题，故只记不提。
3. **AC-004 里「带多条条目」这一情形按现在的设计不可产生**（一列最多两条），
   `Every_face_produces_a_frame_that_can_be_walked_to_its_end` 的 1/2/4 三个参数产出同一帧。
   实质覆盖靠 `Only_the_position_the_flip_claims_reaches_the_wire`（四位置牌面）与读取器的
   走到帧尾检查，够用；条目数的自由度是被设计掉的，不是被漏掉的。
4. **EV-003 里「服务端逐字节日志」这一半在 `live-rq009.log` 中不存在**——该文件是应用层日志，
   没有结算帧的十六进制转储。决定性的可观察量（屏幕）由使用者确认，AC-008 的 Then 因此仍然成立；
   但若日后要复查这一局的帧，手上没有字节。
5. `SelectCardCommandResult.Slots` 仍在（只用于 `CardRewardHandlers.cs:123` 的日志），
   即「规则层摇 4 个位置而只有第 0 位能送达」的残留——已具名为另开任务，此处仅记位置。
