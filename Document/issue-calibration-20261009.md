# Issue 状态校准：2026-10-09

## 结果

读取 104 条开放 issue 的快照，重点核对旧 bug/实现声明、近期 PR #142 和当前本地成就工作。更新11条 issue，关闭其中 #8；10条继续开放。全部通过 GitHub 回读校验正文、标题和状态；历史记录保留，不用当前实现反写过去的证据等级。

| Issue | 状态 | 校准范围 |
|---|---|---|
| [#29](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/29) | OPEN | 技能实现与待验收分开，关联 #7；保留战斗规则范围 |
| [#30](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/30) | OPEN | CH100 往返验收和旅店记忆实现；不扩展实机范围 |
| [#65](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/65) | OPEN | 结构/Cell 规则与击杀账本进展；完整运行时仍开放 |
| [#73](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/73) | OPEN | 古代地下城 CH100 入口进展；玩法和奖励仍开放 |
| [#90](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/90) | OPEN | 固定成就条件本地增量已部署，实机待验收 |
| [#8](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/8) | CLOSED | 有效击杀明细与结算事实已实现；按该范围关闭 |
| [#7](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/7) | OPEN | 修正旧未接线声明和 01E5 归属；独立01AB及配置验收开放 |
| [#66](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/66) | OPEN | 修正固定188/权限只读旧背景；组队与完整验收仍开放 |
| [#36](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/36) | OPEN | 碎片图鉴验收；制作0313仅请求捕获、尚未实现 |
| [#58](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/58) | OPEN | 已验收称号簿与未验收固定条件增量分开 |
| [#124](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/124) | OPEN | 主动深渊/APC奖励和图鉴拾取进展；多人/随机等仍开放 |

## 核验边界

- main 基线 d0427ce3（PR #142，v0.2.3）；当前 task/achievement-state-events 基线提交4bb4a631的固定条件增量已部署，未合并、实机待验收。
- 本次 DungeonRunDomainServiceTests 专项68通过、0失败，结合源码确认 #8 击杀账本及结算消费；不声称加权金币公式或进程重启战斗恢复完成。
- #7 的独立01AB退点路径不能被001D降级退点替代；01E5是支援兵候选角色查询，归属 #89。
- #66 的账号难度权限有写路径，通关解锁不依赖评分；预置难度选图样本不等于实际通关解锁的实机验收。
- 旧 #13 耳环/宠物槽位、#17 外观过滤差异、#18 孤儿物品槽、#22 初始登录票据180秒、#119 下一地下城等，没有充分的新修复/验收依据，继续开放。新300秒重连宽限不能当作初始票据问题已修复。
- #71/#102 已有当天收口说明，本次不重复覆盖；随机深渊 #140 按用户要求延期。ClientHack 冷启动/长时稳定性风险不因短期验收而关闭。
- 其余尚未启动或范围仍完整开放的 Cycle，不因相邻基础服务已实现而批量关闭。本次不实施这些缺口。

回读收据、原始快照和正文位于根仓库 `artifacts/epic-craft-probe-20261009/issue-updates`。正文比较只规范化CRLF/LF，内容没有因换行差异重复叠加。

同时校准 CH100 与旅店记忆说明，补充 [史诗制作实测](epic-craft-request-20261009.md) 和 [CMD0313协议](../protocol/game/client-to-server/0x0313.md)。不提交玩家数据库或测试附件内容，不重新部署。
