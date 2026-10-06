# T-001 协议只读准备结果

来源：独立 Sol6.1 `joust_protocol_resume` 的 A21 静态核对。ClientStatic，不是 WireCaptured/ClientVerified。正式逐协议记录由 T-003 保存。

EXE SHA256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`；PVF SHA256 `3B2FE3DA115FEE949282558A864A319FC9CA197E1D1F22E656E74316A0F04B06`。依据 dump `dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin`，SHA256 `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`，image base00400000；下表地址为RVA。

| Protocol | Body | Static anchor |
|---|---|---|
| CMD04E6/04E8请求 | 14B opaque，无分页字段 | D21A0/D21BE、F4CE1 |
| CMD04E7请求 | 23B：opaque14,u8 KnightIndex,i32 MaterialSlot,i32 Amount，offset14/15/19 | F2B10/F2B4D |
| CMD04E6/04E7 ACK | u8flag1+u32result（0成功） | D2030/D20B0 |
| CMD04E8 ACK成功 | 3505B：flag1+result0+500×(u16 round,u8 winner,f32 odds)，round0 sentinel | D25B0读取DB0、步长7 |
| NOTI04B8 | 3B：u16round+u8state | D2690 ReadBytes3 |
| NOTI04B9 | 90B：u16round+8×11B | D2330 ReadBytes5A、步长0B |
| NOTI04BA | 46B：u16round+i32personalTotal+8×(u8knight+i32personalBet) | D23E0 ReadBytes2E |
| NOTI04BB | 31B：u16round+u8stage+7×(u8winner,u8winnerAux,u8loser,u8loserAux) | D24A0 ReadBytes1F、FCCA0 consumer |

04B9每选手记录 offset0 u8knight（FF无选手，signed消费），1 u8条件选项（首版0），2 f32倍率，6/8 u16参考语义胜/败数（业务意义尚未静态确认），10 u8隐藏/特殊显示flag（D23B3..D23C0消费）。04BB Aux必须显式编码为byte，不能当u16高字节padding。详见 recovery.md 后续修正；当前PVF每骑士Win/Loss为4×7向量，aux0合法。

状态消费 RVA FCBD0：0→scene0，1→scene1+刷新竞猜，2→scene2，3不切scene，4→scene13比赛准备，5按内部进度循环scene5/8/11。实际scene命名、State5时序、条件/四组展示选项及真实服选择算法列为Unknown。04BB Stage0/1/2/3分别累计4/6/7/7场动画所需赛果；Stage3切scene12，最终效果待实机。

日历LinkKey必须 `joust event/0/0`，首段 resolver→窗口1FC；两个斜杠参数必需（01018410/010186B0）。EventId2365是ReferenceImplementation/ServerPolicy。历史每日轮号应由保存的round.Schedule和StartsAt计算，不能从当前配置重算，须非零。

下注复用 IEffectSettlementRequest / IRequestSettlementCommand 和现有物理连接128帧重放账本；opaque14未初始化，不能当请求ID。领域key使用原始transport ordinal的稳定身份，裸ushort序号不能处理wrap。State/History/Bet补推进走Transactional；通知Compose只读，使用独立只读投影。后台事务结束后按round/phase signature发布，Final开始的展示不能触发提前派奖。
