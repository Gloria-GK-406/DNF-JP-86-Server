# TASK15 真实诊断与轮次身份

每个 task-15-* native 命令有新 prefix 的完整 log/meta（command/cwd/HEAD/UTC/exit/elapsed/resources），自然结束；runner 自身退出0不替代 metadata 内 actual exit。没有补造 exit、覆盖旧 output 或 TERM 父 runner。

| 实际轮次 | 事实与处置 |
|---|---|
| phase0-oracle-01 | CS0246，新增测试缺 Networking using，未运行 case；非行为 RED。补 using 后02两旧 route oracle通过，03旧31+新2共33通过，04补 Error 级别两case通过。旧r1证据封存。 |
| unit-red-01 | CS0535，唯一额外 ICommandWorkUnits 实现 GameLoginTests.DirectWorkUnits 缺新成员。停写/主控r2精确追加单文件，新方法只 NotSupported；非 RED，无生产接口 default。 |
| unit-red-02 | 可编译的三个真实行为 RED：实际 native SQLite Commit 外抛、E+rollback X 两 theory仅保一个原因。具名 commit-only 结果和 Abort(E) 后green01全29通过。 |
| unit-red-03 | 两实际 RED：RequireRollback(cause) 的 cancel/typed 被初版转正常值。新入口拒绝并sticky，green02共50通过，原首typed/cancel单次目的保留。 |
| whole-focused-01/02 | 分别缺 thin Dispatching using、现成 AddProtocolCommandHandler exits-first overload 顺序错误，编译修正；非 RED。 |
| whole-focused-03 | OpeningWireSlots 是 Protocol internal，App测试原 fine caller未直接访问。主控核准四个明确 typed CreateDelegate 调原纯转换，拒绝扩生产可见性/复制算法；非 RED。 |
| whole-focused-04 | 63 case=60pass+3原DarkRepulsor skip。原31 Package/SQL/Throws/冷却/同步/capacity等全部保留，新旧协议两oracle通过。 |
| source-focused-01/02 | 01缺 Persistence.Database using（非 RED）；02真实三leaf/facet来源7+原Uncertainty7共14通过。 |
| whole-matrix-01 | 新测试缺 Content using（非 RED）。 |
| matrix-focused-02 | 34pass+两个新测试失败：新增unawaited假设错误，旧bus先DemandFinished sticky unfinished guard、drain不能继续native Commit；另TCS entered先于pending赋值有fixture竞态。加assigned同步，改为原unfinished guard+真实SQL回滚/cleanup目的，不改bus/超时/并行，不冒已到Commit。 |
| post-current-red-01 | 四真实组合3fail/1既有正例pass：真正 native Commit E 后独立 current X丢E；相同对象的独立current被Ref相同吞掉。仅局部保因stage+retained Commit cause修复，green01共54通过。 |
| matrix-focused-03 | 新rollback注入throw条件表达式缺括号，编译修正；非 RED。 |
| matrix-focused-04 | 新三个测试误预期只有一次close。实际默认 participant fence close＋failed-job source最后close两记录，均同原物理连接；原行为未改，纠正新测试为精确两次+ReferenceEquals。 |
| mutation-guard-01 | owned runner临时移除Guard/Unknown恢复排除，实际SQLite custody returned1/rows0/ConfirmedRollback/cause none使一case红，对照一pass。finally原bytes/SHA完整恢复；恢复green01共57通过。不是编译/fixture RED。 |
| final-focused-01 | 新 phase取消/真实持久后nonfallible违约加原default/B2，全93通过。 |
| formal01 | 首轮完整 build0/0＋八full8699=8679pass+2准确原Dimensionfail+18skip；原8644全display/status/multiplicity保存，N55/D0；DFO74/7精确，无outside/exe/orphan。所有step自然结束，formal01-*副本保存；因后续重复声明修复仅为历史首轮，不混最终。 |
| repeated-refusal-red-01 | 先普通E1声明，后独立typed/cancel E2被拒绝时，两个实际case均只外抛E2，E1丢失。主控已核r2§4，Action传播再要求retained与cause同源，green01全95通过（首typed单次/guard传播/post-current/B2/default保持）。 |
| mutation-guard-02 | 在上述最终source再跑同一真实SQLite mutant，1fail/1pass，finally原bytes/SHA50336652fad2b84338641bd9243fb9fa48ada532982779f05c91124ce3ea4817恢复；restored-green02全95通过，再freeze02完整正式轮。 |

formal02完整8701/N57-D0、37源冻结的九step/DFO/capture/strict/8bad全部保留为prefinal02；RunItem非法直接Commit Guard阶段标签经主控精确裁定后，现有case ExpectedAction/ActualFallibleCommit真实RED，单点改Action后来源+原不确定性14/14 GREEN；不新增case，Kind/cause/message/predicate均不改。

最终正式判定只绑定 freeze03 与 formal03 全九step、DFO03/最终capture/ledger strict；formal01不代替它。两原 Dimension 身份和精确 Dungeon7100/maze0/cell(1,0)/entrance Left InvalidDataException 按契约保留，不是新增baseline；本Task未遇到要改 socket harness/queue/deadline/并行的理由。

未进行单CMD≤2秒性能测量、实机游戏、远端发布、根Spec/总目标封存；没有stage/commit，required独立结果快审和checkpoint归主控。


最终证据收集器的两项非产品诊断：只读进程检查初次sandbox的ps被PermissionError拒绝；随后require_escalated只读过滤确认无本Task runner/testhost活跃，无signal/TERM，见native-idle-final.json。另一次有界ledger自检误将允许补全的original_callers也按original前缀视为不可变而assert；核实际schema后改为精确original/baseline字段，确认仅8授权行及原字段不变，未改ledger。final03所有native步骤自然完成，native-step-index77步完整保留。
