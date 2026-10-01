# H13 便携分解机耗尽修复
2026-10-01；基线33e26f34fa454e09b623ed3311e56afdb23f0b27，task/solo-play-roadmap。用户授权：“先解决H13问题”。

## 原因与修复
现场日志首次toolSlot86/providerId0；耗尽后的下一次变成toolSlot-1/providerId0，旧handler直接送入NPC分解命令。修复为无工具且无正数NPC提供者时返回0x13。有效机器来源仍由真实背包/PVF决定，每次独立消费；NPC与便携原有effect/事务/通知保持。

测试新增连续原生请求路径，检查源装备/机器/材料的持久状态；耗尽拒绝后正数NPC请求仍正常，新seq才视为新操作。过期/容量/结算失败回滚、剩余工具批次及原NPC流程继续回归。NPC provider在本次用于合法模式分流，不新增NPC目录或距离判定。

## 自动证据
证据目录：E:/Project/DNF/DNF-JP-86-Server/artifacts/h13-portable-disjoint-fix-20261001。
最初red.trx是缺少测试夹具原始编码器的失败，不作为行为RED；补齐9字节夹具后red-correct-fixture.trx显示第二件装备被删除（期望耐久45，实际null），确认实际缺陷。修复后green-disjoint-final.trx为30通过。中间增加的拒绝请求重传预期依据现有ledger调整为抛InvalidDataException并检查资产未变，未改变重放策略。
整体Release为0警告/0错误，Application完整复验2633通过、Protocol1226通过，共3859（2633+1226）、0失败/跳过。首轮Application另有一项SQLite ObjectDisposedException出现在未改动的消耗品重放用例，保留application-first.log/TRX；原样单项4通过、完整复验全绿，没有改SQLite/消耗品代码，也没有把首轮当成全绿或已确定基线故障。当前源与部署DLL摘要保存于artifact/source.json和publish-manifest.json。发布Dfo*.dll与上述Release输出逐项匹配。复制当前正式配置/PVF引用，在SQLite一致副本和独立端口27001/20010/20011/28080启动检查通过，API ready且48表内容不变，已停止测试进程36156。正式切换等待用户正常退出游戏；切换后的PID、ready时间、数据库备份/差分与接受提交身份记录于同一artifact下deployment.json/runtime.pid，不在本记录自引用提交身份。最终实机仍待用户复验。

## 复验
准备2台同类型便携机和3件可分解装备。第一件成功后剩1台且窗口可继续使用；第二件成功后机器耗尽，窗口应立即关闭，不应需要第三次操作。第三件装备保持；到正常NPC仍可分解。重新登录确认机器数量与产物余额一致。服务端无机器请求拒绝的自动检查继续保留。

## 立即关窗的后续修复与正式部署

用户后续授权：“可以，你来修改一下部署。部署后别忘了给我再发两个分解机，我测试一下看看。”

ClientStatic：本轮只读分析镜像 E:/Project/DNF/DNFClient-A21-debug/DNF.unpacked-b1-debug.exe，SHA256
d7618a102251f1db724f2514c74a4cc855df26e6a4bc308f8ee9f2397db0a99a。普通分解成功 handler 位于
调试镜像0x011376D0（原壳VA0x00D376D0）；0x01137AAE—0x01137ACB检查便携状态和工具存活，
调用窗口关闭槽。0x02350770判定状态0x10—0x13；0x02350780检查工具引用及数量。背包移除工具
时0x016F13AA—0x016F13BB比较并清空窗口工具引用，该路径本身不触发耗尽关窗。此前输出文件
_disjoint_ack_D376D0.txt未平移镜像地址，不能作为该成功handler的证据；本轮直接对上述镜像核对。
用户观察第二次分解后关窗，与旧ACK先于工具清空NOTI的顺序吻合；没有新增实帧归档或运行时读取跟踪。

结算receipt增加PortableToolSlot业务槽，仅用于辨识本次工具；领域消费和effects仍在原事务执行。
协议投影在该工具最终为空时单独发清空NOTI 0x000E，再发成功CMD 0x001A，且工具清空不重复投影。
源装备与奖励绝对状态仍在ACK之后。机器仍有库存和NPC分解保持原顺序，不增加客户端字段。

证据目录：E:/Project/DNF/DNF-JP-86-Server/artifacts/h13-disjoint-window-close-20261001。
新增原始请求路径检查消耗品槽65及快捷槽3的编码后消息顺序、单次工具清空、目标移除和产物余额。
red.trx是夹具实际走显式消息而非Batches导致的无效RED；修正夹具用真实编码器编码有序消息后，
red-correct-fixture.trx为2失败（期望首帧NOTI，实际CMD），修复后分解32项通过。
最终Release全解决方案0警告/0错误；真实PVF完整复验Application2635通过、Protocol1226通过，
共3861、0失败/跳过。保留首轮未设PVF的27失败/184跳过日志，以及真实PVF首轮两项未改动用例的
SQLite ObjectDisposedException和game-login.db清理占用失败；两项隔离复跑通过，再完整复验全绿。
没有修改这两项用例、SQLite或登录代码，不把首轮失败称为已证明的基线故障。

正式发布Dfo*.dll逐项匹配通过检查的Release产物，沿用原appsettings、experience、Resources及PVF。
切换前确认客户端及游戏连接已释放，备份一致SQLite；停止旧PID15484，启动新PID21384，
HTTP bootstrap status=ready，7001/10010/10011/18080全部属于新进程。启动前后47张非sqlite内部表
内容摘要完全一致，quick_check=ok。服务stderr为空；正式部署摘要、DLL/PVF/config/source指纹及备份
在artifact中，最终接受提交身份由deployment.json记录，不在本文件自引用。

部署后为1123（CharacterId2、AccountId4）补发优质的便携式分解机2600548两台，原0台→2台，
主背包槽85、core_uid1111。发放前再次确认游戏未运行，另备份before-grant.db；仅对应items/item_slots
行新增，其余记录逐行保持，核心与槽数量一致且无到期时间，quick_check及foreign_key_check通过。
这两项修复已上线，H13立即关窗和客户端完整效果仍为HumanPending。
