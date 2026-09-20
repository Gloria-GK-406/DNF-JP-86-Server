---
kind: rks-verification
decision: pass
---

# 验证结论

验证主张：本目录给出 GH #101 所需的 A21 PVF 普通地下城准入/疲劳静态取证，能够从指定归档复跑，且不把 PVF 声明或当前服务端投影冒充为原版服务端政策。

## 条件与证据

- 输入身份：2026-09-20 重算 `DNFClient-A21-protocol/Script.pvf` SHA-256 为 `19F5FA85FF73CCE94E299E0902269DE2F0164934128F15B8A7B99249C881BE1A`，长度 72,894,573 字节；debug/protocol/protocol2 三份归档同长同哈希。
- 可复跑：`dotnet run --project .\PvfAdmissionProbe.csproj --configuration Release -- <Script.pvf>` 退出码 0，输出 1047 行 `dungeons.tsv` 和 `summary.md`。
- 清单断言：1047 行、1042 个文件声明 minimum level、506 个文件声明 no fatigue、346 个文件可投影 Maze、984 次合法 quest connection；候选 144、145、10000、10004、100、5006 的等级/疲劳/Maze 分类均与清单预期一致。
- 独立抽样：现有 `Tool/PvfProbe` 逐项读取上述六个 dungeonId，确认 144/145 的多 Maze 与 quest connection、10000/10004 的 `[no fatigue]`、100 的 event/required item、5006 的 license/required item。
- 消费核对：在基准提交 `28efa005fa5945d0c8d27de8076ac50ea83f2c7f` 上全文检索生产代码；`PvfDungeonDefinitionSource` 读取 minLevel/noFatigue/Maze/questConnection，生产路径不消费前两者，`DungeonLayoutRuleService` 消费 Maze/questConnection；难度标签不由该内容源读取。
- 范围：任务新增内容仅位于 `Document/outputs/issue-101/pvf`；未改生产代码、协议文档、客户端或运行数据库。
