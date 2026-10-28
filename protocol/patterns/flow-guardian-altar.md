# 守护者祭坛原生流程

适用A21，DGN3511–3523。当前为ClientStatic、70Reference与CurrentImplementation交叉映射；
没有本任务WireCaptured或ClientVerified。输入哈希、原始摘录及领域规则见
[实现说明](../../Document/tasks/guardian-altar/README.md)。这里维护协议编排，单包字段以链接页为准。

1. 选角与活动在线更新发送[033F](../game/server-to-client/NOTI-0x033F.md#守护者祭坛通用调度)，
   类别4／内容10，打开state1、关闭state0。原生底栏和主菜单进入点发送空体[0392](../game/client-to-server/0x0392.md)。
2. 0392返回业务result及完整配置。服务端同时记录精确会话的祭坛选关状态和Interactive意图。
   不通过普通选关入口绕过50级、单人、城镇和活动开关检查。
3. 客户端用0010选择3511–3523，服务端以祭坛独立进度校验关卡和难度；成功使用既有选关前导，
   然后发送[035B](../game/server-to-client/NOTI-0x035B.md)、[035C](../game/server-to-client/NOTI-0x035C.md)（普通关卡round=1，基准等级来自关卡难度配置），
   不再发送普通001D。0025加载完成后走既有ACK／001E，再启动祭坛时间线。
4. 敌方生成发送[035D](../game/server-to-client/NOTI-0x035D.md)；客户端0027报告已有actor死亡，
   服务端只确认该本局actor，不套用普通怪物经验／掉落／清房结算。
5. [0395](../game/client-to-server/0x0395.md)升级能量，[0396](../game/client-to-server/0x0396.md)召唤，
   响应纠正服务端能量。友军由0396成功分支执行本地STK效果，不额外发送敌方生成包。
   [0398](../game/client-to-server/0x0398.md)暂停冻结时间线、能量与冷却，恢复后重算期限。
6. 中心存活、全部阶段及待生成项结束、敌方已清空后结算；先提交实际奖励、星数、次数和防重记录，
   再发[0361](../game/server-to-client/NOTI-0x0361.md)及真实背包变化。成功领取每轮奖励扣一次，失败不扣。
   普通终局及生存最终结束再发送[0360](../game/server-to-client/NOTI-0x0360.md)。
   生存中间轮只更新035C时间线，保留当前地图、中心和能量，不重新Loading。
7. 结果操作复用0048／退出链；自动回城为结果后50秒，旧run／session／generation的回调被拒绝。
   主动放弃、结果回城和自动回城在进入城镇前从当前角色事实补发Main完整库存，恢复祭坛替换的物品快捷栏。
   不修改原库存位置或快捷栏持久化设置。再次选择从原生0392重新进入。加载超时、断线和主动退出不冒充通关。
8. 大厅升级[0393](../game/client-to-server/0x0393.md)、装卸[0394](../game/client-to-server/0x0394.md)、
   交换[0397](../game/client-to-server/0x0397.md)、成就[0399](../game/client-to-server/0x0399.md)、
   金币洗星[039A](../game/client-to-server/0x039A.md)各返回同type的CMD；洗星还发送
   [0362](../game/server-to-client/NOTI-0x0362.md)完整配置及金币变化，成就发送真实物品变化。

NOTI035E当前没有构造：A21所见handler未取得明确取字段证据。035F的70测试函数为空操作，
不把它当每秒能量同步的必要包。0392配置头offset28的4B仍为Unknown；静态读取闭合不代表最终UI已验收。
