# CMD `0x03F3` `WEDDING_CHARAC`

## 2026-10-17 等级与职业纠正

ClientStatic / Confirmed：成功正文@43为等级，@44为职业，原先人工语义及实现写反。00892D20将raw42写GameCharacterInfo加密等级+14；00892DCE将raw43写job+4，13A6E31..13A6E3D用+4进行职业换装查询。正文含ok信封，故raw偏移加1。已改编码并加入骑士job12/level85回归；GrowType及13项部件不变。原生模型显示仍待验收。


2026-10-16；A21 ClientStatic / Confirmed，CurrentImplementation；无本轮完整正常实帧，原生显示待验收。

handler RVA00892C60在结果信封非零时精确读取97B，线上正文98B：

| 正文Offset | 大小 | 类型 | 用途 |
|---:|---:|---|---|
| 0 | 1 | u8 | 结果信封，成功1 |
| 1 | 4 | i32 | 被读取但未使用，本期0 |
| 5 | 4 | u32 | 对象CharacterId，与manager+0x5c比对 |
| 9 | 4 | i32 | 被读取但未使用，本期0 |
| 13 | 30 | bytes | 对象名字，零结尾固定C字符串 |
| 43 | 1 | i8 | Level |
| 44 | 1 | i8 | Job |
| 45 | 1 | u8 | 编码GrowType，经RVA015DAFB0（VA019DAFB0）保存 |
| 46 | 52 | 13×i32 | 外观槽0..12模板ID，空槽必须为-1 |

失败正文为`00 01`；handler在信封为0时跳过成功结构。名字沿用现有UTF-8 wire字节，最多29B并保留完整字符与终止零。对象资料读取持久数据，允许对象离线；无绑定或目标不匹配不返回其他角色资料。

12:32客户端崩溃补证：空槽0会进入资源查找，NULL+0xE58导致RVA0138B903访问0xE60；RVA013973A0只对-1绕过查找。CurrentImplementation已将空槽编码改为-1，其他字段和98B长度不变。详见[故障证据与补正](../../../Document/MarriageEngagementCrashFix-20261016.md)，修复后原生登录待复测。

该CharacterId不是在线WireUserId。两项未使用DWORD不擅自命名为关系状态/时间。与[03D0状态](NOTI-0x03D0.md)和[C2S查询](../client-to-server/0x03F3.md)配套。

证据：[RVA00892C60读取链及GrowType helper](../../../Document/outputs/marriage-engagement-20261016/client-evidence.txt)，镜像SHA256 `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`，客户端SHA256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。
