---
kind: rks-verification
decision: pass
date: 2026-09-09
---

# 完成验证：角色静态规则与动态事实

验证对象是基线 8b19eaac9863e23da4703fa644bfd0cb882b6cd6 上、分支 refactor/character-rules-and-state 的完整重构。完整独立审查 [change-review.md](../reviews/change-review.md) 已 approved；本次全部检查在该审查之后执行。75 个产品文件与 [审查输入](../reviews/change-review-input.json) 的 SHA-256 逐一一致，删除项仍不存在。

## 新鲜检查

工作目录为本任务工作树。设置 PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf，执行 dotnet build Server/86JP.Server.slnx --nologo，退出 0，零警告、零错误；见 [build.log](final/build.log) 和 [build-exit.txt](final/build-exit.txt)。

七个测试项目分别运行 dotnet test Server/tests/<项目>/<项目>.csproj --no-build --nologo，使用 console;verbosity=detailed 和项目独立的 trx logger，完整输出写入 final/。各进程退出码全部为 0；逐个解析 UnitTestResult，未以进程成功或 TRX 文件存在代替用例通过。

| 项目 | Passed | 条件跳过 |
|---|---:|---:|
| Application | 1162 | 0 |
| Infrastructure | 260 | 2 |
| Networking | 52 | 0 |
| Protocol | 656 | 0 |
| Pvf | 60 | 0 |
| Persistence | 333 | 0 |
| Host | 6 | 0 |

合计 1162 + 260 + 52 + 656 + 60 + 333 + 6 = 2529 Passed。随后在独立进程清除 PVF_ARCHIVE_PATH，筛选 Infrastructure 的 FullyQualifiedName~A_missing_archive_fails，两项均 Passed、退出 0。比对名称确认它们正是全量中的两个条件跳过项。因此 2531 个适用用例均获得通过结果，没有未覆盖跳过或失败。逐项统计、原始 TRX 哈希和退出码见 [trx-audit.json](final/trx-audit.json)；各原始日志/TRX/退出码位于同目录。

## 成功条件

| 条件 | 决定性证据 |
|---|---|
| SC-1 职业表示 | CharacterStatRuleServiceTests 的往返/非法输入检查及真实档案支持性检查通过。当前生产属性、技能与角色读取使用 CharacterCareer；未声明组合明确拒绝。 |
| SC-2 1—86 属性 | CharacterStatArchiveTests 的独立 token 文本/闭式算式 oracle 覆盖 174 组合、14964 行、每行 19 属性；初始化/取消/重试及共享对象检查均通过。当前日志记录初始化 1026.705 ms、百万次查询 233.435 ms、校验和 20186173950；这是七项目并发回归期间的测量，不作为性能门槛。 |
| SC-3 技能规则 | CharacterSkillArchiveTests 与 CharacterSkillRuleServiceTests 通过，包含剑魂 25 级十字斩、血之狂暴拒绝、元素点燃多值上限、前置、SP/TP、免费成长和只读共享规则。 |
| SC-4 当前点数 | 当前账本、真实规则与临时库测试覆盖两页、等级、奖励变化、未知规则诊断、负余额与协议夹取；静态缓存不含角色已学组合，无持久化余额。 |
| SC-5 当前事实与事务 | CurrentCharacterStateTests 的生产 DI/真实 PVF/临时 SQLite 用例通过：同一角色实例即时读取事务写入、回滚恢复、提交与独立容器重载，共享属性不污染，技能 slot/extra 与未建模动态字段保留。 |
| SC-6 现有流程与范围 | 全部七项目回归及完整独立审查通过；实际 handler 判据覆盖旧快照与当前值不同、无授予结果回落、新手防降级和选本帧顺序。Persistence 生产项目、协议编解码及运行数据未修改，没有新增技能学习协议。 |

真实档案 SHA-256 再次核对为 19F5FA85FF73CCE94E299E0902269DE2F0164934128F15B8A7B99249C881BE1A，与属性规则依据一致。主工作区仍为 main，HEAD 为原基线且状态干净。三个单元的归属清单按顺序叠加后恰好覆盖全部 75 个产品路径，无额外路径、缺失或指纹不匹配；git diff --check 通过。

暂存前后的产品字节与审查指纹一致。默认暂存空白检查将新增 CRLF 文件中的 CR 视为空白，并提示尾部空行；采用单次命令的 core.whitespace=blank-at-eol,space-before-tab,cr-at-eol,-blank-at-eof 识别 Windows 行尾、忽略无语义的 EOF 空行后，75 个产品路径检查退出 0。未改变仓库配置或已审查产品内容，原始证据文件保持原样。

## 声明边界

本结论证明约定的代码重构与回归验收，不声明客户端实机确认。属性成长及相关技能解释以 PVF 和参考代码为依据；装备/Buff 合成、自由决斗的新玩法、多个事务外查询的全局原子快照和整个会话聚合改造不在本次范围。合并、推送、部署与清理需要另行选择。
