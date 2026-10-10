---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# 史诗图鉴制作规格

## Outcome and scope

收到合法CMD0313后，从PVF输出装备对应配方制作一件；账号图鉴、共享/携带材料与装备保持原子性。客户端和PVF不变，随机兑换和溢出邮件不纳入。

## Requirements and acceptance

| ID | Requirement | Acceptance |
|---|---|---|
| RQ-001 | 规则读取epicpieceinfo的output/recipe，核对piece list及正数成本 | AC-001: 实际黄金石配方完整且非硬编码；未知/错误配方拒绝 |
| RQ-002 | 当前已选角色只能消费其账号/合法携带及共享材料；仓库不作为成本 | AC-002: 材料不足、过期/锁定、碎片不足、未知目标不能扣除或生成 |
| RQ-003 | 一次成功产出一个独立装备；全流程同事务，满装备背包拒绝 | AC-003: 成功余额、真实UID与新槽正确；背包满/生成失败/提交失败回滚；既有请求序号重放不再执行 |
| RQ-004 | 成功CMD0313 body01；失败00+原因码；同一即时投影先ACK、0465 mode1最终余额、000E最终物品/空槽 | AC-004: 消费到0也明确清除图鉴和共享材料；不复用开盒ACK的客户端本地扣除假设；拒绝不发送成功通知 |
| RQ-005 | 业务由领域服务编排，规则读PVF，协议层仅路由/编码；变化经EventService | AC-005: 构造器接线/职责检查、普通制作与图鉴拾取回归通过 |
| RQ-006 | 发布前准备新程序和回滚程序，切换保留当前DB并验证真实运行身份 | AC-006: 程序版本/路径/监听与HTTP ready核验；SQLite完整性和余额快照未被部署改变；用户实机待测 |

## Protocol evidence

ClientStatic: A21 CMD handler RVA00D3C1D0只使用结果/错误并清除目标缓存；0465消费者00D630B0读取mode、u32条目数、u32碎片ID与余额，mode0先清目录。错误04/16/其他是三条文案分支，实现采用明确映射，不把上游所有错误统一04作为客户端依据。

黄金石请求已WireCaptured；成功/失败与UI刷新仍为CurrentImplementation，实机验证由用户后续完成。背包满与缺料在客户端文案上的具体效果不扩大静态结论。
