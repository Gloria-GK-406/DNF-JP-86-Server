# `<TYPE>` `<NAME>`

> **Game 端点的协议不要用这个模板**——它们的文档已由静态分析生成，见
> [全量索引](../game/index.md)，直接在既有文档的人工区往下写。
> 本模板用于全手写的新记录（例如 Channel 端点，或任何尚无普查覆盖的协议）。
>
> 复制到 `protocol/<endpoint>/<direction>/<TYPE>.md`，并在
> [协议清单](../catalog.md) 增行。名称若非来自客户端或上游枚举，必须在此注明是
> 本资料库的描述性命名。

## Identity and behavior

| Property | Value |
|---|---|
| Endpoint | `<Game/Channel/...>` |
| Direction | `<Client → Server/Server → Client>` |
| Command | `<hex>` |
| Type | `<hex>` |
| Role | `<Report/Query/Mutation/Session side effect/Notification>` |
| Trigger | `<可观察的客户端或服务端动作>` |
| Expected response | `<none 或确切的响应身份与顺序>` |
| 阶段 | `<未知/确定语义/实现服务端功能/双向验证结束>` |

## Wire layout

帧结构链接到共享 pattern（从 `<endpoint>/<direction>/` 下写作
`[game-frame.md](../../patterns/game-frame.md)`），这里只写 body。先给长度规则，再给字段表。

```text
BodyLength = <常量，或含 count 的算术式>
```

| Offset | Size | Encoding | Meaning | Confidence | Evidence |
|---:|---:|---|---|---|---|
|  |  |  |  |  |  |

写明确切长度规则、可选段、计数、条件分支与非法输入行为。**字段宽度之和必须与实测 body
长度闭合**；不闭合说明读漏了分支或循环。未确认含义的字段按 offset/宽度命名，不要猜。

## Samples

链接 `samples/` 下已去敏的样本，说明产生它的客户端动作。记录重复次数或不同环境的差异。

## Client reversing evidence

| Evidence | Kind | Location | Supports |
|---|---|---|---|
| `E-001` | `<static/dynamic>` | `<DNF SHA-256 + RVA/调用栈>` |  |

包含足以让另一个人复现结论的精简伪代码或指令。区分**观察到的指令**与**推断的命名**。

## Server and historical evidence

旧服 handler、当前实现路径、持久化行为，以及"历史上是否确实没有 handler"是否验证过。

## Implementation mapping

| Layer | Type/path | Responsibility |
|---|---|---|
| Data |  |  |
| Networking |  |  |
| Application |  |  |
| Domain/Infrastructure |  |  |

## Verification

- Parser/codec vectors：
- Boundary cases：
- 条件分支/重复项：
- Response count/order：
- Real-client result：

## Unknowns and next evidence

只列会影响解析、行为、领域语义或客户端兼容性的未解问题，并给出解决每一项**最便宜的证据**。

## 逆向工作日志

过程、反汇编片段、走过的死胡同。死胡同要写——它能避免下一个人重复同样的尝试。
可复用的技巧应提升到 `patterns/` 并在此回链。
