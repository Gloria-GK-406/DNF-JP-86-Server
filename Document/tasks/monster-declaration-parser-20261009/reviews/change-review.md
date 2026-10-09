---
kind: rks-review
verdict: approved
---

# 独立结果审查：怪物声明统一解析

## 审查对象与依据

工作区 `.worktrees/monster-declaration-parser`，基线 `8e8192b94f5acc3ab7a471e27ed197d08ae8c7fd`。审查对象为 2026-10-09 当前全部生产代码差异、新声明模型、新解析器、相关测试和本任务文档；包含末轮将 MonsterTeam 与战斗 Faction 分离、拒绝 dummy 死亡 claim 的变更。

适用判据：`reviewing-work/references/code.md` 与 `references/deliverable.md`。依据 `TaskContract.md`、`spec.md`、`plan.md` 的边界，未将本次任务扩大为全地下城机关或条件脚本解释器。审查者未参与实现、未修改生产文件，也未运行共享构建。

## 结论

**approved**：当前范围内未发现未解决的阻塞缺陷。批准的是代码及说明的当前结果，不替代控制器的最终测试、部署身份验证或 A21 实机验收。

## 独立检查

| 范围 | 检查结果 |
|---|---|
| typed token 与记录边界 | 普通怪物共同前缀、dummy/NPC 修饰、rank、精英特征数量由游标消费；不会再固定切分十 token。缺失末尾精英 count 的真实资源兼容与下一记录前缀识别有回归保护。非法类型、数量与截断明确给出资源、section、记录、token。 |
| APC 与条件声明 | APC 精英特征先于两个尾参数，阵营独立保留；条件 summon 的三参数和尾坐标分开。条件声明只从生成适配层过滤，仍能由 GetDeclarationsAsync 取得。blood/ultimate 等附属声明保存而不伪装成立即普通敌人。 |
| source、缓存和 AIC | 完整声明与生成适配结果各自缓存；AIC 等级仍由内容源读取，保留先前缺失文件的过滤和格式错误校验。普通/APC 组内顺序稳定，dummy 保留原 MAP 怪物索引。 |
| dummy 与死亡消费者 | dummy 在 Cell 创建中不冻结普通经验与奖励、不阻塞清房。末轮 CanClaimDeath 额外拒绝 dummy，避免后续非 monster 分支误将其作为 APC 任务击杀。 |
| monster team | 末轮不再把任意 team 数值强转为 APC Faction；保留原值，BlocksClear 独立接受缺省或 100。已核对上游 IsBlockingMonster 的 100 判定；70 赋值链证据由调查提供并在 survey 标注。其余 team 战斗语义未被宣称已知。 |
| 特殊对象身份 | 父索引在空对象、虚拟非 monster 对象、控制对象后继续递增；子索引包含 item/trap/quest 项。START_MAP 使用独立模板 owner/child index，flag0=1、flag1=owner，不消耗普通/APC 组内序号。已独立比对上游 DungeonActorTemplateProjector 的投影。 |
| 清房、奖励与重投影 | 已确认 GentDefence 波次模板参与死亡账本，Boss suppression 排除特殊对象来源；其余模板默认不阻塞的边界明确。重述房间从 Cell 保存的 owner/child 身份投影，未重新计算编号。深渊模板路径与既有奖励冻结仍保留。 |
| 测试与调查范围 | 新测试覆盖真实 22036 五条记录、21314 的 Boss APC 与三十模板，以及游标边界、对象索引、非法输入、dummy、team 和场景波次死亡。全量测试是 map.lst 引用的 10,293 MAP 语法覆盖，不代表孤立资源或全部机关行为已经实现。 |

## 验证证据与剩余边界

控制器报告末轮消费者保护之前聚焦测试为 Infrastructure 49、Application 170、Protocol 295，合计 514，通过且无跳过，其中包含实际 PVF 全量 MAP 解析。末轮新增七例 Application 回归及更新后的最终测试结果，由控制器写入完成证据；本审查没有独立重跑这些共享测试，也不将先前数字冒充最终轮验证。

实际客户端仍需验证追击歼灭战进入 Boss 房、根特防御战模板激活与通关，以及普通/主动深渊房间；dummy 资格和 GentDefence 控制器路径是当前明确投影策略，不能由语法解析成功直接升级为 ClientVerified。

非阻塞文档建议：survey 开头的扫描 Program.cs、report.json、summary.txt 应补可定位的实际工件路径，便于复现计数。完成证据与部署记录仍由控制器负责补齐。
