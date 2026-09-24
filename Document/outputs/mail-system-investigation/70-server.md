# 70 级 `df_game_r` 邮件系统静态取证

> 2026-09-24；仅研究，未运行 ELF、数据库或客户端，未修改生产代码。样本：`E:\Project\DNF\Research\1995chen-dnf\other\等级补丁\70\df_game_r`（32 位、有符号 ELF）；本次 `Get-FileHash -Algorithm SHA256` 实测 `CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7`。VA 是该 ELF 的虚拟地址。证据为符号表、函数反汇编及嵌入 SQL；它是 **70 级参考实现**，不是日服 Act.86/A21 协议或业务规则的权威。下文“确认”只指该二进制的静态路径；实际运行、失败恢复、客户端效果仍待验证。

## 范围普查与核心模型

符号范围包含 `Dispatcher_MailBox_Open/Send/Extract`、`Dispatcher_ChangeLetterStat`、`WongWork::CMailBoxHelper`、`Inter_MailBox_Req_*`、`DB_MailBox_Req_*`、`Inter_Monitor_Notify_New_Mail`，以及系统单发、批量发信。核心存储是两个可独立存在的对象：

| 对象 | 直接证据 | 身份与用途 |
| --- | --- | --- |
| `letter` | `InsertLetter` `0x0841f15a` 的 `insert into letter(charac_no,send_charac_no,send_charac_name,letter_text,reg_date,stat)`；`SelectLetter` `0x0841d52c` | `letter_id` 标识正文、发送者、状态。`0x0842602e` 可按 `letter_id` 删除或改状态。 |
| `postal` | `insertMailData` `0x0841f2fa` 的 `insert into postal(...receive_charac_no,...item_id,...gold,...letter_id,...unlimit_flag)`；`DB_MailBox_Req_List::dispatch` `0x0841de4a` 的 `select postal_id,...,letter_id,... from postal` | `postal_id` 标识实物/金币附件；`letter_id` 为正文关联。领取与超时以 `postal_id` 更新 `delete_flag`。 |

两个 SQL 创建步骤先后调用，未在所查函数中看到 begin/commit/rollback；不能据此声称二者创建是原子事务。`insertMailData` 在有正文且未带既有 `letter_id` 时调用 `InsertLetter`，再插入 `postal`；没有附件/金币的分支可在写正文后直接返回（`0x0841f445..0x0841f4f4`）。附件也可带已有 `letter_id`。反过来，`letter_id=0` 的邮包也有表示空间。静态证据尚不足以断定所有失败组合会怎样补偿。

## 协议与处理链（70 样本静态映射）

`InterfacePacketBuf::put_header(kind, command)` 的两个常量可从调用点确认；以下均为该 ELF 的**服务端出站**构包，不等于已实机确认的 86 opcode。入站 read 函数只证明载荷读取顺序，入站 opcode 的分发表尚未逐项核实。

| 功能 | 入口 / 出站构包 | 已确认字段与顺序 |
| --- | --- | --- |
| 打开邮箱/刷新 | `Dispatcher_MailBox_Open::process` `0x081cc196` → `ReqDBLoadMail` `0x085552ac` → `DB_MailBox_Req_List::dispatch` `0x0841de4a` → `Inter_MailBox_Req_List::dispatch_sig` `0x084cf7fa` → `SetMailBoxInfo` `0x085525a6`；`Dispatcher_MailBox_Open::send` `0x081cc2e6` | `0x61` kind 0 列表，`CMail::MakePacket` `0x08551a36` 逐条写邮包信息；打开响应 `0x63` kind 1 写 byte + short；列表加载后另有计数/分段响应。 |
| 新邮件提醒 | `Inter_Monitor_Notify_New_Mail::dispatch_sig` `0x084d017c` | 校验当前角色号后，未加载数加一，`0x63` kind 0 写 `short` 未加载数。只是提醒，具体邮件仍走列表加载。 |
| 领取附件 | `Dispatcher_MailBox_Extract::read` `0x081ccdfa` 读取 `u32`；`process` `0x081cce50` → `GetPackege` `0x085549de` | `GetPackege` 以此 ID 在当前角色邮箱缓存 `GetMail`；成功时先发一个或多个 `0x0E` kind 0 物品更新，然后 `0x62` kind 1 写 byte + `u32` 该 ID，再排队删除 DB 邮包并清缓存。错误走 `0x62` 错误包。这里的 ID 是**邮包/附件 ID**，不可与 `letter_id` 混同。 |
| 正文状态/删除 | `Dispatcher_ChangeLetterStat::read` `0x081ce152` 读取 `s32 letter_id` + `s16 stat`；`process` `0x081ce1fc` → `ReqChangeLetterStat` `0x085568a6` → `DB_ChangeLetterStat::dispatch` `0x0842602e` → `Inter_ChangeLetterStat` `0x084d18ac` | DB 对 `stat=0` 执行 `delete from letter where letter_id=%d`，对 `stat=2/3` 执行 `update letter set stat=%d where letter_id=%d`；回包 `0x89` kind 1。`Inter_ChangeLetterStat` 比对当前角色号与回包角色号。静态证据未证明 DB SQL 本身包含角色归属条件。 |
| 玩家发送 | `Dispatcher_MailBox_Send::read` `0x081cc3da`；`process` `0x081cc958` → 收件人检查/发送资格 → `ReqDBSendNewMail` `0x08555b14` → `DB_MailBox_Req_Send` `0x0841f84e` | 发送错误包使用 `0x61`；收件人检查回包的普通发送分支也以 `0x61` 构包（`0x084cf987`, `0x084cfcd1..0x084cfce6`）。成功回报与完整正文长度/编码字段还需逐项解码。 |

**70 与 86 的编号差异需显式保留。** 本样本 70 服务端构包的常量是：打开 `0x63`（[反汇编](core-asm.txt#L120)，VA `0x081cc33f..0x081cc355`）、领取 `0x62`（[反汇编](core-asm.txt#L2304)，VA `0x08554d8e..0x08554da4`）、普通发信 `0x61`（[默认命令设置](send-asm.txt#L847)及[收件人检查回包](send-asm.txt#L1075)，VA `0x084cf987..0x084cfce6`；[错误包](send-asm.txt#L369)，VA `0x081ccde2`）。这三个值分别比本次并行 86 客户端分析报告所列的 `0x60/0x5F/0x5E` 大 3；它来自 `put_header` 的实参或错误包显式 cmd，并非仅从枚举名推定。**这里只确认 70 出站侧**；86 的入站/出站对应与实际线包应以该客户端报告和后续实帧分别核对，不能做统一的 `opcode ± 3` 全局换算。

列表不是仅按 `letter` 拉取：`DB_MailBox_Req_List::dispatch` 先选 `postal`，保留 `postal_id`、`letter_id`、金钱/物品/时限/特殊物品字段；`SelectLetter` 再读取 `letter_id`、文本及状态，结果拼入 `SIG_MAILBOX_LIST`。`SetMailBoxInfo` 把它们建为 `CMailBox::CMail` 并以邮包键缓存，`CMail::MakePacket` 的首个 `put_int` 写 `CMail` 偏移 0 的键（`0x08551a3d..0x08551a4e`）；`CMail::GetLetterId` 是另一个字段。`SetMailBoxInfo` 的 `0x61` 列表构包调用 `MakePacket`，且跳过待保留/已缓存的 `letter_id` 集合；这足以确认**邮件列表条目可同时携带附件身份与正文关联**，但尚未逐字节标注全包布局。

## 系统投递与持久化

单发入口 `WongWork::CMailBoxHelper::ReqDBSendNewSystemMail` `0x085555e8` 构造 `SIG_MAILBOX_SEND_NEW_MAIL`，调用 `_makeSendMailData` `0x08555756` 后投递 `MsgQueueMgr::put`。DB 侧 `DB_MailBox_Req_Send::dispatch` `0x0841f84e` 调 `insertMailData` `0x0841f2fa`。后者用角色号、发送者名、正文、时间、物品、金币、`letter_id`、`unlimit_flag` 等写 `letter`/`postal`；邮包 SQL 明确存 `receive_charac_no` 与 `letter_id`。`Inter_MailBox_Req_Send::dispatch_sig` `0x084d0130` 调 `CMonitorServerProxy::SendNotifyNewMail` `0x08471072`，在线角色的 monitor 通知进入 `0x084d017c`，再发 `0x63` 未加载数。异步 DB 队列与通知顺序可确认，通知一定发生在持久化提交后的保证仍需更完整的结果码追踪。

批量系统邮件另有 `DB_MailBox_Req_System_Multi_Mail::dispatch` `0x0844346a` → `_InsertLetter` `0x084435d4` → `_InsertPostal` `0x0844372e` → `sendResult` `0x08443b6e`。它与单发使用相同两表，但独立协议结构；不能拿批量路径证明单发事务语义。其他已发现的系统来源含 `eventReward::sendPostalReward` `0x080f0976`、`APSystem::CActionPointManager::GetTodayRewardAndSendMail` `0x08122104`、`EventClassify::CEventActionMng::process_mail_send` `0x0810da1c`，本轮未逐个证明它们的去重策略。

## 领取、校验、删除与到期

领取入口先经 `check_error` `0x081cceea` 检查角色状态、交易和摆摊状态；`GetPackege` `0x085549de` 只从当前用户的 `CMailBox` 取 ID，因此跨角色伪造 ID 在这一缓存路径找不到邮件。它先以 `_DoGetPackage(..., true)` 预检，再以 `false` 执行（`0x08554a64..0x08554adf`）；现金点/金币上限在 `_DoGetPackage` `0x08554650`，硬币上限在 `_DoGetPackageCoin` `0x08553672`，普通物品调用 `tryInsertEventItems`/`tryInsertItemIntoInventory`，随后 `insertItemIntoInventory`（`0x08553936`）；时装和宠物分别走 `tryInsertAvatarIntoInventory`、`tryInsertItemIntoCreature`（`0x0855419c`, `0x0855447c`）。背包不足或上限失败由预检回错误码，不应在设计里简化为“领完再补偿”。

成功路径的可见顺序是：角色背包/金币改变 → 发 `0x0E` 物品变化及 `0x62` 领取结果 → `ReqDBRemoveMail(..., false)` `0x08556772` 投递 DB 队列 → `CMailBox::RemoveMail` `0x08552138` 清缓存。DB 执行 `DB_MailBox_Req_Delete::dispatch` `0x0841f084`，SQL 为 `update postal set delete_flag=%d,receive_time=now() where postal_id=%d`（批量 `IN` 变体亦在函数内）；`false` 对应请求状态值 1。SQL 无 `receive_charac_no` 或 `delete_flag=0` 条件，亦未查到 DB 对背包变更与邮包状态的共同事务；这些是**不能直接继承到新实现的完整性缺口**。本地缓存移除可阻止同会话顺序重复领取，但跨重启、失败重试与并发语义不能由本次静态路径证明。

到期由两路处理。加载时 `DB_MailBox_Req_List::dispatch` 用 `update postal set delete_flag=... where receive_charac_no=... and unlimit_flag=0 and occ_time<subdate(now(),interval ... day) and delete_flag=0`，查询则过滤 `unlimit_flag=1 or occ_time>subdate(now(),interval 15 day)`；`SelectLetter` 会删过期且指定状态的正文，但 `stat=3` 正文仍可纳入列表。在线 `SetPeriodCheckTimer` `0x085523e2` 选择近期到期点，`TimerCheckMailTimeOut` `0x08635d20` 发 `0x62` kind 0 的移除通知；`CheckMailTimeout` `0x08554e90` 跳过 `isUnlimit`，对 `CMail::IsTimeOver`（`0x085574e0`，绝对到期秒 `<= now`）的邮包清缓存并 `ReqDBRemoveMail(..., true)`，该 `true` 对应请求状态值 2。未看到此链路自动创建一封返还寄件人的新邮件；“到期退信”**尚无证据**，不能作为需求默认项。

## 玩家发送与 COD 的取证边界

玩家发送路径在 `Dispatcher_MailBox_Send::process` `0x081cc958` 经 `check_error` `0x081cc636`，收件人检查 `ReqDBCheckReceiver` `0x08555464` 和中间层 `Inter_MailBox_Req_CheckReceiver` `0x084cf95a`，再到 `ReqDBSendNewMail(CUser*, MSG_MAILBOX_SEND*,...)` `0x08555b14`。发送函数查发送者背包槽与物品属性，扣 `use_money`，并按普通物品、时装、宠物分别执行删除/所有权处理，再 `_DoSendNewMail` `0x08555916` 排队建信。可见的是扣除寄件人资产、DB 异步写入以及通知链；**扣资产与建信没有在本轮找到一个跨角色资产/DB 的原子事务**。失败、掉线与重试补偿需另查，设计时不得假定有。

已查关键符号及邮件 SQL 字符串，未发现明确的 COD/货到付款收款链路，也没有发现“超时自动退回寄件人”的专用符号或调用；这是“本轮未证”，不是对整个 ELF 功能缺失的证明。`seal_flag`、`gold` 等字段不可直接解释为 COD 价款。若 86 客户端显示 COD 或退信行为，应以其自身构包/实帧再定。

## 后续与 86/A21 比较时优先核对

1. 先分别标注列表中的 `postal_id` 与 `letter_id`；领取 `u32` 应对应附件身份，正文状态另对应信件身份。核对 86 的实际客户端构包、回包以及无附件正文如何显示。
2. 校验 86 的列表、通知、领取、正文状态 opcode、字段顺序与错误码。本文的 `0x61/0x62/0x63/0x89` 只属于 70 静态构包证据。
3. 决定系统发信幂等键、单次/每日发放边界、过期政策与未领取邮件的处理；70 的 15 天过滤和 `unlimit_flag` 只提供候选机制，不替用户确定业务规则。
4. 设计领取时将“归属 + 未领取 + 背包/金币容量 + 资产入账 + 附件标记”纳入明确原子边界；70 缓存与异步 DB 顺序不足以证明防重。

## 可复核工件

- [mail_elf.py](mail_elf.py)：只读 ELF 符号/调用/反汇编/常量字符串定位脚本。
- [core-calls.txt](core-calls.txt)、[claim-and-send-calls.txt](claim-and-send-calls.txt)：关键调用链。
- [core-asm.txt](core-asm.txt)、[storage-and-wire-asm.txt](storage-and-wire-asm.txt)、[cache-asm.txt](cache-asm.txt)、[send-asm.txt](send-asm.txt)：对应 VA 的逐指令证据。
- [mail-strings.txt](mail-strings.txt)：ELF 内字符串初筛；其中还有其他子系统和历史 SQL，**不能仅据字符串存在判定执行路径**。

置信度说明：上文列 VA 的调用与 SQL 为 **Confirmed（静态）**；“无共同事务”“无退信/COD”只能表述为**所查路径未发现**；运行时是否实际走到该路径、70 客户端效果与 86 兼容性均为 **未验证**。
