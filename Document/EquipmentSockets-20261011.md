# 装备与时装钻孔、徽章镶嵌

日期：2026-10-11；基准：b9470093，工作分支 task/monster-declaration-parser。

## 职责与数据

`ItemSocketHandler → ItemSocketCommand → IItemSocketDomainService → 装备/时装实例 + IItemSocketRuleService → PVF`。
领域服务校验精确在线会话、目标模板/所在容器、锁定/到期、全部材料堆和批内孔位；规则服务接显式快照与时钟，读取PVF布局、材料类型和颜色。全部通过才修改实例并扣料，SQLite同事务提交，保留UID；失败回滚全部。成功经EventService投影ACK及NOTI000E，重复请求只恢复库存。

装备沿用 EmblemSocketCount/EmblemId1/EmblemId2，空孔-1；时装沿用AvatarDetail.JewelSocket的30B存储，孔色非0且徽章0为空孔，通用投影最多3孔/18B。时装变化保留属性、染色、期限和透明装扮外观，不经容器移动、重建或换UID。覆盖旧徽章不返还。超过可投影孔数、未知布局/颜色均拒绝。

## 协议依据

[037A装备开孔](../protocol/game/client-to-server/0x037A.md)、[0379装备镶嵌](../protocol/game/client-to-server/0x0379.md)、[00CE时装开孔](../protocol/game/client-to-server/0x00CE.md)、[00C9时装镶嵌](../protocol/game/client-to-server/0x00C9.md)记载当前人工结论与对应asm证据。固定客户端SHA-256：1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD；dump：DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin。

关键区别：0379没有列表字节；00C9有。开孔成功只回目标槽和材料槽，镶嵌成功回材料槽及绝对剩余数量。上游成功体不能直接沿用。开孔ACK本地扣一，因此先ACK再权威库存；失败00+原因。可兼容既有4B请求校验和，但本次没有采到此操作的实帧。

## 当前边界和实测集

PVF提供装备部位孔色、时装布局与徽章适用颜色；无action的四个装备钻孔器使用有出处的兼容白名单，固定到期变体受日期限制。时装开孔器action附带数值/grade与目标档次关系尚未闭合，单孔辅助装备孔号0/1别名来自上游兼容；这两点不称ClientVerified。

实机建议按以下顺序：

1. 背包上衣使用开孔器，确认双孔、材料只扣一，重开拒绝。
2. 绿色徽章分别/同批填两孔，核对数量与属性；覆盖一孔不返还旧徽章。
3. 用颜色不符或数量不足的整批操作，确认目标及所有材料均未变化。
4. 时装开孔、镶嵌，检查三孔/铂金孔布局、透明外观与染色保留；穿戴栏时装镶嵌另测。
5. 重登后核对孔和徽章持久化；装备/时装切换与穿脱确认UI展示。

服务端钻孔/镶嵌逻辑不要求新增客户端补丁。用户已实机确认7421可以给装扮开孔；徽章实际属性等其余场景仍按上面的测试集验收。自动化结果与部署身份在发布收据中记录。

## 7421道具说明

PVF的 `stackable/event/emblem/emblem_drill.stk` 原先在 `[explain]` 的普通QuotedString中直接保存 `explain_7421`，并非运行时缺少外部STR键。名称为「镶孔生成器」；说明为「使用该道具后，可选择一件装扮并开启镶嵌栏。」。类型仍为 `[add avatar socket] 2`，不修改道具行为。

`Tool/PvfAvatarSocketTextPatch` 接收源PVF和一个不存在的输出路径：

```text
dotnet run --project Tool/PvfAvatarSocketTextPatch -- <source.pvf> <new.pvf>
```

工具仅接受该占位文本，追加UTF-16字符串和一个脚本数据块，仅重定向这一文件；原有字符串偏移、其余文件表记录和既有数据块保持不变。输出旁的JSON记录前后SHA-256和完整性检查结果。不原地修改、不覆盖现有输出；对已修补输入直接拒绝。不同环境必须分别以各自PVF为源，避免覆盖已有的商店和地图改动。部署前正常退出客户端，保留原PVF或切换前的服务端资源路径，并重启服务以清除内容缓存。
