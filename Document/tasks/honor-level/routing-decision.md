# 执行决策

- TaskContract: `./TaskContract.md`
- Contract revision: `2`
- 契约审核绑定提交前的原检出路径。把任务资料迁入功能工作区时逐文件核对内容哈希，沿用仓库相对路径。

## SDD 就绪性

用户已授权契约和实现。A21 的账号归属、经验转换和出站字段有可核对的规则。需要检验非零已有数据、跨门槛和不同账号的路径，避免只修登录或只修刷图时造成进度回退。

## 结构选择

- Selected workflow: `single-spec-workflow`
- 同一账号经验从结算到客户端展示是一个验收结果。完整结果审查可发现只修一处通知或用收件人账号替代目标账号的问题。
- 如果 A21 证据推翻核心字段或出现独立归属和验收的功能，重新判断范围。

## 控制器选择

- Controller: `sdd-lite`
- Profile source: `focused-delivery`
- 已有经验账本、PVF 内容源及协议投影能承接。用规格记录字段与验证目标并独立审查结果，具体接线和测试次序在实现阶段确定即可。

## Execution Configuration

- Profile: `focused-delivery` with `sdd-lite`; long-running: `no`
- Spec baseline: `cada01a0ec33ad9e3ec34fcad1ce2ca475244fd4`
- Gates: `implementation: auto`, `review: auto`, `verification: auto`
- Bound methods: `implementation: test-driven-development`
- Artifact path overrides: none
