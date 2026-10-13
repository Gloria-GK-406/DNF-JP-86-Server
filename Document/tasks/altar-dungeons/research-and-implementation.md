# 无尽与极限祭坛：查证、实现与边界（2026-10-13）

## 结论与来源

本期为 CurrentImplementation，线格式为 A21 ClientStatic，尚无本期 WireCaptured / ClientVerified。上游及70仅作行为参考。用户对时间限制的补充为“记不清，先按查证结果处理”。

当前PVF：`artifacts/avatar-socket-text-20261011/Script.server.pvf`，SHA-256 `49D4CAFA402DAA93E9C60EFAC8550C20789BB5BD9F99DD7B9235FD0F20666E7A`。
上游来源：`E:/Project/DNF/ServerS4A21/Server/DfoServer` 的 BloodAltar 系列。
70来源：`E:/Project/DNF/Research/1995chen-dnf/other/等级补丁/70/df_game_r`，SHA-256 `cf45f4e8514c3bf560dd322c146f8441249f6c28246ec8566dcf9ebc4e1b2eb7`。
A21 dump 身份沿用 `Document/outputs/special-solo-dungeons-20261011/inputs.json`，SHA-256 `aaae24216e0e260afa528445775e767d104f658fd68fdad21ec7bb601b9c8a3d`。支持EXE SHA-256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。

| 内容 | 无尽11006 | 极限11007 |
|---|---|---|
| 最低 / 基准等级 | 60 / 60 | 65 / 70 |
| 总轮数 | 15，三个房间各5轮 | 7，单房间 |
| MAP内容 | 16348—16354，七种候选，每图5轮 | 17030 |
| 每日次数 | 3 | 3 |
| 本局复活上限 | 5 | 7 |
| 轮中难度 | 固定 | 首尾固定1，中间可选1/2，超时默认1 |
| 疲劳 | 不扣 | 不扣 |

`etc/dungeonetc/dungeonincountinfo.etc` 的两项均为3；`etc/serverparameter.etc [dungeon inout time]`为空，未找到有效固定开放时段。70 `check_dungeon_start` 0859F7FA 经0859F6E2检查开放时间，但目标70样本的 `isDungeonOpen` 082687FC 已被改成各分支返回1，无法还原原服时段。因此当前采用全天开放，显式配置才限制时段；不推测“每小时前十分钟”。日切使用项目已有06:00规则，不能据此宣称70原生日切已确认。70 `IsUseFatigueDungeon` 085BE9C8 对blood非零不扣疲劳。

无尽费用来源：serverparameter等级段、入场次数涨幅与彩票费用。Lv86三次分别22000 / 29200 / 46000；计算为基础12000 × (1+0%/60%/200%) +10000。70 `getfee`0859F75E、`GetAdmissionIncrease`085BEBB6、`GetEnterCount`085BFE88、`ResetDaily`0832A5B6。极限不套用无尽金币费用。

可选配置 `AltarDungeons:OpeningWindows` 为数组，每项 `DungeonId, Opens, Closes`，时间按UTC+8解释，开始包含、结束不包含，支持跨午夜，同起止表示全天；空数组全天。非法配置启动失败，避免静默绕过限制。

## 架构与职责

EndlessAltarDomainService / UltimateAltarDomainService 各持有独立的Run状态，路由Facade不保存业务状态。AltarDungeonEngine共享状态操作，AltarDungeonRuleService只生成波次/奖励和门禁规则，Infrastructure内容源读取PVF，Protocol专门处理CMD、消息编码、截止时间调度及提交后投影。

统一入场依然是判定凭证→消费→建图。金币与UsageQuota在同一业务事务内，计次通过DirectAsync的AltarEntryRecorded处理；持久化复用character_daily_counters，无数据库破坏性变更。登录、加载及日切由同一配额快照生成NOTI0227。

共享Cell新增MechanismBlocksClear：未来波次/选难度/房间未结束时即使活怪清零也不能清图。动态Actor按Run/RoomVersion/CellRevision提交，序号跨房间唯一；普通图默认不受阻塞。最终一波带boss类别，但祭坛不触发普通boss死亡自动抑制其他怪物或普通通关奖励。

0102由队长在UI准备好后发送，不等待每个队员都发送0102。领域拒绝尚未完成当前房间加载的准备请求；若队员还未加载，先记PreparationRequested，由到期检查在全员加载后再发D7并刷怪，无需队长重复0102。旧Run、房间、generation请求不推进。截止时间唤醒器空闲await，波次清空后短间隔检查死亡结果；离开Run后调度不能再产生怪物或奖励。

主动002A放弃按个人结算已完成轮次并退出，剩余队员可继续；0048整队退出须队长校验。两条路径按实例projection gate→全部账户transition gates排序加锁，校验后结算再移除Run，奖励领取状态去重。断线视为放弃尚未结算收益，不提供离线补偿或跨重启续打；已入账不回退。生产Run参与者由PartyService当前成员和ActiveParticipants求交，离开的成员不再阻塞剩余队员加载。

## A21协议及顺序

| 消息 | Body | 静态证据 |
|---|---|---|
| NOTI00D4 | u32 dungeon +8×u8，12B | RVA D74AD0 |
| NOTI00D3 | x,y,seed u32,0,1,mapId **u32**,3×u8空计数，15B | RVA D59ED0，VA115A1BB读u32 |
| CMD0102 | 空；无ACK | builder VA26D4190，host gate全局3B3DFB8 |
| NOTI00D7 | round u8 + interval u32，5B | RVA D473C0 |
| NOTI00D2 | u16 count + count×17B actor +u16尾值 | RVA D47150—D472D9 |
| CMD00FF | u8 count + count×u16序号；无ACK | A21构包及上游死亡列表 |
| NOTI0129 | 提示0+completed u8+timeout u16；超时解选为difficulty u8 | RVA D8C1F0 |
| CMD013D | 请求difficulty u8；成功ACK为1,difficulty | 请求RVA21724F0，成功解码D060F0 |
| NOTI00D0 | 六个u32，24B | RVA D47070 |
| NOTI00D1 | completed,max各u8 +固定4组(count u8 +n×8B itemId/count) | RVA D48A60 |

重要校正：00D2尾值不是毫秒。VA1147299读取后比较0，再经11472BA调用setter1AC8210，将altar对象+194设为(tail==0)；getter26D4120，模式0xA的1914D84—1914E28清房路径受它控制。所有非房间末波必须非零，仅本房间最后一轮最后一批写0。本实现使用1/0。

成功入场ACK→D4→D3→0025加载→0102→D7→D2分批刷怪→00FF或普通0027死亡→下一轮D7 / 极限0129 / 清房→最终D0→8秒D1→8秒经验、金币及ITEMLIST。手动选难度通过013D ACK解锁，只超时额外发0129单字节。退出用既有0048，不发送上游错误的0104[0]：A21的0104实际上要读u16+u32 ITEM_EFFECT；0105也不是退出就绪通知。

入场拒绝：A21金币不足0A（D0B68C，对应字符串61D7）；开放时间外F5（D0B948，字符串9CAA），均保留统一响应中的成员上下文；不能照抄70的F4。

## 奖励及剩余边界

奖励固定后只发放一次，失败保留已完成轮数收益，零轮不发卡；被动物品走InventoryThenMail。每人卡片保留空卡(-1)、金币(0)、物品(正ID)以保证展示和派发一致。经验为完成轮数×对应等级MobReward×PVF进度倍率；无尽再乘本次成功入场序号倍率，极限乘10。70依据085B75D4 / 085B7EA2；不照抄上游只奖励全通的限制。极限当前额外1251/1252概率表为0，不保证获得。

排名历史最佳尚未持久化，相应字段置0表示不可用，不伪造本次结果为历史纪录。D1操作按钮部分VMP路径仍需实机确认。本期没有客户端patch、没有修改PVF、没有预先宣称原生UI或全部多人路径已验证。

## 实机验收列表

1. 重登1123，查看两祭坛各自每日剩余次数；进入一次后刷新、重登仍保留消耗。第4次拒绝，两祭坛次数独立。
2. 无尽15轮：等待刷怪、跨过两个出口、第三房最终轮结束；中间清空一批不能提前开门。费用三次不同、不扣疲劳。
3. 极限7轮：中间分别选两个难度，并至少一次不点等待超时；首尾固定难度；没有重复刷怪/提前通关。
4. 分别检查排名、奖励卡、经验金币及背包；满包时被动奖励通过邮件；重复点击/退出不重复领奖。
5. 中途放弃、死亡耗尽复活、返回城镇、重新进入，旧局不能继续刷怪或覆盖新局。失败的已完成轮数收益正确。
6. 正常普通地下城/大会各跑一次；测试日切前后次数刷新。固定开放时段当前没有配置，无需等待猜测时段。
