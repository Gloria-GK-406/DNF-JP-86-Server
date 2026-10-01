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

## H13 通过与 H15 限购修复

用户后续反馈：“H13 ok，我测试H15时候，尝试购买光明之心礼包，失败了。”因此 H13 在 749eb658 版本上记为 ClientVerified。

H15 现场日志在 20:39:18 显示商品 101236 因 `PurchaseRestriction: Unimplemented catalog purchase restriction` 拒绝；此时未进入结算。完成支持后的人工效果仍待复验，不能由自动购买测试提升为 ClientVerified。用户随后授权“你部署吧，然后给我一个待验收的新表格”。

新表见 [第二轮待验收表](../human-test-list-20261001-round2.md)，实现、自动证据及历史规则边界见 [H15 记录](../../../Verification/H15-cera-restrictions-20261001.md)。H10、H11、H12 等原有未完成边界保持。

## H14 通过与 H16 满包提示修订

用户反馈：“H14通过，client有检查 H16 单次多次都ok，但是在满了以后开显示正在适用防沉迷系统的限制。”H14 记 ClientVerified（缺材料由 client 检查，未据此声称非法请求服务端实机路径）；H16 单次／批量成功通过，满包提示待复验。

用户授权：“先修改不需要部署，我继续测试”。当前修复仅改魔盒 CMD00D0／0450 的 NoRoom 失败字节 7E→02，协议自动检查通过，未切换运行服务。来源、回归结果及部署边界见 [H16 修复记录](../../../Verification/H16-magic-box-no-room-20261001.md)。

## 后续验收反馈与 H18 / H19 / H11 / 地下城出售诊断

用户确认 H17、H20、H22经验、H23、H24通过；H21后续再验。H11仍不能转换属性，地下城结束后的商店出售失败。相关问题未作为已完成处理。

用户要求将疲劳改为1点，以继续H18；已为1123保留ConsumedToday=28、将Used28→187，总量188，剩余1。原子事务及全部其它业务行保持校验通过，随后独立连接重读一致；运行日志21:44:37已发FatigueNotification。无服务切换或新增物品。

H19先前发的10006007还在Main109，数量1、实例ExpireTime=0。实际运行客户端为protocol2，其PVF与服务端／另外两客户端不同，保留[expiration date] 2015-09-22 06:00:00。先前只验证实例期限不足以证明客户端可用；待游戏退出后同步PVF，已有券可继续验收，无需再发一张。

H11当前A21构包点RVA12EF0E3 push0，12EF0EC WriteI16：第二字段固定0；服务端MagicSealHandler要求MaterialSlot=-1，因此Change请求会在发领域命令前拒绝。现场连续发ChangeRandomOptionResponse，未见对应MagicSealCommand。不是词条生成或effect未保存；尚未修改此实现。

21:40:18.737出售现场：NPC1000/Main43/数量1被ShopUnavailable拒绝，回CMD0016。此前Eplp state2 option2未离开地下城。ItemSaleDomainService要求城镇成员；同时PVF德利拉角色[simple shop]1000、[visible on dungeon clear]1，PvfNpcShopDefinitionSource只识别[item shop]，故有两个拒绝原因。后续修复需以当前角色已通关的地下城场景及明确商店资格放行，不能取消全部场景检查。NPC1000的真实商店为EquipmentShop_dungeon1.shp；尚未修改出售实现。

数据回执、备份和诊断摘要：E:/Project/DNF/DNF-JP-86-Server/artifacts/h18-fatigue-one-20261001/（before.db、receipt.json、diagnosis.json）。H16修复558b8d6b仍未部署，服务PID18764保持。
