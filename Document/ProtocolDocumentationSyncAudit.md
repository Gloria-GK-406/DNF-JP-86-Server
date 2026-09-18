# A21 协议资料与当前服务端接入同步审计

日期：2026-09-16。对象：`DNF-JP-86-Server` 的 Game 协议资料库与当前
`Dfo.Server.Protocol` 接入面。

## 结论

当前服务端接入面已能被资料库完整看见：77 个已注册 C→S decoder identity、43 个
encoder-backed S→C CMD identity、82 个 encoder-backed S→C NOTI identity，未登记差集与缺页
差集均为零。这个结论只代表**文档覆盖了当前实现**，不代表 202 个 identity 的编号、布局、
响应关系或客户端效果全部正确。

本轮新增同步检查、补齐 `0x0084` 手写协议页、更新 catalog 的实现覆盖机器区，并修正
`0x0003`、`0x002D`、`0x0048`、`0x0117` 等已与代码或 A21 证据冲突的陈述。资料库当前静态普查
口径为 C→S 1411 个 MakeHeader 调用点，其中 1391 点解析为 762 个 type、20 点保持未解析；
S→C 1515 个有效注册点 / 1510 个 identity（CMD 671、NOTI 839）。全量索引另把 84 个只有响应
handler 的 CMD type 纳入交换页，所以生成 846 个 CMD 页；旧 A12 的 604/538/728 只作为明确
标注的历史里程碑保留。

## 审计来源与判定边界

| 来源 | 本轮用途 | 不能单独证明 |
|---|---|---|
| A21 `protocol-identity.tsv`、构包/读取原语与反汇编记录 | 身份、静态可见布局、普查覆盖与盲区 | 真实运行必然触发、业务含义和客户端最终效果 |
| 已归档帧、读取 trace 与流程记录 | 线上字节、方向、顺序和已观察分支 | 未采样分支及全部业务语义 |
| 真实 A21 客户端行为 | 玩家可见结果和客户端是否接受 | 没有逐帧归档时，不能反推每个字段 |
| 当前 decoder/encoder/handler 与测试 | 当前实现接入、可编码形状和回归保护 | 协议号、线布局或 ClientVerified |
| 上游/旧服 | 命名和待验证业务假设 | A21 线协议事实 |

成熟度按上述证据逐项裁决。代码存在只写“已实现服务端功能”；`ClientVerified` 必须绑定已观察
的真实客户端路径，并写明没有覆盖的分支。机器区在更强 A21 证据完全闭合后可以修正，但优先
修生成输入或生成逻辑，避免错误再次生成。

## 同步前漂移

同步检查的首次清点发现：

- 31 个已注册 C→S decoder identity 未被 catalog 覆盖：`0013`、`001F`、`0021`、`0022`、
  `0024`、`0025`、`0026`、`0027`、`002A`、`002B`、`002C`、`002D`、`002E`、`0030`、
  `0045`、`0048`、`0075`、`0078`、`007B`、`0084`、`00BF`、`00D9`、`011B`、`01E4`、
  `02BE`、`02EF`、`035C`、`039D`、`045C`、`04D7`、`04F1`。
- 54 个 encoder-backed S→C NOTI identity 未被 catalog 覆盖；协议页其实都存在，缺的是“当前
  服务端可发送”这一实现覆盖视图。
- 77 个已注册 C→S identity 中只有 `0x0084` 没有协议页。A21 三个真实空 body 帧证明了该
  identity；它的 builder 位于静态 MakeHeader 普查不可见区域，所以作为生成器声明的手写例外。
- 验收还识别出两个已有但未被例外清单承认的证据页：实帧确认、静态不可见的 C→S `0x0075`，
  以及专项逆向留下的独立 CMD `0x0040` 响应页。两者现已进入生成索引和显式例外清单，不再靠
  “检查器没看到”维持。
- catalog 和总览混用了 A12 历史计数、A21 静态普查数、枚举 `END` 值与当前服务端接入数。
  现已拆开：A21 线上 `END` 值为 CMD 1271、NOTI 1218，不叫“容量”或“已注册数量”。

## 目标条目裁决

| Identity | 当前裁决 | 仍缺的最便宜证据 |
|---|---|---|
| C→S CMD `0x0003` | 当前服务端 typed pipeline 已实现；A21 七帧线级退出序列可复放。未把实现测试写成真实客户端完整退出已验证。 | 正常游戏内退出一次，归档双向帧与客户端落回行为。 |
| C→S CMD `0x002D` | `MoveMap` decoder/handler/测试已存在，旧“未实现”结论删除；尚未授予 ClientVerified。 | 在合法传送点执行一次移动并归档请求、响应/通知和最终地图。 |
| C→S CMD `0x0048` | A21 静态身份和参考帧可解释，当前实现与测试已映射；真实 A21 客户端接受仍未归档。 | 触发对应离开/返回分支，抓取请求及后续状态通知。 |
| C→S CMD `0x0084` | 三个 A21 空 body 帧直接确认请求身份/长度；`[01]` 响应仅由参考与当前 encoder 支持，保持 High。 | 在当前服触发返回城镇，归档 CMD 回包和客户端地图状态。 |
| C→S CMD `0x0117` | 当前服务端有意原样消费并不响应；这只证明实现策略，动态/多态 body 布局和业务语义仍 Unknown。 | 在 A21 读取原语上按 type 取一次运行时 trace，再与触发场景对照。 |
| C→S `0x0450` / `0x00D0` | 交付后真实 A21 客户端确认赛丽亚魔盒批量/单开成功，日志摘要出现幸运值迁移；仅已观察成功路径标为 ClientVerified。 | 归档成功帧、完整日志和背包差分，覆盖幸运触发与逐项 UI；00A0/0207 另行实测。 |

## 自动检查

`protocol/patterns/data/check-server-documentation-sync.py` 从当前 C# 源码提取注册 decoder、直接
encoder 注册与状态同步泛型注册。无法通过局部常量静态解析的 19 条动态/多载荷发送映射明确列在
`server-wire-overrides.tsv`，每行必须指向一个真实注册 symbol 与存在的源码文件。检查器随后验证：

1. 所有当前 C→S/S→C identity 都能在 catalog 找到；
2. 每个 identity 按方向和 CMD/NOTI 族解析到正确协议页；
3. catalog 内全部本地 Markdown 链接存在；
4. `--self-test` 通过受控源码执行注册提取与对账，注入缺失的 C→S/S→C identity、
   无法解析的注册调用及缺失源码根目录，必须被检出。正常检查还要求每类注册非空，
   并核对当前注册调用形状的数量与实际提取数，防止提取器整体失配时伪报零差集。

正常运行结果为 77 个 C→S、125 个 S→C（43 CMD / 82 NOTI）、19 条显式 override、0 failures。
全量脚手架验收为 1685 个静态 inventory identity + 5 个有证据的手写例外，1690 个文档、0 缺失、
0 未声明额外页。

### 检查盲区

- 这是基于 C# 语法形状的仓库内检查，不是完整编译器调用图；已知调用形状失配会失败，
  但新增不同名称的注册 API 时仍必须扩展提取器。
- override 校验确认 symbol 已注册且来源文件存在，但不会证明表中 type 真是运行时发送的 type；
  这些行仍需代码审查，疑点最终用线级帧裁决。
- encoder 可产生某 identity 不代表业务路径可达，也不代表客户端会接受。
- 链接检查验证文件存在，不验证标题锚点或文档内容语义。
- 静态普查对混淆区有盲点，`0x0084` 说明“差集为零”仍不等于客户端协议全集封闭。

### 外部生成输入

完整重建依赖未入库的 A21 内存 dump 和上游枚举源。旧脚本把它们写死到已经不存在的 D 盘路径；
本轮改为通过 `DNF_A21_DUMP_PATH` 与 `DNF_A21_ENUMS_PATH` 注入，并让嵌套的再生成安全检查继承
同一组冻结输入。现存 2026-08-22 与 2026-09-06 两份同版本 dump（DNF.exe SHA-256 均为
`1F8244D3...B20B00DD`）独立产出了逐字节相同的 `protocol-identity.tsv`，排除了“换本地 dump
导致 identity 漂移”。RVA/原语仍是 A21 脚本常量；跨客户端版本尚未形成通用 profile。

## 复现命令

从仓库根运行：

```powershell
python protocol/patterns/data/check-server-documentation-sync.py --repo-root .
python protocol/patterns/data/check-server-documentation-sync.py --self-test
python protocol/patterns/data/check-acceptance.py protocol/patterns/data/protocol-inventory.tsv protocol/patterns/data/protocol-layouts.json protocol
python protocol/patterns/data/check-a12-residue.py protocol
python protocol/patterns/data/check-controls.py protocol/patterns/data/protocol-layouts.json
python protocol/patterns/data/check-regeneration-safety.py protocol/patterns/data protocol
```

重新生成前后还应检查工作树，确认只有有意的生成输入/产物变化，手写区没有被改写。同步检查
生成的 catalog 区应通过 `--write-catalog` 重建，不手工维护覆盖行。
