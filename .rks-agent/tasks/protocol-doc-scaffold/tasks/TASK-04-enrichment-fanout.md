# TASK-04 字段语义富化（并行派工）

## Task Unit Contract

**交付物**：以上游服务端实现为证据，为静态提取出字段边界的协议补写"可能含义"；
无依据处保持 `unknown`。

**Spec 覆盖**：RQ-006、RQ-007。

**边界**：只写 `patterns/data/meanings/*.tsv`。每个工作包独占一个分片，不得写协议文档、
索引、提取器或他人分片。

**依赖**：TASK-02。

**完成证据**：全部分片通过 `check-meanings.py`（偏移落在字段边界、置信度合法、来源齐备）。

## Execution Result

上游实现覆盖 313 条协议身份（CMD 192 + NOTI 121，占 1410 的 22%），按上游模块切成 **19 个
工作包**并行派出。产出 **19 个分片、896 条含义**，校验 **0 错误**。

采用「分片作为生成器输入」而非「代理直接改文档」的结构，得到两个性质：
并行写入零文件竞争；重新生成会带上含义而不是覆盖掉它。

**执行者报告的关键事实**（已并入 [失谐记录](../../../../Docs/ProtocolScaffoldDissonances.md)）：

- 枚举名与分发目标可以不一致——`0x00A0` 名为 booster 实际走 `OPEN_SELECTABLE_PACKAGE`，
  `0x03EA` 名为 `BIND_PLUS` 实际是 `COMPOUND_AVATAR_SET`。实现前必须跟进分发目标。
- 相当一部分 CMD 上游根本不解析 body（只打日志或完全不引用），这类协议的 `unknown`
  不是"没查"，而是"上游侧确实没有证据"。
- 多处上游要求的 body 长度与客户端构包长度冲突，两侧都不能单方面采信。

## Executor Self-check

- 三个执行者独立报出同一处工具缺陷：`check-meanings.py` 用 `ty.upper()` 把 `0x` 变成 `0X`，
  与 layouts 的键永远匹配不上，闸门实际恒失败。已修复为按数值规范化。
  **该缺陷被外部独立发现三次，说明分片自校验这一环起了作用。**
- 一个执行者报出提取器的诚实性缺陷：`walk_builder` 静默跳过未识别的直接调用却仍报
  `closed`，写 dstring 的辅助函数会被整段漏掉。已改为跟进调用（深度 3），跟不动的降级为
  `partial / unfollowable-call-in-builder`。修复后 C→S 的 `closed` 由 317 降至 305、
  `closed-parametric` 由 21 降至 9——**24 条协议此前在谎报完整性**。正对照仍 7/7 通过。
- 一个执行者报出含义键不区分顶层与组内字段。已引入 `gN:offset` 作用域并迁移既有分片。
- 3 行因提取器修正后失去落点，用 `--prune` 剔除并留档在 `pruned-meaning-rows.tsv`；
  未做任何"重映射"猜测。
- 一个工作包因 API 错误中断，已重新派发并完成。

## Independent Quick Review

每个分片由其执行者独立自校验（19 份），并由 `check-meanings.py` 统一门禁。
完整结果复核见 TASK-06。
