# 两祭坛次数显示与退出虚弱排查

日期：2026-10-13。分支：task/next-round-20261013，源码基线9c266a9f。
本次只读排查，未修改运行代码、数据库或部署。

后续修复状态：用户授权修复部署后，角色状态加载已由祭坛领域服务填充当前配额，移除同键旧条目；Run通过统一的AppliesFailureWeakness政策处理主动退出及全灭，祭坛免新增虚弱。原始排查结论保留如下；修复后客户端验收尚未完成。

## 次数：正确计数被角色信息中的默认值覆盖

当前运行库只读结果：1123（character_id=2）当日无尽11006已用1次、极限11007已用3次，真实剩余分别2和0。character_dimensions中不存在这两个键的记录。

- CurrentImplementation：AltarDungeonEngine.ReadQuotaAsync读取UsageQuota持久化；UsageQuotaProjection按剩余数发送NOTI0227（u32 dungeonId + u8 remaining）。登录、入场、加载及日切有投递路径。
- CurrentImplementation：StateSynchronizationPayloadCodec.Entry.cs的SubtypeOneDimensions仍包含(11006,3)、(11007,3)。WriteSubtypeOneDimensionTail在extras.Dimensions没有记录时写默认值，与UsageQuota不一致。
- CurrentImplementation：打开选图时发送ExtendedUserStateMessage（NOTI0002 subtype1）；仅补发绝望塔的0227，未在此序列中恢复祭坛真实次数。最新日志20:29:40附近可见该流程。
- ClientStatic：A21 VA013E1D60解析角色信息中的u8计数及N×(u32键,u8值1,u8值2)，在013E1DBD将值2传给01870AD0，写入角色+5E90字典。0227在011643B2也调用同一setter。
- ClientStatic：选图次数文本在00752F95调用01870AF0读同一字典，00752FD7以后格式化剩余数/上限。无尽入口02624783也读取11006对应值。

结论：不是扣次失败，也不是必须更换NOTI类型；角色完整状态和配额增量通知不共享数据真源，后发的默认3覆盖已同步值。客户端实际显示由用户报告，覆盖因果由代码和静态链支撑，尚未作修复后实机验证。

建议：构建角色状态快照时由配额服务填充祭坛剩余值，再统一用于subtype1；避免编码器读库或另建第二套持久化。选图状态序列也应在UI初始化前携带最新配额。只补发0227可以暂时恢复，但不能消除其他subtype1路径的覆盖风险。

## 虚弱：漏掉祭坛放弃惩罚豁免

- CurrentImplementation：DungeonRunDomainService.EndAsync用当前Cell.ClearOpen判失败，失败即SetWeakness；TryEndPartyWipeAsync对结束成员也设置虚弱。没有祭坛豁免。
- PVF：11006的[blood dungeon]=1，11007=2。
- 70Reference：CDungeon::get_blood_dungeon_type（0826B928）读取+089F。CParty::giveup_game（085B2BAA）入口085B2BEA读取同一标记，非零设置免罚变量；085B2F50、085B323B以及后续离队分支均据此跳过CUser::giveup_panalty。
- 70Reference：CUser::giveup_panalty（086786BE）才是设置stamina=10并发送NOTI0021的路径。BloodHandleDieUser（085B8E5C）先处理死亡状态及全灭结算定时，没有直接调用该惩罚函数。

结论：70参考支持两祭坛主动放弃不新增虚弱；当前通用退出判定缺失玩法政策，现有加虚弱行为不一致。死亡/全灭需随退出政策一起覆盖测试，不能仅修CMD002A。已有虚弱应保留，免罚不等于免费清除已有虚弱。

建议：在地下城规则/Run快照中明确退出惩罚政策，统一由End及全灭路径读取；祭坛免罚，普通地下城沿用现有规则。通关回城也要确保不因Cell状态推断错误而新增虚弱。

## 静态证据身份

- A21 dump：DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin；SHA256 AAAE24216E0E260AFA528445775E767D104F658FD68FDAD21EC7BB601B9C8A3D。文中客户端地址均为VA（RVA+00400000）。
- 70 ELF：Research/1995chen-dnf/other/等级补丁/70/df_game_r；SHA256 CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7。
- 上游回城协调器未发现可供对照的完整祭坛虚弱政策，未将其作为正确性依据。
