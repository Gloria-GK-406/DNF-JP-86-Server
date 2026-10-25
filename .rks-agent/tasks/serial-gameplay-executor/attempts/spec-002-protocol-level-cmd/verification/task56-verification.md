---
kind: rks-verification
decision: pass
---

# TASK-56 验证

本判定仅覆盖十五个轻量消息与 QueryShopPurchaseCounts 一 fine，共十六 ledger 行及无新增回归，待唯一 fresh Astra 结果快审和主控接受。T-007 迁移范围在此收口，不据此封存 Spec B；父提交 `7275969e`，执行 round 1。

## 正式旧新表征

- `task56-prep-old-r6/Application`：80/80；`task56-prep-old-r1/Protocol`：76/76。旧源码上的纯测试准备独立保存在 `verification/evidence/task56/pure-preparation.patch`。r1/r2/r5 是测试编译修正，r3 是测试误以为 Select 同一 session 会重置阶段；源码明确保阶段，改为 ReleaseExact 后 Select，r4 79/79，加入冷 DI／时钟次序后 r6 80/80。
- `task56-new-focus-r1/Application`：同80/80。全新 `LightweightProtocolCommandFlowTests` 46例，十五真实 decoder／dispatcher 各有／无在线归属，生产 encoder／outbox，完整 body、原类别语义日志和 no-frame；保持 Creature script 字符归属、Story selected-not-world、Shop 有查询无投影与 session→online→clock→PurchaseCounts→IsInWorld。
- 实际 scope.Dispose 注错仅 Contents 与 Shop 两入口；Shop 原 fine 对新 whole：同原 cause，旧无帧，新响应登记后按 send→close，均无事务／typed／Fence（VC-B36）。七个有响应入口逐一注首次实际 encoder，Shop Project Failed 忽略，其余同原 cause 精确来源关闭；same-ID 不同 physical 替换不误关。日志异常实测 ClientSpecStatistic，领域查询异常实测 Shop；没有写，不适用 SQL／native Commit 故障。
- 原硬件三组与 ExitFlush 的 metrics／Roster 强断言保留，改真实 dispatcher；Roster 原双 CMD 数提升为生产编码后的两帧完整 selector／type／00 00 body。ShopFixture.Query 改实际 decoder，原真实 SQLite 的跨角色／跨天／累计计数断言保留。其余codec强例未改。

## 最终旧复验与八项目检查

主控全文读取并执行不可变 `/private/tmp/task56-old-final-r1.py`；`logs/task56-old-final-r1` App80／Protocol76 全pass，实际 argv／环境／start-end／childexit 留 `run.json`。`evidence/task56/old-final-r1.json` 及主控独立复核确认39 owned（34 source、27 product）与8704非owned、HEAD／branch／status／空index精确恢复。最终旧复验冻结的 CURRENT 中，Shop 的 DomainEvents using 已被误删；恢复 CURRENT 后 fresh build-r1 暴露该编译错误（0warning／1error），随后只恢复该行，精确差异在 `old-final-current-to-final-r2.patch`。主控接受其无业务／测试差异、无需再旧回放，r1原日志保留。

`logs/task56-final-r2/build` 为 fresh Release solution `--no-incremental --no-restore -m:1`，child0／0warning／0error。随后八项目全部 fresh，原类并行／5m／console detailed／独立 DFO＋TRX，`--no-build --no-restore`，每运行实际 argv／start-end／childexit完整且全部自然结束／严格串行。

| 项目 | 本次full结果 | 对当前有效基线逐display／outcome／重数 |
|---|---:|---:|
| Application | 5859＝5841pass＋2原fail＋16skip | 新增46／删除0 |
| Protocol | 1790pass | 0／0 |
| Networking | 203pass | 0／0 |
| Persistence | 484pass | 0／0 |
| Infrastructure | 976＝974pass＋2skip | 0／0 |
| Host | 203pass | 0／0 |
| HttpApi | 57pass | 0／0 |
| Pvf | 80pass | 0／0 |

合计9652＝9632pass＋2原fail＋18skip。原失败仅 DimensionDungeonTests 的 `Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` 与 `All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，完整Message仍 `Dungeon '7100' maze '0': cell (1,0) has no compatible map for entrance 'Left'.`；Name／Message及18skip身份和重数完全不变。基线为App／Protocol／Net TASK55-final-r2、Host TASK53-final-r1，其余TASK48-final-r2；全部本次fresh，未复用旧项目冒充。本地 `comparison.json` 和主控独立 `task56-final-r2-main-inventory.json` 均核通过。

原 `violations.py` 逐当前程序集核74＝67negative＋7control；App62／Protocol1／Net7／Persistence1／Infra1／Host1／Http1／Pvf0，outside／exe／orphan0。expected取各项目上述已接受原日志，不改parser／expected／Skip或测试并行。逐项目命令及输出在 `dfo-check.json`／`dfo-parser.txt`，主控独立parser输出为 `main-dfo-*.log`。

## 接线、收口与限制

`evidence/task56/runtime-r1` 保存probe源码／csproj／runner／argv与原输出，实际编译程序集 Location／MVID 核十五唯一Protocol binding、十四whole、Direct、空exits／Self None，QueryShopPurchaseCounts类型／handler／注册均0，十四保留遥测不变。冷probe只构造模块DI登记，不Build provider／Host／数据库或游戏；真实provider／invoker由正式flow和CommandRegistrationTests覆盖。冷probe DLL与最终测试载入DLL逐bytes相同，见 `runtime-dll-match.json`。

ledger仅本十六行改变，baseline全部original／行序及其他行原样；消息136/159、fine118/204、来源2/73、保护2/5、过渡0/8。Result后strict及八坏例均通过，`ledger-strict.json`无待完成Result、八种坏数据全部拒绝；含所有新增文件的 `git diff --check` 无诊断。纯准备patch当前reverse-check通过，可供主控分段提交。最终39 owned完整备份、恢复patch、source-owned与non-owned守卫在 `/private/tmp/task56-finalbackup-r1`，原旧复验备份保留 `/private/tmp/task56-old-finalbackup-r1`。

实测范围：scope仅Contents／Shop；encoder为七个有响应入口；日志异常ClientSpecStatistic、查询异常Shop。其他十四入口的scope不冒称动态覆盖。无写故不适用SQL／native Commit；受控physical替换非真实重登录，未做A21实机／专项CMD性能测量。未Git写入／外部变更／部署／清理工作区，不启动下一Task。所有runner结束后交主控唯一fresh Astra。
