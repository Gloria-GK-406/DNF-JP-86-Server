# 实现证据：内存容器改说领域话

实现阶段用会话待办控制，`sdd-lite` 无 plan 阶段；完整变更评审由 review 阶段独立执行。

## 做了什么

### 四个接缝改名换类别（RQ-001）

| 旧 | 新 |
|---|---|
| `ISessionPresenceRegistry` / `SessionPresenceRegistry` | `ISessionPresenceDomainService` / `SessionPresenceDomainService` |
| `IExpertStoreRegistry` / `ExpertStoreRegistry` | `IExpertStoreDomainService` / `ExpertStoreDomainService` |
| `IPvpLobbyRegistry` / `PvpLobbyRegistry` | `IPvpLobbyDomainService` / `PvpLobbyDomainService` |
| `ISelectedSessionDirectory` / `SelectedSessionDirectory` | `ISelectedSessionDomainService` / `SelectedSessionDomainService` |

两个变更动词改用领域说法：`RegisterReplacing` → `Select`，`TryRemoveExact` → `ReleaseExact`。
查询族（`Find` / `FindByCharacter` / `FindByWireUserId` / `FindByConnection`）保持原名——它们
已经是正当的领域提问。文件级移动全部 `git mv`，六处被 git 识别为重命名。

### 拆掉透传壳（RQ-002）

`ISelectCharacterSessionEntryDomainService` 上 spec-001 为通过判据而加的三个逐字透传成员
（`Find` / `RegisterReplacing` / `ReleaseExact`）连同实现删除；
`PrepareSelectedCharacterSessionCommandHandler` 与 `CompensateSelectedCharacterSessionCommandHandler`
改为直接注入 `ISelectedSessionDomainService`。另外四个用同一接口的处理器不受影响。

`ReleaseExact` 上那段解释「为什么是 Exact 而不是按账号」的 XML 文档随成员搬到新家，脚本验证
整块 8 行逐字符未改。

### 最后一个容器操作（RQ-001）

执行者主动指出：`Current()`（整表列举、按 `WireUserId` 排序）在 `Server/src` 下**零生产
调用者**，是四个接缝里唯一一个不回答业务问题的成员——判据放它过去只因为它挂在一个名字合格
的接口上。**这正是本轮要消灭的形状**，故删除（接口 1 处、实现 1 处、15 个测试替身实现、
1 条断言）。全仓 `Current()` 零命中。

`ISelectedSessionDomainService` 最终六个成员：两个领域动词 + 四个领域提问，无整表列举。

### 判据反例的替换（RQ-003）

`ATypeOutsideTheVocabularyIsRefused` 里那条反例原本喂的是 `ISelectedSessionDirectory`。它改名
后恰好成为合格领域形状，纯符号更新会让这条**反例**断言失败。改喂
`ISelectedSessionRuntimeDirectory`——仍在 `Abstractions`、仍是货真价实的运行时容器、仍被规则
拒绝，继续充当「reaching a container」那一类。**判定谓词与 `Exempt` 豁免清单一个字未动**，
反例类别数与用例数均不变。判据文件全文 diff 只有这一行。

### 类别措辞（RQ-001 的散文一半）

接口摘要与类注释里仍在说 directory / registry 的地方改为与新类别一致的说法，**只改类别措辞，
不改它们陈述的事实**（「世代安全」「一把锁」「不含外部技术所以不放 Infrastructure」等原样）。
刻意未改的同类词：指旧系统的 "the legacy directory / registry"，以及摆摊语义的 "expert store"。

一条测试的方法名 `RecipientResolutionIsStableAndOnlyReturnsCurrentRegistrations` 里的
"IsStable" 原指 `Current()` 的排序保证，删除后失去主语，改为
`WireIdAndCharacterLookupsEachFindTheirOwnSession`——按它现在实际断的事命名。同文件局部变量
`directory` 一并对齐。

### `store` 的最后一种读法（RQ-004）

`OnlineStateStore` → `OnlineStateRegistry`。它是 `Application` 内部的运行时内存表，由三个门面
包着、不跨层暴露，`*Registry` 在本仓后缀表里正是「运行时注册表」这一类，归类正确。

## 用例数为什么没有下降

删掉 `Current()` 后总数仍是 2007。执行者如实说明了原因并请求裁决，我核过后采纳：唯一用到
`Current()` 的那个测试方法里，另外两条断言是 `FindByWireUserId` 与 `FindByCharacter` 在**真实
实现上的唯一覆盖**（其余命中全是测试替身的空实现）。整条删除会让两个幸存的、被判定为正当
领域提问的成员失去全部覆盖。故只删 `Current()` 那一条断言，保留另外两条。

**这不是为凑数字而留测试**：删掉的是 `Current()` 的全部覆盖，留下的是另外两个成员的全部覆盖。

## 自证

对最终 diff 的删除行套用改名映射后与新增行做多重集比对，55 个文件的残差全部落在四个已授权
类别之内，无第五类：**35 个文件零残差**（一行未动）；13 个文件是 `Current()` 的纯删除
（0 新增行）；3 个文件是拆壳；3 个文件是文档搬移/措辞/变量名；1 个是判据反例替换。

## 验证

| 检查 | 结果 |
|---|---|
| 构建 | 0 警告 0 错误 |
| 全量判据 | 通过 2007 / 跳过 2 / 失败 0，与 spec-001 后完全一致 |
| 判据谓词与豁免清单 | 未改动（RQ-003） |
| LF | 55 个改动文件无 CRLF |

## 文档（RQ-006）

`AGENTS.md`：`OnlineStateStore` 两处改名；后缀表补入**判别式**——「一个进程内容器属于哪一边，
看接口说的是领域动作还是容器动作，而不是看它内部有没有用字典」，并给出这四处的例子与反面
（对外暴露「取出来、改一改、塞回去」的类型不会因为改名就变成领域服务）。

`Docs/ProtocolSliceArchitectureNotes.md`、`Docs/领域结构重构设计.md` §5 的陈旧符号引用更新。
`Docs/领域结构重构设计.md` §12「第 0 步已完成（2026-08-18）」与 `Docs/Refactor/` 下的引用
**不改**——它们是带日期的历史记载，改了等于篡改当时的记录。

## 留给后续的

- `IEntryInventoryLeaseRegistry` 仍是 write-behind 缓存形状，根契约已记为推迟项。
- `ISelectedSessionRuntimeDirectory` 仍是纯容器——它正是下一个成果要拆的那个聚合的持有者。
