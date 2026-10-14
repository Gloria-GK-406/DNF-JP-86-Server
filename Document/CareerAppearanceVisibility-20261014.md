# 职业进阶外观与显示选项

日期：2026-10-14；基准：`2163eefb`；分支：`task/next-round-20261013`。

## 在线进阶后缺失特效

用户报告1123二觉完成后没有三把透明剑的效果，重新登录后恢复。只读数据库显示job=0、packed grow type=33，character_settings.user_state_bits=3。服务日志在任务4730完成后确实发送NOTI0013、CMD0022、NOTI0002 subtype0与subtype1，因此不是未更新职业或完全漏发外观。

旧公开外观编码的最后64字节尾部固定把offset47写0；登录后单独NOTI0165又发送持久化的3。A21 ClientStatic / High：VA013E1A30读取可见性字节，bit1经VA013E1A76调用VA01646DC0更新特效开关；bit3/bit4经VA013E1AF3调用VA019DD810更新全身时装和觉醒装扮。NOTI0165入口VA01162170使用同样两个目标函数（VA011621AB、VA011621F2）。尾部offset47位置与上游UserInfoSubtype0Builder的A21AfterAliveUserStateBitsOffset=47交叉校对。

静态输入：`E:/Project/DNF/DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin`；支持EXE SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。

CurrentImplementation：共用EncodeCurrentUserState的tail+47改为checked byte的CharacterPublicEntryState.VisibilityBits，与NOTI0165同源，保留全部显隐位。转职、一觉、二觉完成后的既有外观刷新自动使用正确值；不增加补丁或额外职业通知，不修改技能结算。相同编码也覆盖穿装备及同场景公开外观。

## 设置位置

ReferenceCode / High：上游Game/Settings/AccountSettings.cs定义三个主设置槽，每槽little-endian u16，非零为开启。00C5外壳为u32长度150，以下偏移从0开始。

| 界面选项 | 主blob槽号 | blob偏移 | 完整00C5 body偏移 | 对应角色可见性位 |
|---|---:|---:|---:|---|
| 转职／觉醒特效 | 1 | 2 | 6 | bit1，1=显示 |
| 查看全身时装 | 55 | 110 | 114 | bit3，1=隐藏 |
| 觉醒装扮 | 74 | 148 | 152 | bit4，1=隐藏 |

后两项与角色位反向：开启时清除对应隐藏位，关闭时置位。位值通过NOTI0002 subtype0 tail+47及NOTI0165传递。01C0的512字节角色blob是另一层载体，不应把其索引109/126/130盲目当成这三个checkbox的权威值；当前1123该三处为FFFF，但主blob三个开关都为1。

最初设置领域服务仅持久化00C5 blob，未发布显隐刷新。按用户随后明确修正的方案，不再新增可见性位写入：00C5 blob作为这三项唯一真源，通过只读访问器解释，协议发包时临时组合可见性字节。旧user_state_bits只提供缺失blob时的回退与未知位，不被本次保存修改。

## 只读blob视图与事件刷新

`ClientAppearanceOptions`持有一次读取的blob副本，三个无参只读属性直接解释u16@2／110／148，`VisibilityBits`是临时协议投影，不持久化。空blob沿用已有角色行为，非空非150B的异常数据显式拒绝，避免误读偏移；非零u16均表示开启。复制输入避免上传缓冲区后续变化影响快照。

`IClientSettingsDomainService.ReadAppearanceAsync`读同一账号的当前blob；SaveMainAsync仍保存原始150B，在有有效selected角色且三项显示语义改变时，把账号ID／角色ID事实`ClientAppearanceSettingsChanged`入队。事件不携带会话或协议对象。账号blob与事件同事务；回滚不会留下通知，选角前上传不触发角色投影，其他选项变化与重复保存不触发显隐刷新。

事件消费时解析当前角色会话并读最新blob，向本人及当前可见的城镇／地下城／塔成员投影NOTI0165。收件人捕获为精确会话；异步读取前后重新检查来源、收件人及场景归属，离线来源不投递，跨区成员不误收。LiveOnly合并以账号／角色为键，消费时以最新快照为准。

CharacterEntryStateDomainService将同一视图投影到Public.VisibilityBits、Public.Settings与Settings.VisibilityBits，因此登录、进阶完成、同屏公开外观及后续完整角色刷新都与0165同源。没有新增数据库列、没有修改01C0 blob，也没有单独持久化三个布尔值。既有原始blob上传／回放格式保持不变。

用户受控差分：三项全开→全关并保存后，150B主blob只在槽1／55／74出现u16 1→0，516B的01C0角色blob完全不变；旧角色位保持3。新只读视图应将该状态投影为25（0x19）。证据：`artifacts/career-appearance-20261014/settings-before.json`与`settings-after-all-off.json`。这是持久化上传差分，不能当作新增修复后的客户端显示验收。

## 验证与边界

首次外观包修复的协议回归1506通过。新增检查覆盖开启／关闭及组合显隐位、只改变tail+47、越界拒绝，以及三种进阶完成后的真实编码。历史完整选角hash先断言新可见性字节，再仅将该明确补丁点归零进行旧基线比较，其余字节继续固定验证。结果见`artifacts/career-appearance-20261014/verification/`。

只读视图／事件接入后：Application 3819通过，Protocol 1515通过，无跳过。覆盖原始blob保持、未修改旧角色位、真实SQLite重载、事务回滚、重复／无关字段不发事件、消费最新状态及合并、离线不投递、本人／同城可见成员同步、跨区不投递、完整角色视图同源、u16非零判断及缺失回退。结果位于`artifacts/settings-visibility-20261014/verification/`。

尚待实机确认在线进阶后即时显示。用户“重登后恢复”只证明旧部署的重登行为，不作为本次修复的ClientVerified。
