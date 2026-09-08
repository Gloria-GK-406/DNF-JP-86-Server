---
kind: rks-verification
decision: pass
---

# spec-004 完成验证

2026-09-08，分支 `every-frame-draws-the-current-character`，待验收基准 `1d887c59`。
独立复审 `change-review-r2.md` 已 approved；其后新鲜全量输出为 `full-suite-final.txt`。
执行前确认无 Dfo.Server.Host 进程，PVF_ARCHIVE_PATH 指向 E:/Project/DNF/DNFClient-A21-protocol/Script.pvf。
命令：`dotnet test Server/86JP.Server.slnx --nologo --logger "console;verbosity=detailed"`，退出 0。

| 条件 | 当前证据与结果 |
|---|---|
| C-1, C-2 | reader-enumeration.md 的基准清点：16 读点，12 文件；完整结果/工厂/PvP 边界均已追踪，独立复审核实。仅两处取快照装备。 |
| C-3 | Opening_dungeon_selection_after_dressing_reads_the_current_equipment 通过；真实副本选择 handler 在穿上后返回新装备、脱下后返回空装备。P2 去掉刷新后该界面断言失败。 |
| C-4 | reader-enumeration.md 对三候选逐一比较：只修副本屏会漏 PvP，拆快照改动过大；每次移动加载并只替换库存。 |
| C-5 | 其他八成员身份保留测试通过；真实目录 true/false 持久化标志均不变。并发覆盖不在该无交错证明内，复审明确接受已披露的现有聚合风险，不声称已消除。 |
| C-6 | behavior-change-list.md 双向列出成功、拒绝、Unchanged、会话缺失、加载失败、新增代次拒绝抑制外观及并发覆盖条件；旧 BF-2/BF-6/BF-7 复审通过。 |
| C-7 | P1–P4 精确脚本、JSON 与日志齐全，1/6/2/1 条预期失败；包含成员身份与界面数值不等。逐次 binary 还原哈希相同，最终定向 17 条通过。 |
| C-8 | Move 注释、AGENTS 七调用清单、DungeonSelection 注释、历史调查与两处协议记录均按实际代码更正；清单基准和异常解释已修正。 |
| C-9 | 复审后全量 2417 通过、2 跳过、0 失败；比 2409/2/0 基准多 8，均在 Protocol。完整输出保留。 |

通过数为 Protocol 631 + Networking 52 + Pvf 60 + Persistence 333 + Infrastructure 259 + Application 1076 + Host 6 = 2417。
跳过两项为载入真实 PVF 时不适用的 missing-archive 判据，日志给出具体名称。7 个测试程序集均执行，全文无 [FAIL]、error CS、MSB数字标记。

产品差异仅 Move 的刷新、DungeonSelection 现状注释与移动测试；未改字节锚、两份架构断言、协议布局、注册表或旧六处快照写入行为。这些守卫在全量输出内通过。其余差异是记录更新与任务证据。reviewed-state.json 的七个产品/现状文档 SHA-256 在独立复审后及本次测试后均一致，git diff --check 通过。

边界：主仓 main 仍为 addbeb52；未触及 CRAP、客户端、只读参照或其他任务 worktree。没有发布、合并或推送。源码变异恢复均以文件备份和 SHA-256 验证，没有 Git 恢复命令。

已披露限制：库存重读/代次校验失败时只发已提交移动确认，快照可保留旧值；整份快照并发覆盖风险仍存在；PvP 仅有源码证据，未增加实机验证。首次测试编译失败及四组预期变异失败的根和输出见 behavior-change-list.md；复审后全量无新失败。
