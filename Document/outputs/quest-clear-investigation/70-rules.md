# 70 服务端批量清任务道具调查

调查日期：2026-09-23。只读分析 `E:/Project/DNF/Research/1995chen-dnf/other/等级补丁/70/df_game_r`（ELF32 x86，36,360,954 字节，SHA-256 `CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7`）。地址均为该 ELF 虚拟地址。分析使用 `Document/outputs/enemy-reward-investigation/inspect_enemy_elf.py` 和 `Document/outputs/object-drop-investigation/elf.py` 的符号边界与 Capstone 反汇编；未运行 ELF、客户端、进程或数据库。本文的 **Confirmed** 只表示此 ELF 的静态控制流，不表示 A21 实机验证。

## 可实施结论

**Confirmed：该 70 构建没有发现与 A21 道具 `10006007` 对应的 `[quest clear]` 批量动作实现。** `getStackableActionType`（`0x089aeb9b–0x089aed66`）只识别固定字符串；逐项对照其 `.rodata` 比较常量，列表没有 `[quest clear]`，未知字符串在 `0x089aed60` 返回 `0`。ELF 全字节搜索也没有字面量 `[quest clear]`。这不是凭函数名称否定：`ImportStackableScript` 在 `0x089b239b–0x089b23ac` 确实将 `[action type]` 的字符串交给该转换函数，并把结果存到脚本字段 `+0x1e8`。`StackableAction::Action::init_function_type`（`0x0827f7c8–0x0827fa0a`）的执行表只注册 `0x34 → proc_bingo`、`0x36 → proc_lotteryCount`、`0x37 → proc_addFatigue`，不存在 `77/0x4d` 或清任务处理函数。这里的结论限于该 ELF；不证明其他版本或外部插件不存在。

**Confirmed：70 的 `0x0204` 与给定 A21 实帧不能按同一布局移植。** `Dispatcher_StackableActionUse::read`（`0x0825df72–0x0825e11c`）按序读 `u16 slot`、`u8 count`、`u32 inventoryChecksum`、`u8 actionType`、`i32 argument1`、`i32 argument2`，共 16 字节。用户提供的 A21 实帧 body 为 19 字节 `450000000000004D0000000000000000000000`；按客户端现有构包记录，边界是 `u16,u8,u32,u32,u32,u32`，offset 7 的 `0x4d=77` 是一个 `u32` 候选动作值（[0x0204 协议记录](../../../protocol/game/client-to-server/0x0204.md)）。即使把这 19 字节强行送给 70 读取器，70 会在 offset 7 只取一个字节作 `actionType=77`，随后字段整体错位；这只是字节级对比，不是线上互通结论。`slot=69` 可由开头 `45 00` 确认；offset 7 的业务意义仍需 A21 客户端构包与服务端响应证实。

**Confirmed：70 分发器对未注册动作没有清任务副作用。** `Dispatcher_StackableActionUse::process`（`0x0825e11e–0x0825e331`）先用槽位找物品，按请求动作类型调用 `Action::check`、`Action::run`，成功后才调用 `CInventory::use_item`。`Action::check`（`0x0827fa26–0x0827fa99`）和 `Action::run`（`0x0827fa9a–0x0827fb13`）在 map 未命中时均返回 `0`。因此，不能把返回零误判成清任务成功；若合法物品与数量检查均通过，控制流仍可能进入消耗道具步骤。是否实际消耗、如何回滚取决于具体库存状态，未作运行验证。`check_error`（`0x0825e332–0x0825e4eb`）还检查角色、交易状态、动作类型非零、物品、可用场景、冷却与库存可用量。

## 70 中相似但不等价的功能

**Confirmed：另有跳跃角色的 `clearEpicQuest`，它不是 `0x0204` 的道具处理。** 唯一直接调用点来自跳跃角色 `dispatch_sig`（`0x08199c1b`）；`0x0204` 分发器调用链只指向上面的 Action map。`clearEpicQuest`（`0x081993c4–0x0819973d`）先取 `QuestList::getQuestNPCList(grade=0)`，再扫描 20 个当前任务槽：对能找到定义、`Quest+0x08 == 0`、`Quest+0x20 < 角色等级` 的槽，设置已清标志并清除当前槽及其伴随状态（`0x0819941a–0x08199518`）。随后遍历 grade 0 清单，排除 `Quest+0x04` 为 `0xe04..0xe08`、职业不符、等级未低于当前等级、已清的任务，再置已清标志（`0x081995a2–0x081996b8`）。该路径没有调用 `check_ahead_quest` 或正常任务受理条件检查；它不能证明前置链、转职、普通任务或任务类型都按 A21 道具文案筛选。

**Confirmed：这个跳跃角色路径会调用 `CUser::quest_basic_reward`**，分别位于 `0x0819954a` 和 `0x081996ee`，所以与 `10006007` 文案中的“不获得任何奖励”直接冲突。它直接设置清标志，也没有在此函数中调用普通 `UserQuest::finish_quest` 或 `CUser::_onQuestClear`；不可据此推断普通完成事件会触发。对这一独立路径，`Quest+0x20 < level` 是严格小于，即排除当前等级；它也不能回答 A21 道具所谓“当前等级”的上界到底采用最小等级、最大等级还是其他规则。

70 ELF 中还有 `CUser::questAutoClear`（`0x08692390`），但函数体仅 `push ebp; mov ebp,esp; pop ebp; ret`。回归用户首登路径 `processReturnUserQuestAutoClear`（`0x08691e70–0x08691ec2`）会调用它，却没有实际清理行为。`[clear quests]` 字符串位于 `0x08e1380d`，交叉引用是 `ImportExpertJobScript` 的专家职业脚本解析（`0x0896bade`），不能当成道具 `[quest clear]` 实现。A21 的单任务 `[quest clear coupon]` 数据与批量 `[quest clear]` 也是不同机制，不据此补全 70 算法。

## 响应与后续验证边界

70 的 `Dispatcher_StackableActionUse::send`（`0x0825e4ec–0x0825e680`）对错误分支以 CMD `0x0204` 写 `u8 0, u8 error, u8 count`，对结果为零的分支写 `u8 1, u16 slot, u8 count`。`process` 在 Action `run` 返回零后才调用库存 `use_item`，其结果成为响应码（`0x0825e291–0x0825e2fd`）。这只能说明 70 的通用物品动作响应形状；A21 客户端响应格式、任务列表刷新顺序、清标志持久化时点、是否发任务事件或奖励，均无该 ELF 对应证据。

**A21 可实施边界（High，基于上述否定证据与用户提供的文案/帧）：** 当前应将 `10006007` 视为 A21 独有、尚需从 A21 客户端/PVF 与实帧补齐的批量任务清理语义；不能移植 70 的跳跃角色流程，也不能用 70 的 `0x0204` 解码和回包形状。待确定的关键点是 offset 7 的 `77` 是否确为动作枚举、等级用哪一端、grade/职业/转职/前置/当前任务与已清任务的筛选、重复使用结果、奖励和任务完成事件是否完全跳过，以及最终响应/任务列表更新序列。应先做只读 A21 构包与响应 handler 追踪及 PVF 数据交叉核验，再设计事务性清理与协议输出；此报告不作生产实现断言。
