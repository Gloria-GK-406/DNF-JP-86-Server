---
kind: rks-verification
decision: pass
---

# TASK-49 核验摘要

判定仅覆盖两个退出消息、三条 fine 五行及当前有效全量无新增失败，待唯一 fresh Astra 结果快审和主控接受。产品基线 `b0c4343e`；不关闭 T-007／T-008 或 Spec B。

## 交付和直接证据

- GiveUpGame／EplpCommand 各唯一 Protocol／Direct whole，消息 handler 只交总线。普通 End 仍零 unit／事务；原 stale、InWorld、leader／cohort、entry intent／town ownership、取消卡牌、移除 participant、XO-2 peers→caller ACK→Town 位置保持。GiveUp 无 run 不离队、weakness 一次及原顺序由原强例守护。
- Boss 选择一段 ordinary Write，option2 ACK 后逐成员独立 Leave，保 None 取消、全 selected 和各单元报告者 physical，Leave 的 DeclaredNonRequest owners 与保护集合独立；Illusion 选择仍一个 ordinary `[]`／None。GiveUp 直接复用 TASK-39 真实两塔／队伍／返城阶段，不调用 fine／另一 CMD／bus，也不注入旧 coordinator／delivery。三目标 fine 类型／注册／生产 caller 清零，其余两塔 Leave／Start／Synchronize、AltarAction／Timer 保留。
- 祭坛直接调用现有领域结算，cohort 仍由 registry／InWorld／同 run 重新取得；一个 ordinary Write、不传 footprint。新旧共用收件人筛选、实际投影、typed 合成、日志与 Wake 算法；legacy 保投影前捕获的同一份 recipients 逐 null physical Block，新 whole 经显式已提交单元默认保护。投影时钟仍在 recipients>0 的受保护 try 内。
- 正式 16 oracle＝普通4＋两塔8＋祭坛4，在最终旧、新均通过。真实 decoder→dispatcher／bus／DI scope、SQLite／actual encoder／production outbox／受控 physical，直接核声明、Completion、cause、run／registry、帧和关闭；普通 EPLP 代表例另核实际 root／source／close cause 链，whole 的精确 Dispatch physical／Type0x48／Ordinal7／RequestClosed。旧 fine 不暴露 ProtocolRequest，不给旧侧填假值。祭坛 SQL trigger 的真实30exp／17gold回滚后换新 physical 重试只支付一次；双人投影失败核精确 Fence／physical Block 和原合成 cause。测试规则和物理发送器受控，不冒充真实 PVF 奖励或 A21。
- VC-B25～27 只列实际差异：普通 EPLP scope 从 End 后、移除／ACK 前变为 whole 末尾；Boss option2 whole scope 已完成选择和逐次离场，Illusion scope 已登记 ACK；祭坛报告者 physical 随已提交单元默认 Block。scope／默认保护与本次 whole 归并不可分，纯准备测试可独立提交，不扩 raw／Main／Self。

## 正式核验

最终旧源 `logs/task49-old-final-r3`：Application **202/202＝186原强例＋16新 oracle**、Protocol **119/119**、Networking **2/2**。主控执行安全复验；固定31路径 manifest、完整当前 binary recovery patch、HEAD／branch／新鲜等价及 reverse-check 预检后逐项 finally 恢复，`restore.json` 为 true／errors0／mismatches0。最终纯准备为 `/private/tmp/task49-pure-preparation-r2.patch`，规范路径 `/private/tmp/task49-pure-preparation.patch` 为同内容，reverse-check 通过；初版另留 initial 文件，全部恢复材料保留。

当前非增量 Release build（`logs/task49-final-r1/build`）**0警告／0错误**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental -m:1`。正式 test 保 `-c Release -m:1 --blame-hang-timeout 5m`、detailed console／独立 TRX／DFO，项目严格串行、原类并行及 skip 不变；每次 `run.json` 含实际 argv／cwd／HEAD／branch／UTC start-end／exit。最终旧源恢复后 App r3 重编译当前 DLL；`task49-restored-current/Protocol` **119/119** 另恢复当前测试 DLL。

| 项目／有效目录 | 结果 | 相对48 display＋status＋重数 |
|---|---:|---:|
| Application／task49-final-r3 | 5637＝5619 pass＋2原 Dimension＋16 skip | N16／D0，全为本 Task oracle |
| Protocol／task49-final-r1 | 1786 pass | N0／D0 |
| Networking、Host／task49-final-r2 | 各203 pass | 各N0／D0 |

四 fresh 合计 **7829＝7811 pass＋2原失败＋16 skip**。其余四项目产品未受影响，沿用48有效 r2：Persistence484、Infrastructure976＝974pass＋2skip、HttpApi57、Pvf80；有效总库存 **9426＝9406 pass＋2原失败＋18 skip**，未称八项目 fresh。两失败仅 `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` 与 `All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，仍7100／maze0／(1,0)／Left缺图。逐例差集及全部正式 runner 无重叠在 `logs/task49-final-r3/comparison.json`。

原 DFO parser 精确通过：四 fresh **71＝67反例＋4 control**（App62／Protocol1／Net7／Host1），其余三旧 control 保持，有效合计74＝67＋7；outside／exe／orphan0，未增 expected-negative。实际模块 capture 不 build 游戏 provider：`evidence/task49/runtime/task49-route-check.json` 为两唯一 Protocol／Direct、三旧注册0；`task49-types.json` 从实际 Protocol assembly 反射三旧类型0，生产源码引用0。五行外、全部 original 和行序保持；Result 后 strict／八坏例及含新增文件 diff-check 通过。

## 限制及诊断

首轮 Net 与 Host 各一连接拒绝已保留在 `task49-final-r1`：Net 在首次连接前有相邻 IGameTcpService bind SocketException48／Address already in use；Host 已过 HTTP／Channel、首 Game 连接拒绝，缺子进程输出，未确认同根。保持源码／并行／5m／skip，两原例各一次复跑通过，再两项目 full r2 各203/203；不把首轮失败并入原 Dimension，也不称已修复端口问题。前期新增断言的材料／party roster／余额上限和各单元 reporter／重试 physical 假设错误只修测试夹具，未改变产品迎合测试。

未运行真实游戏、部署、提交、推送、PR、tag、issue 或清理。Task 局部完成不等于 Spec 封存；受控故障与 transport 不是自然故障或 A21 验收。所有 runner 已结束，产品和测试停写，交唯一 fresh Astra。
