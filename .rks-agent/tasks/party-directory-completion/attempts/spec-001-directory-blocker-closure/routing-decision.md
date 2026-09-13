# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

目标是一次固定 A21 的证据裁决：分离 ID `0x11` 主目录与 ID `0x14` 候补窗，关闭会影响生产实现方向的
未知项，并给出明确开工 verdict。客户端、输入、排除项与证据等级稳定。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: matching-info、窗口归属、真实数据源和服务端接入裁决必须作为同一个一致结果接受；拆开接受会
  留下互相矛盾的旧结论。一个 Spec 能封存被推翻假设并给后继实现单一输入。
- Reroute conditions: 若证据仍无法区分主目录与候补窗，或主目录必须依赖快速匹配/地下城进入状态机，返回
  根控制器重新划分范围。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `investigation`
- Rationale: 多份静态报告存在冲突，需要显式证据计划、完整结果审查与新鲜验证；无生产 mutation，不需要
  Governed 的更强回滚控制。

