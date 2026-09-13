# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

接受基线已提供可运行实现，剩余结果边界固定为当前 A21 双会话原生 ID `0x11` add/update/delete 及其
wire/Host/client-state 归因。用户动作、禁止 Computer Use、exact-session 和不得将“已发送”冒充“UI 可见”的
限制均明确；若实测失败，合同仅允许同一投影边界内的聚焦修复。

## Marginal Control Value

直接执行最可能把登录成功、服务端发送或静态对象链误当成原生 UI 验收，也容易在用户动作前缺少可关联的
session/log/probe 边界。持久的准备、运行、归因、审查和完成验证能保护唯一不可自动化的验收窗口；其控制
收益明显高于短期流程成本。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: Host/会话准备、一次用户原生操作、wire/client-state/UI 归因和必要修复共同构成一个不可拆分的
  接受边界；任一部分单独通过都不能满足 SC-4。
- Reroute conditions: 若固定客户端身份不成立、双会话所需外部账号/登录能力不可用，或修复需要新目录协议、
  ID `0x14`/快速匹配链、客户端逻辑绕过，则停止并返回父控制器，不扩大本 Spec。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `controlled-change`
- Rationale: 本地 Host/Client 配置与启动是可逆但有状态的操作，且唯一用户动作前必须固定 exact-session、
  观测边界和恢复方式，之后必须根据真实进程/日志/UI 结果做 postcheck。Lite 无 preflight/postcheck，容易把
  不同会话或旧日志误关联；普通独立 review 与 fresh verification 已足够，未涉及不可逆外部资产、密钥或数据
  丢失风险，因此不需要 Governed。
