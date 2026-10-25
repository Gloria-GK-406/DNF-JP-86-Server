---
kind: rks-verification
decision: pass
---

# TASK-54 核验摘要

本结论只核六消息、三 fine 的迁移执行结果；基线 `ae0fd603`，分支 `task/refactor-session-20261021`。仍待唯一 fresh Astra 快审及主控接受，不代表 T-007／Spec 封存。

**交付与行为。** 五个 Direct／Protocol whole、六个薄入口；两种 Notice 是精确 CLR 的有限家族，未知形状无 fallback。原三客户端设置在 online account 门后捕获完整 ClientSettingsOrigin，只有原 nullable selected 非空才声明精确 session／本次 physical／metadata；CharacterOption 保原 actor，两 Notice 保原 IAccount-only 空 selected，Secondary 的角色 owner 不被扩成保护参与者。每次有效保存各一个 ordinary Write、None footprint，无 Main／效果账本／raw／Self、业务 catch 或核心／注入白名单变化。Main150B、QuickChat 双 bank300B、Hotkeys 完整最长65536B、CharacterOption516B、Notice exact4B／0..127 的原 decoder 不变。Hotkey 捕获仍在事务外，未知 profile Warning 后零单元／无帧；事务只用该捕获 context。原字段、profile token／key／generation、exact connection／selected 和领域 InWorld／数据库 owner 门全部保留。全部六路没有 ACK 或额外 snapshot；scope 默认差异仅 Spec VC-B34。

**正式旧新证据。** 新增 `SettingsProtocolCommandFlowTests` 35 项，以真实注册 decoder／dispatcher、原领域／SQLite、production outbox 和受控 physical sender，覆盖账号态与 selected、完整 blob／双 profile／双 bank、多字段隔离、两 Notice 精确 owner_type＋owner_id＋notice_group＋notice_id、重复报告无帧、未知 profile／缺角色零事务、事务外捕获后的 token 变化、Secondary 在外层门后丢世界的原领域拒绝。六路 SQL action 错误与 native Commit deferred-FK 错误分别核原 SqliteException 对象、1811／787、实际 ConfirmedRollback 和相关五表完整值回滚；scope 为真实 DI Dispose，直接核 Committed、同 cause、Fence／physical Block／委托实际 Creature 的 DiscardExact、真正 physical CloseAsync 与同 ID 替换连接零帧零关闭。无 ACK 路径没有虚构 encoder 故障。原 App15 项强域／entry／appearance-event 判据和 Protocol30 项 codec 原断言保留。原 entry 强例改走合法 dispatcher，仍核真实保存后 entry 完整读回；其自行 new 同 ID physical 在新精确保护下无效，仅改用在线 user.Connection，作为结构夹具适配与纯准备 patch 分开。

最初旧实现 App15／Protocol30 均通过；纯准备 r4 App50 通过。第一次冻结全量完成后，执行自检发现 wire 的两 Notice 只核 ID／数量，主控裁定在同用例补精确 owner/group 断言，再做完整旧新 r2，无产品变化、无新增 case。主控最终旧源 `logs/task54-old-final-r2`：App50／Protocol56 全 pass、两 child0；21 CURRENT 路径、固定 HEAD／branch／空 index／status、finally 逐文件恢复经主控核实。实际执行脚本及完整备份在 `/private/tmp/task54-oldcheck-20261026-r2/`，报告 `evidence/task54-old-final-r2-report.json`；r1 备份、run/TRX 和报告仍保留。交付脚本后曾发生 ignored 证据输出命名修正竞态，由主控保留实际执行版本并核对两轮真实报告；没有产品变化或证据丢失。纯准备 `evidence/task54-pure-preparation.patch` 仅两个 App 测试文件，不含 exact-physical 结构适配或新注册断言。

**最终 r2 测试。** `logs/task54-final-r2/build`：fresh Release solution `--no-incremental --no-restore -m:1`，child0、0warning／0error。随后三个项目严格串行，原类并行、5m／console detailed／独立 DFO 与 TRX、`--no-build --no-restore`：Application5788＝5770pass＋2 原 fail＋16skip，Protocol1789pass，Networking203pass，共三 fresh 7780＝7762pass＋2fail＋16skip。两 fail 精确为 `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` 与 `All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，Message 均为原 Dungeon7100／maze0／(1,0)／Left 缺图，逐字比 TASK-53 不变。`final-r2/comparison.json` 逐 display／outcome／重数：App 新增35删0、Protocol新增1删0、Net零差；skip 身份／重数完全不变。其余五项沿有效历史：Host203（TASK-53 r1）、Persistence484／Infrastructure976（974pass＋2skip）／HttpApi57／Pvf80（TASK-48 r2），有效总库存9580＝9560pass＋2fail＋18skip；不称八 fresh。

**闭合与限制。** 原 violations.py 逐实际程序集核 App61反例＋1control、Protocol1control、Net6反例＋1control，共70，outside／exe／orphan0，见 `final-r2/dfo-check.json` 及各 parser 输出。`evidence/task54/runtime-r2/runtime.actual.json` 用实际编译程序集和 DI 登记证明六唯一 binding／五 Direct Protocol／空 exits／Self None，三 fine 类型／handler／注册0，含实际程序集身份；该冷探针不构造 provider／Host，正式 flow 与 CommandRegistrationTests 的实际 invoker 解析覆盖 provider。冷探针 r1 仅因引用 internal CommandBus 取程序集而编译失败，改 public ICommandBus 后 r2 成功，未改产品。

迁移账只九目标、original／顺序／非目标行不变：118 消息 closed／41待／14保留，114 fine closed／90待；ActivateClientHotkeyProfile 及其真实登录／选角消费者、appearance 可靠事件来源保留后继。AGENTS 仅表增一行，Spec 只追加 VC-B34／更新其余范围，不改 revision／Plan。Result 后 strict 与八坏例全部拒绝，见 `evidence/task54/ledger/`。含新增文件 whitespace 检查、全部 runner 结束、最终22 owned 路径和可恢复 patch／备份在 `evidence/task54-final-owned-manifest.json`、`evidence/task54/diff-check.json`、`/private/tmp/task54-finalbackup-20261026/`。产品／测试与最终旧复验 CURRENT 一致。未做 A21 实机、专门性能测量、Git 写入、部署、外部 issue 或清理；执行者 STOP，交主控唯一 fresh Astra。
