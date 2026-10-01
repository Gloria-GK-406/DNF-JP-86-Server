# 2026-10-01 effect 重构实机验收进展

测试版本：`a0e916c0`，运行PID3336。用户反馈原文：

> H01，H02通过，H03 client有自己的冷却约束检查，无法触发非法流程，H04，H05，H06，H07正常，H08缺少强化保护券。需要你各提供两个了

H01、H02、H04、H05、H06、H07按用户实测反馈记通过。H03只确认客户端冷却门，未捕获服务端冷却内拒绝实帧；服务端路径保留自动回归证据，不自耗分项尚未反馈，因此记Partial。H08仍待实测，不能以补给视为通过。其余项保持HumanPending。

## H08补给

补给时间：2026-10-01T18:38:56.477839+08:00。目标角色1123 / Character2 / Account4。依据本轮“各提供两个”及此前“直接加，加完了，我频道登录一下”的授权，在当前在线SQLite新增两种券各2张，复用已验证82字节材料core原型（type3），实例期限0。

| 物品 | 模板 | 之前 | 新增 | 发放后 | Main槽 | core UID |
|---|---:|---:|---:|---:|---:|---:|
| 装备强化保护券 | 7804 | 0 | 2 | 2 | 140 | 1053 |
| 装备强化超级保护券 | 10007840 | 0 | 2 | 2 | 141 | 1054 |

发放前通过SQLite backup取得before-grant.db；BEGIN IMMEDIATE内部验证角色身份、空槽和完整差异，只有4条新items/item_slots记录，全部其他行保持。quick_check=ok、foreign_key_check无异常；提交后独立只读连接复核槽数量/core数量均2、期限均0。在线缓存尚需重新进频道刷新；没有发送即时NOTI或重启服务。

原始脚本、备份与回执：`E:/Project/DNF/DNF-JP-86-Server/artifacts/effects-h08-protection-two-each-20261001/`。receipt/backup存在时脚本拒绝重复发放。

两券共存时原规则优先超级券；H08需继续实际失败保护及无保护损毁分支。源码、概率和既有保护规则未修改。

## 固定等级券版本上的后续验收反馈

测试版本33e26f34，运行PID15484。用户反馈H08/H09通过；H10缺红字装备样本；H11宝珠附魔、魔法封印解封通过，赛丽亚处转换属性失败；H12确认client自带非武器过滤；H13只剩一台便携分解机仍可分解两次。H08/H09记ClientVerified，H11/H12分项记Partial，H10缺样本与H13待复验。

H13运行日志：19:25:11.204 target Main/33、toolSlot86、providerId0进入PortableDisjointItemCommand；19:25:23.965 target Main/25、toolSlot-1、providerId0却进入NpcDisjointItemCommand并成功。这是解码后的现场日志，未新增归档原始帧，不将其标为WireCaptured。修复和复验条件见[H13记录](../../../Verification/H13-portable-disjoint-20261001.md)。

后续客户端静态调查确认：成功ACK末尾检查便携工具耗尽并关窗，旧顺序ACK先于工具清空NOTI导致
迟一次检查。两项H13修复现已部署PID21384，最后工具的清空NOTI提前到ACK之前，装备/产物刷新
仍在其后；1123已补发两台优质便携分解机。自动回归通过，立即关窗与完整客户端效果仍HumanPending。
