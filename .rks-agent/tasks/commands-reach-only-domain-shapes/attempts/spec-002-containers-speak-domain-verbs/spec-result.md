---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 内存容器改说领域话

## Delivered

- 会话、在场、摆摊、PvP 大厅四个接缝成为领域服务：名字说出类别，成员是领域动词或带条件的
  领域提问，容器留在实现的私有字段里不出现在接口上。
- **spec-001 为通过判据而留下的那层壳拆掉了。** 两个命令处理器不再隔着一层问同样三件容器事，
  而是直接向拥有会话的领域服务提问。
- 四个接缝上最后一个整表列举 `Current()` 一并删除——它零生产调用者、不回答任何业务问题，
  判据放它过去只因为它挂在一个名字合格的接口上，而那正是本轮要消灭的形状。
- `OnlineStateStore` → `OnlineStateRegistry`，领域层 `store` 一词只剩「事务结算的真源」一种读法。
- 仓库约定补上判别式：一个进程内容器属于哪一边，看**接口说的是领域动作还是容器动作**，
  而不是看它内部有没有用字典。

## Success Evidence

| Condition | Evidence |
|---|---|
| C-1 | `verification/completion-evidence.md`：四个接口成员面逐个核对，无整表列举与容器操作 |
| C-2 | 同上：透传成员与其实现删除，两个处理器直连；评审确认无第二层包装 |
| C-3 | 判据文件全文 diff 只有反例锚点一行；谓词与豁免清单未动；评审另做两次变异均被抓 |
| C-4 | 同上 C-4 行 |
| C-5 | 2007 / 2 / 0 与基线逐项目相同；55 个文件机械对消，35 个零残差，余者落在四类授权变更内 |
| C-6 | `AGENTS.md` 自相矛盾的布局句订正、判别式补入；评审对三份文档机械复扫未发现第二处位置错误 |

## Repository Result

- Changed paths: 59 项，全部在 `Server/`、`AGENTS.md`、`Docs/` 之下（另含本任务的产物目录）。
  六处文件级移动均被识别为重命名，无 CRLF 污染。
- Baseline or accepted result: 基线 `70a3e54e10faccd65ca522f4e22af37355f88f06`
- Parent rollback required: no

## Successor Guidance

- `ISelectCharacterSessionEntryDomainService.ReplaceRuntimeExact` 仍是对
  `ISelectedSessionRuntimeDirectory` 的逐字转发。它早于 spec-001 就存在、且转发的正是下一个
  成果要拆的那个聚合持有者，本轮不动是范围问题而不是遗忘。**不要因为本轮拆了另一层壳就当它
  也解决了。**
- 注入面判据的反例现在锚在 `ISelectedSessionRuntimeDirectory` 上。下一个成果拆掉它时必须换
  锚点，而**换的时候不许顺手放宽规则**——得另找一个仍被拒绝的真容器，否则那条反例名存实亡。
  这条与上一条是同一件事的两面：判据的可证伪性依赖于反例始终是真的反例。
- 本成果亲历的一个教训值得后续沿用：`Current()` 这种「挂在合格接口上的容器操作」是白名单最
  容易被绕过的地方。判据能看的是类型，看不到成员——所以成员层面的收口只能靠人枚举。
- `IEntryInventoryLeaseRegistry` 仍是带 `Version` 与 `IsDirty` 的 write-behind 缓存形状，
  根契约已记为推迟项。

## Artifacts

- `./TaskContract.md`、`./routing-decision.md`、`./sdd-execution.yaml`、`./spec.md`
- `./implementation-evidence.md`
- `./reviews/change-review.md`（verdict `approved`，两轮）
- `./verification/completion-evidence.md`（decision `pass`）
