# SAO 内容期限与名称核对

日期：2026-10-15。基准提交：0de58d52417b9101052c855b74f6de1b80ca4c7b。

## 修复范围

`Tool/PvfSaoTowerEntrancePatch` 的 `--sao-items` 模式仅审计路径含 `sao` 的 `stackable/*.stk` 与 `equipment/*.equ`，删除这些 SAO 联动物品的历史固定 `[expiration date]` 标签及其一个字符串值。每份当前档案独立读取、生成，不互相覆盖档案。实际被修改的路径仅在 `stackable/cash/chn_20141111_sao_package/` 与 `stackable/event/chn_event_20141111_sao_event/`；完整逐条清单包含在对应 `.pvf.json` 的 `RemovedExpirations` 中。

源档包含 172 个 SAO 候选物品，其中部分礼盒存在两次固定期限声明。服务端与 protocol2 的门票已经移除期限，故各修改 98 条目、删除 116 处声明；另外三份各修改 99 条目、删除 117 处声明。对应删除 232／234 个 token，其他 token 不变。

服务端源档的日期分布为：2014-11-30 06:00:00 共18处，2014-12-30 06:00:00 共4处，2015-01-15 06:00:00 共92处，2015-03-26 06:00:00 共2处；合计18+4+92+2=116。其他三份另外包含一处门票的2014-12-30期限。

保留动态期间、租赁、每日删除及其他非 SAO 物品；不改变数据库内玩家已有物品的期限。工具遇到陌生日期形状、2026年及以后的期限、在位覆盖、已有输出或空补丁会拒绝执行。

## 开发客户端清单

|目录|用途依据|纳入本次期限补丁|源档有 SAO 入口|
|---|---|---|---|
|`E:/Project/DNF/DNFClient`|本地18080 Launcher；2026-10-11卡牌／拾取取证日志|是|否|
|`E:/Project/DNF/DNFClient-A21-debug`|本地 Launcher、调试版 exe 与重建／保护层取证配置|是|否|
|`E:/Project/DNF/DNFClient-A21-protocol`|本地18080 Launcher、协议测试环境|是|否|
|`E:/Project/DNF/DNFClient-A21-protocol2`|本地正在使用的协议验收客户端；门票修复源档哈希吻合|是|是|
|`E:/Project/DNF/DNFClient-A21-upstream`|上游对照客户端|否|未修改|
|`E:/Project/DNF/DNFClient-OneKey`|发行一键包|否|未修改|

期限模式本身保留原有入口差异；对前三个开发客户端在独立期限补丁之后追加应用已验证的默认入口模式，同步既有3600入口修复。因此最终三份组合档案各修改99个SAO物品＋1个worldmap/Towers.wdm，共100条目，入口插入2个整数 token。server与protocol2入口已在原档存在，不重复修改。客户端的 exe、插件、其他资源和配置没有修改。

## 产物与回退

新档案、原档完整备份及同步清单位于 `artifacts/sao-core-content-20261015/`。`sync-manifest.json` 记录每份源路径、输出路径、源／输出 SHA-256、备份路径、修改数与验证结果；每个 `Script.<来源>.pvf.json` 含逐条期限清单与100个 DGN 名称审计。`rollback/Script.<来源>.previous.pvf` 是对应当前源的逐字节备份。

生成完成时未替换任何现用客户端或服务端档案，未启动进程，未修改数据库。清单中的 `Deployed=false` 是准备阶段状态，实际同步后应另写同步回证，不能将准备清单当作部署证明。

## 验证

- 工具构建通过，零警告、零错误。
- 每份档案712010条文件表项逐项比较：目标条目的路径／名字／类型不变且数据等于计划 token；其他条目完全不变。
- 所有原有压缩 chunk、名称池、哈希表字节保持一致，期限补丁仅追加一个新 chunk 并重定向目标表项；三个组合补丁再追加一个入口 chunk，两阶段分别核验，manifest列出完整修改路径与各阶段收据。
- 五份新档重新打开审计，172个 SAO 候选物品都不再含固定期限；100个 DGN 的名称未变。
- 相同服务端源两次生成的 SHA-256 一致。
- 旧 `--ticket` 模式对原测试输入的输出仍精确等于 `D6D5D56EE057D191562D27E14E2DF64D25D011A3CF7C47577475AACAF6528068`。
- 默认入口模式对已知原始输入的输出仍精确等于 `9DF742F973C79A10882D80C6D3A46FB34F6691A26A4777F6F261E37061662832`。

这些是档案与工具验证，不是客户端实机验收。

## 地图名称

证据等级：ClientStatic／CurrentContent。

五份客户端／服务端源里的 `dungeon/Towers/SAO/SAO_TowerOfDespair001.dgn` 至 `100.dgn` 共100个 DGN，`[name]` 均是直接 `QuotedString`「艾恩葛朗特」，不是待解引用的 `dnf_xxxx` 键；这100个 DGN 的全部字符串 token 也不含 `dnf_`。因此没有证据支持把 DGN 名称补写或全量替换。

第一层 MAP `map/SAO_Tower/001_SAO_1st.map` 的 `[map name]` 为「PVP无名」，确实是另一个名称字段，但与用户描述的 `dnf_xxxx` 不同。原截图被结算界面遮挡，缺少完整 HUD 键及其客户端显示调用点，尚不能判断是地图名、HUD 本地化格式键，还是特殊 SAO 层数通知缺失引发的显示路径。当前不修改任何名称字段；后续实机取得完整文字后才追具体字符串／显示函数。
## 两处迷宫尺寸修复与 v2 产物

原始100层声明核对：98层的 `[size]` 是 `1 1` 且 `[greed]` 只有 `BB`；第25层DGN3624却是 `4 1`／单BB，第55层DGN3654是 `2 1`／单BB。两者的出生格、Boss格都是 `(0,0)`，没有其余迷宫格声明。第25层对应 `map/sao_tower/025_sao_1st.map`，归属DGN3624、有完整出生区、10只64814 Boss；第55层对应 `055_sao_snow.map`，归属DGN3654、有完整出生区、一只64812 Boss。它们与其余98层同属单房模式，不能以缺失格子隐式补房来放宽解析器。

新增工具 `--sao-core` 模式：在去除 SAO 历史期限之外，仅对这两个明确已知的 DGN 将宽度整数从4／2改为1。修改前严格核对旧尺寸、单BB、出生／Boss格0,0，不泛化猜测其他地图尺寸；高度、地图、怪物、名称及其余 token 不变。每档额外修改2个DGN、2个整数token。

最终同步请使用 `artifacts/sao-core-content-20261015/sync-manifest-v2.json`，此前期限补丁及其清单保留作证据，不覆盖。最终server／protocol2各修改98物品＋2DGN＝100条目；其余三个开发客户端各修改99物品＋2DGN＋1入口＝102条目。各最终档100个DGN均为1×1／BB，期限零残余，SAO入口存在。原始→最终的712010条文件表复核见 `original-to-final-v2-verification.log`；100／102个指定条目外，其余711910／711908条完全一致。

原始100层尺寸清单见 `server-maze-audit-before.json`；MAP原文证据见 `map25-before.txt`、`map55-before.txt`；各最终100层尺寸清单见 `<来源>-final-v2-audit.json`。修复后真实地下城创建测试由领域实现测试承担，档案尺寸一致性本身不是实机验收。
## 实际同步回证

2026-10-15已同步4份开发客户端和现用服务端PVF；对应原档完整备份已核验。上游对照与OneKey发行包未改。服务未启动、数据库未修改。原准备清单的Deployed=false保留，不用于宣称部署；实际回证为artifacts/sao-core-20261015/content-sync-receipt.json，AllSynced=true。

| 来源 | 已同步SHA-256 |
| --- | --- |
| server | FF883D739DB35EE552F5EFABFE0CEE99849CB6E053C4FEAA79FB08ADA0B3192A |
| client | 0B75CEC45068F7C01F68EACCC6F44D85E24A7A3B2E441C9DA33FCBF4C85492BE |
| debug | AC02923435FE13261200D08496207BA3FE9020017771F4FCFC6034DCD9200068 |
| protocol | 0B75CEC45068F7C01F68EACCC6F44D85E24A7A3B2E441C9DA33FCBF4C85492BE |
| protocol2 | 950E98959C15B79BCD820C736DF40CE62C47EAB4275BF3C28F3D76FEBFA2981F |
