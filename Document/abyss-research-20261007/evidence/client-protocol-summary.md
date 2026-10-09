# 客户端协议裁决索引

所有RVA绑定本轮PE SHA256 `CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14`，ImageBase00400000。Static消费闭合不等于WireCaptured，所有真实深渊时序、展示验收仍Unknown。

| 方向/opcode | 静态读取/写入闭合 | 关键RVA | 已确认条件/名称 | 未闭合 |
|---|---|---|---|---|
| NOTI001B | 前8B两个u32；后两个u8计数+各u16数组 | D90202,D90358,D90470,D904EF | 首u32与00CF共享global | 全包37case、列表深渊权限语义 |
| NOTI001C | u32+4u8、2u8、u8、groupCount与嵌套pair数组 | D51F68,D523AE,D524E2,D52522 | 结构可变，不能称固定32B | hell坐标业务名、条件尾部 |
| NOTI001D | 3u8+u32+u8+u8+u32+u8=前14B | D57C85,D57CF8,D57D10,D57D34 | offset7/8存不同global | offset7枚举及默认值、actor/item尾部 |
| NOTI00CF | 4B u32 | D4B910 | 队伍人数<=1则立即清0 | 单位、概率、server时序 |
| NOTI02A7 | 4+8×count B | D483C0,134B4A0 | 日志hell index/level；正值键值表 | index→怪物/组映射 |
| NOTI030E | 0B消费 | D48580 | manager存在时事件27(0,0,1,1) | wire尾部与清场时序 |
| NOTI0431 | 8B，2u32 | 570010,56DD70 | booster字段+360/+358 | 单位、上下限、活动门 |
| NOTI049C | 32B | 1E0780,1E01E0 | ensure UI35D并刷新 | 8字段业务含义 |
| C2S CMD0010 | 15B普通路径 | 232DF3A | moduleMode/derivedMode都是byte | +3EC/+3F8状态写入语义 |
| C2S CMD00E4 | 0B | 15F50A0,2B943F1 | native CREATE_GUILD_AGIT | 无理由改名HELLPARTY_START |
| C2S CMD00EB | 5B u16/u16/u8 | 12F82C4..12F8363,2B9445A | native REQUEST_DISJOINT_ITEM | 无A21另一个深渊writer证据 |
| C2S CMD0413 | raw-copy18B，业务u32@+14 | 233BB5B,233BBFD,234CED0 | 状态变化且活动key86F gate | wire正文18/4、首14B来源 |
| S2C CMD0413 | 公共success分支后12B | D05B10 | result13/16/B3映射string5726/5727/5728 | 错误文字与server条件 |
| C2S CMD04C3 | raw-copy18B，index u32@+14 | 1E0290,234CED0 | event D、所有checked依次发送 | wire正文18/4、首14B来源 |
| S2C CMD04C3 | 公共success分支后4B | 1E0190 | ensure UI35D | result业务语义 |

长度证据见client-basic-noti/client-booster-friend/client-cmd-toggle-numbering；原生名称按opcode绑定见client-native-enum-binding；公共发送头/计数边界见client-send-boundary。大量完整asm保留工作区artifacts/abyss-client，不提交。
