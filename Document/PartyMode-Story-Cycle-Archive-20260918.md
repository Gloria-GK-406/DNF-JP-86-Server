# A21 Story/Cycle 组队模式调查归档

状态：**历史取证，不是已交付功能或 `ClientVerified` 协议结论**。本页吸收旧 `party-mode-story-cycle` 工作树的可复用结果；该任务短期不继续。原始 22 份材料（21 份文档、1 个静态探针）保存在 [任务归档](../.rks-agent/tasks/party-mode-story-cycle/) 中，其中 `running` 等字段只是停止时的控制器快照，不表示现在仍在执行。

## 已有证据与边界

| 主题 | 可复用结论 | 证据等级与限制 |
|---|---|---|
| Story 设置 | A21 静态构包表明 `PartyCategory.Story=0`，沿用 `0x000C` 的 12 字节设置块或 inline-title 形状；容量走共用 1–4 规则。类别在 `[9]`，target 以 `u16 LE` 位于 `[10..11]`。 | 固定客户端静态确认；不是原生创建实帧验收。详见 [字段图](../.rks-agent/tasks/party-mode-story-cycle/attempts/spec-001-story-party-mode/evidence/task01/story-field-map.md)。 |
| Story target | 来源是客户端当前剧情进度查询；收到后保存到 party object `+0x64`，被剧情记录查找、当前目标比较与进度显示消费。把它解释为剧情任务/场景目标 ID 是 High 语义别名，wire/API 仍宜称 `ModeTargetRaw`。 | 不能将它当作普通地下城 ID；精确资料表、合法值域、默认值尚未证明。`0` 同时是客户端内部刷新门；不能硬编码 `0` 或 `0xFFFF` 为 wire default。 |
| 不透明字段 | `[3..6]` 为 `u32`、`[7]` 为 `u8`；正 target 路径会联动写入，具体业务意义未闭合。 | 保留原字节，不能归零、重算或据此拒绝请求。`[0]` 和 `[8]` 的观察值也不应擅自扩大为全路径枚举规则。 |
| Story 生命周期 | category 0 的委任队长 UI 静态分支显示 dstr 48613 并跳过 C2S `0x0079`；另有队伍通知和任务变化后的 Story target 自动刷新调用链。 | 静态可达性证据；“从未发出 `0x0079`”及刷新频率仍需同会话动态对照。 |
| 上游对照 | 固定 `ServerS4A21` commit `c58cd2b479b0357cb6ca78887c9d638b080f4638` 对设置块主要做 opaque pass-through。 | 它只证明兼容传输面，不证明任意 target 合法，也不证明当前 A21 客户端行为。详见 [上游基线](../.rks-agent/tasks/party-mode-story-cycle/attempts/spec-001-story-party-mode/evidence/task01/upstream-baseline.md)。 |
| Cycle | 旧规格只确定 `PartyCategory.Cycle=1`、同一设置块的 category/target 位置，以及客户端存在“没有循环地下城区域”的资源文本。 | 尚无 Cycle 专属字段图、原生创建/编辑帧、区域默认/无区域规则、实现或实机验收；[Cycle 规格](../.rks-agent/tasks/party-mode-story-cycle/attempts/spec-002-cycle-party-mode/spec.md)仅供以后重新立项参考。 |

Story 设置的静态探针固定于 A21 B1 EXE SHA-256 `DC322D8F…3306E9`、dstr SHA-256 `60E98577…92F10`、ImageBase `0x00400000`。历史运行记录为 19/19 字节窗口、6/6 文本资源、5/5 上游 blob 通过；这些只验证指定输入未漂移，不自动证明业务语义，当前文件身份变化时探针应按设计失败。完整输入、VA/RVA 和复跑方式见 [输入清单](../.rks-agent/tasks/party-mode-story-cycle/attempts/spec-001-story-party-mode/evidence/task01/input-manifest.md)、[验证记录](../.rks-agent/tasks/party-mode-story-cycle/attempts/spec-001-story-party-mode/evidence/task01/static-verification.md)和 [探针](../.rks-agent/tasks/party-mode-story-cycle/attempts/spec-001-story-party-mode/evidence/task01/probe_story_party.py)。

## 不吸收为产品规则

- Story 子规格在缺少当前 A21 原生 create/edit 和特有操作时间线时已标为 **blocked**；未修改生产代码。不能用上游透明转发或构造测试值补足这项证据。
- 不能从通用“组队模式不符”错误推导 Story 副本准入谓词，也没有静态调用链证明公共目录 `0x023C` 或快速匹配 `0x01BE` 是 Story 基本设置不可分割的一环。
- Cycle 仅有规格设计，没有调查结果；旧控制器的 `running` 标记不得升级为 Cycle 完成或可实现结论。
- 本页讨论的是**组队模式设置**，不是主干已实现的剧情地下城组队入场；两者验收边界不同。

## 将来恢复时的最小入口

先固定当前客户端和 Host 身份，再按 [Story 定向抓帧计划](../.rks-agent/tasks/party-mode-story-cycle/attempts/spec-001-story-party-mode/evidence/task01/runtime-capture-plan.md)获取同一会话的原生 create/edit、S2C、UI 与非 Story 委任对照，裁决 target/default 和 `[3..7]`。旧计划中的 debug 路径、端口和配置只是当时的操作草案，复用前必须重新核对；不要直接对 play 客户端或当前数据库运行。Cycle 需另做静态字段图和原生帧采集，不能套用 Story target 语义。
