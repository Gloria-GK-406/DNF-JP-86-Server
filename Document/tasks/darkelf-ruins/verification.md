---
kind: rks-verification
decision: manual_required
---

# 暗精灵遗迹候选验证

验证对象为 `codex/darkelf-ruins-75` 的服务端候选 `0.2.10`，基线 `63fb3993`。
结论限于当前实现、自动验证和隔离部署；完整功能交付仍须按 [编号步骤](README.md#验证与验收顺序) 获得 A21 用户反馈。

## 自动结果

下列命令在工作区根目录运行，真实内容测试显式设置
`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。
所有数据库写测试均使用夹具临时库；未对玩家库执行夹具初始化。

| 检查 | 最终结果 | 证据（工作区相对路径） |
|---|---|---|
| `dotnet build Server/86JP.Server.slnx --no-restore -p:UseSharedCompilation=false -m:1` | 通过，0警告/0错误 | `artifacts/darkelf-logs/build-final.log` |
| 完整解决方案 `Category=Unit` | 1299通过，0失败/0跳过；未带 Unit 分类的项目无匹配，不代表该项目全量通过 | `artifacts/darkelf-logs/unit-final.log`、`artifacts/darkelf-tests/darkelf-unit-final*.trx` |
| 实际 PVF 目录、全部十二张 DGN 的普通/守护者迷宫及对象/单向传送 | 2通过，0跳过 | `artifacts/darkelf-logs/content-final-v2.log`、对应TRX |
| 遗迹规则、真实SQLite准入/扣费回滚、共享星级、日/月切、复活、冻结/领取/补领、商店兑换 | 遗迹和复活22通过，0跳过；包含于下行受影响领域测试 | `artifacts/darkelf-logs/domain-active-v2.log` |
| 遗迹及受影响房间、拓扑、移动、复活领域回归 | 160通过，0跳过 | `artifacts/darkelf-logs/affected-application.log`、`darkelf-affected-application.trx` |
| 协议注册、结构约束、选图、清场、专用codec、START_MAP对象尾 | 205通过，0跳过 | `artifacts/darkelf-logs/protocol-final-v2.log`、对应TRX |
| Release publish | 通过，输出仅在本工作区 | `artifacts/darkelf-logs/publish.log`、`artifacts/darkelf-package/` |
| 协议文档检查器自测 | 通过 | `artifacts/darkelf-logs/doc-sync-self-test.log` |
| 协议文档全量同步检查 | 当前与基线均有相同18项既存失败；本次新增失败0，不能标为全量通过 | `artifacts/darkelf-logs/doc-sync-current.log`、`doc-sync-baseline.log` |

完整失败与跳过日志保留在 `artifacts/darkelf-logs/`；最终表只统计有效执行。
首次沙箱还原未成功，不声称获得测试先红后绿的有效 RED 证据。

## 隔离副本库部署

从已核实的实际活动库经 SQLite backup API 生成一致快照，再由该快照生成冒烟副本。
快照与迁移后的副本 `PRAGMA quick_check=ok`；4个账号、9个角色、644条物品槽、2196条不可见标记计数及抽样角色行均保留。
候选迁移新增 `darkelf_ruins_progress`、`darkelf_ruins_rewards` 两表，未删除既有事实。
快照哈希、路径和抽样核对结果仅保存在忽略的 `artifacts/darkelf-runtime/backup-manifest.json`、`migration-result.json`，不提交玩家资料。

候选 `Dfo.Server.Host.exe` 从本工作区发布目录启动，使用副本库及活动PVF。
显式关闭脚本频道投影，频道ID11/游戏21011，HTTP21080、频道21000；TCP监听集合完全匹配，UDP监听为空。
HTTP bootstrap 返回200/ready，频道与游戏端口可建立连接。冒烟结束仅停止本脚本创建且 EXE 路径匹配的候选进程；原 PID29936 保持运行。
原始输出、实际启动配置、PID/EXE和监听在 `artifacts/darkelf-runtime/smoke-*`；未将此合成连接记为真实客户端验收。
主干检出为 `63fb3993`，实际原运行产物仍是 `0.2.8+c076e80e`，二者已分别登记；恢复依据实际原产物、配置与环境覆盖。

## Unverified Conditions

- A21 原生入口、曜日/次数显示、升星提示与三个区域共同变化。
- 专用结算两阶段请求顺序、02F9奖励槽语义、02F7第三字段、02FA真实语义。
- 随机对象非空尾、重访重生、艾丽西亚门、守护者实际战斗和地图机关。
- 原生组队、断线重登、跨日/月及背包满邮件交付。
- 用户已授权关闭默认端口现行服务并启动候选；原玩家库、最终一致备份及默认端口就绪已核实。尚未取得用户实机反馈、未创建PR、未运行本候选云审核或CI。

当前活动PVF SHA256为 `01F1BE685D46647961DF65207A36304777DB20EF5A3D03C68EFAA92F8B0C5189`，本次真实遗迹内容测试2通过、0跳过。
最新库副本迁移保留4账号、9角色、670物品槽、2197不可见标记及全部角色的等级/经验抽样；主库已通过最终一致备份后接管。
本次运行身份、源库、备份哈希、完整探针及启动日志在 `artifacts/darkelf-runtime/cutover-20261029/`。
默认端口为7001/10011/18080/18081，启动器bootstrap和GM健康返回ready、GM页面返回200；不据此升级为ClientVerified。
