# 幸运角色取证输入

开发分支以 `818cbeed7391a9fb6079270a06766fecac2a5ff6` 为基线。A21 证据见 [名单 0406](../../../protocol/game/server-to-client/NOTI-0x0406.md)、[结算 0023](../../../protocol/game/server-to-client/NOTI-0x0023.md#a21-幸运角色本次通关加成)、[燃烧请求 0233](../../../protocol/game/client-to-server/0x0233.md) 和 [燃烧状态 01EB](../../../protocol/game/server-to-client/NOTI-0x01EB.md)。

## A21 PVF

路径 `etc/chn_activatecharacter.etc`。开发挂载 `a97132dd…` 与参照客户端 `8fa275a3…` 的脚本结构和可见值一致。两份均枚举了 14 个基础职业的 144 个职业阶段组合，所列排期时间均为零。角色技能规则 `all skill item` 给全技能组、等级区间 1 至 85 的适用技能加一级，排除 EX 技能。脚本另外定义杀怪经验 `10`、通关 `15`、交任务 `10`、组队 `10`、燃烧 `100`。其 `unique drop ratio` 值为 `50`，界面中文资源 `73061` 文案为神器装备掉落率 `+150%`。配置字段消费语义与界面文案是独立证据。复查可在各 PVF 上用 `PvfProbe dump etc/chn_activatecharacter.etc 850`。

开发挂载的 `etc/chn_fatigueaccelchkdungeon.etc` 在 `[unable dungeon]` 列出了 `3557`。服务端在副本入场读取部署 PVF 的同一规则。

## 参照服

- 本地 `ServerS4A21` 公开参照只命中 `ACTIVATE_CHARACTER` 枚举和旧 `i14` 结算明细注释。旧单体 `E:/Project/86JP` 的同名协议号为 `0x039C`。`70Reference` 范围内无幸运角色同名实现证据。
- `E:/Project/DNF/Research/third-party-s4a21-v3.3/decompiled/DfoServer` 是第三方服反编译实施参考。`LuckyCharacterBodyBuilder` 为指定职业枚举阶段编码 0406，`LuckyCharacterNotifier` 在 0406 之后发送 `USERINFO` 子类型 1 刷新和 01EB；第三方客户端还有同一处理器的刷新补丁。`01EB` 在其实现按 `0` 无资格、`1` 可开、`2` 已开启发送，其 0233 路径只接受值 `0` 并切换状态。参考服握手需要和 A21 原生的开／关构包行为独立验证。
- 第三方 `DungeonKillApplicationService.ApplyParticipantRewardAsync` 向幸运参与者的独立奖励传 `UniqueDropBonusPercent=150`。JP86 按用户确认，将额外抽取应用在队伍共享战利品，每队一次，额外结果仅保留神器装备。官方 A21 服务端原始分配仍为 `Unknown`。
- 第三方 `LuckyCharacterConfiguration` 按北京时间每天 06:00 切换其周排班。JP86 已确认使用 GM 起止时间，该第三方制度仅为行为参考。

## 客户端原生联动

客户端文件登记见工作区 `ClientEvidence/dumps/dnf-dump-20260922-194214-883-23144/manifest.json`。技能重算、燃烧按钮资格和关闭后的重算依序使用 `0406 → 0013 → 01EB`。A21 地址、帧槽与证据级别见本页开头的协议链接。静态链已确定，界面表现待实机。
