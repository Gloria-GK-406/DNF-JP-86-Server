---
kind: rks-verification
decision: manual_required
---
# 统一容器添加与邮箱迁移

新入口行为测试覆盖材料跨多堆叠、宠物消耗品自动路由、部分可放但总容量不足的整体回滚、未知物品异常、离线持久添加、不依赖placement的账号材料、错误容器归属拒绝。邮箱原有余额/物品守恒、重复领取/容量失败、虚拟格回归共同验证，43项通过。

初轮完整Application测试1984通过1失败（LoginPageKeepsTotalCountAndOnlyReturnsOldestTwentyInReverseOrder），quiet日志未给出断言细节；该测试单独运行通过，尚不能认定第一次失败原因。最终完整重跑记录于test-container-Application-final.log，不删除首次失败日志。Protocol前一轮1108通过；最终检查另记。

此变更未部署，仍运行mailbox-open版本。之前已发布但未启用的mailbox-material版本不应再使用，其仅过滤虚拟格的补丁已被统一容器入口替代。根任务仍待发信协议和实机验收，保持in_progress。

## 最终验证

完整Application重跑1987/1987通过；最终Protocol1108/1108通过。账号材料补充直接读取accounts.cube_clear=3断言后，入口5项测试通过。首次Application分页测试失败保留，单独与完整重跑均通过，未推断其原因。独立审查container-addition-review.md批准当前持久容器范围。

新发布包artifacts/mailbox-container-20260924/runtime已生成，复制当前配置与资源，未停止运行服务/未部署；下一次重启必须用该包，而非已被替代的mailbox-material包。测试期间原来的两份未领材料仍留在运行库中。
