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
仅保留1台便携机，准备2件可分解装备；在同一窗口先分解第一件，再尝试第二件。第一件成功并消费最后机器，第二件应拒绝、装备和已有材料保持。再次进窗口/重登确认一致；到正常NPC分解第二件仍应成功。有2台机器时各分解1件应消耗2台。
