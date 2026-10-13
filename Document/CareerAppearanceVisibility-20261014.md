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

当前设置领域服务仅持久化00C5 blob，尚未把上述三个字段映射回character_settings.user_state_bits，也没有基于这种设置变化发布显隐通知。这是独立于本次外观包写0的下一段同步缺口；本次修复尊重已有角色位，不宣称已完成设置字段映射。

## 验证与边界

协议回归1506通过。新增检查覆盖开启／关闭及组合显隐位、只改变tail+47、越界拒绝，以及三种进阶完成后的真实编码。历史完整选角hash先断言新可见性字节，再仅将该明确补丁点归零进行旧基线比较，其余字节继续固定验证。结果见`artifacts/career-appearance-20261014/verification/`。

尚待实机确认在线进阶后即时显示。用户“重登后恢复”只证明旧部署的重登行为，不作为本次修复的ClientVerified。
