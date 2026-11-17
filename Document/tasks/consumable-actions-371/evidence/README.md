# 取证输入、产物与复现

本目录不含玩家数据库、账号凭据、游戏二进制或新实机结果。JSON是只读重新扫描、静态摘录、范围快照和实现判断，不是通过报告。

| 文件 | 内容 |
|---|---|
| [issue-371.json](issue-371.json) | GitHub issue状态与范围正文快照 |
| [pvf-actions.json](pvf-actions.json) | 43041个stackable逐条解析；保存1836个带action的模板、文案、参数、根标签及选定限制字段；另含amplifyitem材料子表；解析错误为空 |
| [scope.json](scope.json) | issue范围表9组、79个action type、819个模板，逐类代表及全部参数组合 |
| [action-decisions.json](action-decisions.json) | A001～A079的建议实现、owner、批次、取证门槛；验收状态全部未执行 |
| [reference-matches.json](reference-matches.json) | 本仓/上游/第三方的逐标签源码定位、行号、文件SHA；70参考的标签字符串存在性 |
| [client-static.json](client-static.json) | 当前b1身份、VA窗口、原始指令字节、反汇编和字符串搜索结果；不是全动作映射表 |
| [source-manifest.json](source-manifest.json) | 本仓/上游基线、客户端/PVF身份、原未入库盘点文件身份和证据产物SHA |

## 可重复的只读采集

在本任务worktree根执行，所需依赖为本仓.NET SDK、Python、pefile和capstone；不启动服务或客户端，不访问玩家库：

```powershell
dotnet run --project Document/tasks/consumable-actions-371/scripts/Survey.csproj -- `
  E:\Project\DNF\ClientStore\mounts\dev\content\Script.pvf `
  Document/tasks/consumable-actions-371/evidence/pvf-actions.json

python Document/tasks/consumable-actions-371/scripts/analyze.py
python Document/tasks/consumable-actions-371/scripts/render_matrix.py
python Document/tasks/consumable-actions-371/scripts/verify.py
```

首次采集issue使用只读`gh issue view 371 --repo Gloria-GK-406/DNF-JP-86-Server --json ...`并保存在本目录。scope取自该快照，不是每次默默取最新范围。

先运行analyze生成scope，render生成矩阵/判断，最后verify核对字段、链接和采集身份。重新采集后需再次运行analyze更新产物哈希，再运行verify。脚本不能替代人工复核规则语义与实机。

本轮hash与版本是固定调查快照。以后main、组件或PVF改变时先记录新身份并复查差异；`mounts/dev`本身会跟随通道，路径相同不代表内容仍相同。原二进制不拷贝进任务目录。

## 搜索与证据的限制

- reference-matches中的字面量未命中不能证明功能没有实现；共享规则、通用数据或其他命名可能承担同类责任，调查正文已结合相关owner代码判断。
- 70字符串存在只用于定位，不表示对应动作可用；未命中也不能独立证明全部70功能缺失。
- b1中的字符串搜索没有命中，不能推导A21不支持某活动。静态窗口仅覆盖相应路径，未得到完整转换表。
- PVF扫描保存的是选定字段和完整action参数，不等于所有游戏规则表已解码。缺失外部奖励表/活动配置、目标字段含义和wire参数时，矩阵明确保留取证门槛。
- 原调查`Document/tasks/consumable-use-audit-20261117/`仍在主工作区，未把其未入库内容直接当成已验证协议事实。本目录重新采集的数据足以复核范围、代表与本次实现判断。
