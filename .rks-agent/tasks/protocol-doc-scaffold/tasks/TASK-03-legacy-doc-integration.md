# TASK-03 既有文档整合与官方名增补

## Task Unit Contract

**交付物**：既有手写文档并入新的身份命名方案；因上游枚举进入工作区而可升级的名称完成增补；
资料库导航更新到全量索引。

**Spec 覆盖**：RQ-004、RQ-010、RQ-013、RQ-015。

**边界**：只对既有文档做**证据支持的增补与重命名**，不得推翻其既有结论、删除证据、样本或
逆向工作日志。

**依赖**：TASK-02。

**完成证据**：既有结论完整保留；名称增补均标注新证据来源；无失效链接。

## Execution Result

**重命名（`git mv`，内容零改动）**：

| 原路径 | 新路径 | 理由 |
|---|---|---|
| `server-to-client/0x0001.md` | `server-to-client/CMD-0x0001.md` | S→C 两族共用 type 编号，文件名须带族标识 |
| `server-to-client/0x0289.md` | `server-to-client/NOTI-0x0289.md` | 同上 |
| `server-to-client/samples/0x0001-…hex` | `samples/CMD-0x0001-…hex` | 与其文档一致 |

6 处入链已更新，残留引用检索为零。

**发现并修复一处身份重复**：生成器为 NOTI `0x0289` 新建了文档，而手写文档还在旧路径，
同一身份出现两篇（违反 RQ-002）。删生成稿、把手写稿改名占位；此后生成器因该路径无机器标记
而自动跳过，问题不会复发。

**官方名增补（7 篇）**——全部取自上游 `CmdPacketType` / `NotiPacketType`，并保留旧名映射：

| 协议 | 原记载 | 官方名 |
|---|---|---|
| `0x008F` | 未命名 | `CHANGE_TUTORIAL_FLAG` |
| `0x00C2` | `CLIENT_SPEC_PROFILE`（描述性） | `FRAME_LAG_STATISTICS` |
| `0x00FA` | 未命名 | `LODING_TIME_REPORT` |
| `0x0117` | 未命名 | `LAG_STATISTICS` |
| `0x01DD` | `CLIENT_HARDWARE_DETAIL`（描述性） | `COMPATIBILITY_INDEX` |
| `0x02A8` | 未命名 | `LOAD_EXTEND_CHARACS` |
| NOTI `0x0289` | 荣誉等级下发（描述性） | `HONOR_LEVEL_INFO` |

其中 `0x00C2` 的官方名反过来印证了该文档的核心证据链（`LagLog.txt` 同源），
`0x0117` 的官方名给出了其多态载荷 kind 的实质线索。

**导航更新**：`README.md`、`catalog.md` 区分"全量地图"与"进度清单"；
`game-frame.md` / `game-receive.md` / `character-lifecycle.md` 的普查引用改指统一普查，
并把 game-receive 中"动态注册点尚未写回"的缺口标记为已闭合。四份被统一普查严格超集覆盖的
旧产物（两个脚本、两份 TSV）已移除。

## Executor Self-check

- 重命名全部经 `git mv`，Git 记录为 `R`（rename）而非删除+新增，内容哈希不变。
- 名称增补只动 H1 与其下的命名说明段，正文、证据表、样本、逆向工作日志一字未改。
- 每条增补都写明了新证据来源与"名称到位不等于字段到位"的边界。
- 移除旧普查产物前检索了全部残留引用并逐一改指，检索结果为零。

## Independent Quick Review

见 TASK-06 的完整结果复核。
