---
kind: rks-verification
decision: pass
---

# 普通学习首版最终验证

2026-09-09。验收主张是普通学习首版代码、协议证据和可供实测的构建已交付；不主张本仓 ClientVerified。基线 da623c2d；完整变更已由 [独立审查](../reviews/change-review.md) 批准。完整回归时全部产品/测试/协议文件与审查输入哈希一致；收口只整理5个新C#文件的末尾空行，未修改任何语义，前后摘要见 [final-formatting.json](final-formatting.json)，最终暂存差异格式检查通过。

## Success conditions

| 条件 | 当前证据 |
|---|---|
| SC-1 | LearnSkillCodecTests 的5份真实请求逐项解码，畸形长度/损坏校验拒绝；399/403字节边界、页/模式/退点/零增量及u16高位保留。领域测试证明格式有效但不支持的请求不写库。原始样本位于 protocol/game/client-to-server/samples/0x001D-20260909-1..5.hex。 |
| SC-2 | CharacterSkillLearningTests 覆盖当前职业、等级、固定技能、前置、当前页和SP/TP预算，调用共享规则。真实PVF守护者85级初始技能1从1到3成功，预算差与共享累计成本一致。前置接受、等级和费用基于最终图；新槽分配仍依请求顺序。 |
| SC-3 | 真实SQLite独立连接重读、第二次写入失败及更新0行的整包回滚，已有slot/extra和另一页/另一角色保留。SkillLearningCommandTests 经实际CommandBus验证并发增量、事务提交先于ACK、发送失败不回滚已提交学习。 |
| SC-4 | 成功编码逐字节匹配历史32字节应答，含附加指令分支与页余额钳位有测试；真实TCP未选角请求得到00 03，随后请求和EXIT仍正常。独立评审重新检查客户端成功/失败读取和MD5长度门；03含义仅为项目兼容选择。 |
| SC-5 | Release构建0警告0错误；7个项目完整回归及缺档场景补测，2597个独立用例均有Passed结果。可执行构建及下一步实机步骤见下。 |

## Fresh regression

每个项目执行 `dotnet test Server/tests/<project>/<project>.csproj -c Release --no-build --no-restore --logger "trx;LogFileName=final-<project>.trx" --results-directory .rks-agent/tasks/ordinary-skill-learning/verification --verbosity quiet`。

完整回归设置 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`；随后移除该进程环境变量，只对 Infrastructure 的 `FullyQualifiedName~A_missing_archive` 补测。

| 项目 | 有档案Passed | 有档案Skipped | 缺档补测Passed |
|---|---:|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 | — |
| Infrastructure.Tests | 260 | 2 | 2 |
| Persistence.Tests | 333 | 0 | — |
| Application.Tests | 1210 | 0 | — |
| Protocol.Tests | 673 | 0 | — |
| Networking.Tests | 53 | 0 | — |
| Host.Tests | 6 | 0 | — |

有档案合计 60+260+333+1210+673+53+6=2595 Passed；两项缺档补测均Passed，合并独立用例为2597。8个TRX逐项UnitTestResult与Counters对照一致，无未通过的最终适用用例。完整回归进程与缺档补测进程均退出0。

原始结果（包括预期RED和已分类的测试准备失败）保留于 [test-results.zip](test-results.zip)；各原文件、归档与Release产物SHA-256见 [completion-summary.json](completion-summary.json)。解压后可按各阶段记录的文件名复核。原始日志未直接加入Git，压缩包是保留它们的交付证据。

## 可实测构建与边界

- 构建：`Server/src/Dfo.Server.Host/bin/Release/net10.0/Dfo.Server.Host.exe`，在当前任务工作树内；配套Application与Protocol DLL摘要已记录。
- 切换时应继续使用原数据库 `E:/Project/DNF/DNF-JP-86-Server/Server/src/Dfo.Server.Host/bin/Debug/net10.0/Data/dfo-server.db` 和上述A21 PVF，不能误启新空库。切换原运行Host及占用7001/10010/10011端口仍需用户选定；此次未替换实例或写入用户角色。
- 用户实测顺序：普通页现有技能一次增加多级；再尝试同包新增/升级多个技能，核对等级与SP/TP；正常退出再登录，确认持久化显示；尝试未支持的退点，核对无状态改变及界面恢复。服务端日志保留技能ID、最终等级、余额或精确拒绝理由。
- 尚未观察本仓成功后的客户端显示、附加指令实际读取效果、失败后的可见提示或界面恢复。退点/初始化/切换解锁/自由决斗和特殊职业专有模式不属于本首版。错误03不保证弹出具体原因文本。

主工作树保持干净且HEAD仍为da623c2d；任务仅修改本次声明的文件。协议机器区保持原样，新增相对链接均解析存在，`git diff --check`通过。
