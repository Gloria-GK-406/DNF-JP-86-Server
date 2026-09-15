---
kind: rks-verification
decision: pass
---

# 完成验证：`town-area-lifecycle`

**被验证的声明**：当前任务分支相对基线 `1e75dc85` 的变更完整交付 TaskContract
revision 1；城镇区域生命周期、回城重同步、同区移动归一和主动退本退队均满足自动化验收，且结果可以提交。

## 决定性证据

- **SC-1、SC-2、SC-5**：区域领域、通知听众、注册和发布定向测试 26/26 通过；区域键、惰性实体、
  lease compare-and-remove、幂等、私有 gate、空实体回收和 Party directory 镜像均在覆盖范围内。
  生产 `Server/src` 中 `SessionPresence`/`ISessionPresenceDomainService` 引用为 0；区域契约和实现中的
  `ListenerPort`、`NetworkConnectionId`、`SelectedSessionIdentity` 引用为 0。
- **SC-3、SC-4、SC-6**：城镇投影、跨区/同区移动、Party 投影、选图/进本/回城/主动退本定向测试
  53/53 通过。额外覆盖最终快照批量回城、旧 revision 重读、replacement gate、进本 gate 取消回滚、
  progress 缺失回滚及投影异常不撕裂组队成员。
- **SC-7**：`dotnet build Server/86JP.Server.slnx -c Release --no-restore` 通过，0 warning、0 error。
  设置真实 `PVF_ARCHIVE_PATH` 后，以 `-m:1` 串行执行完整 Release solution 测试：3264 passed、
  0 failed、2 skipped；两项 skip 均是专门验证“缺失 archive”错误路径的测试。`git diff --check`
  通过。Application→Infrastructure 分层测试包含于完整通过集合，生产项目仅有测试友元名称，未新增
  Infrastructure 引用。
- **独立审查**：`reviews/change-review.md` Round 4 为 `approved`；此前关于旧 roster 覆盖、
  replacement、投影乱序和进本 commit boundary 的所有 finding 均已逐项关闭，未留阻断项。

## 判定

`pass`。SC-1..SC-7 均有当前工作树上的新鲜证据；无需实机步骤才能形成本任务的代码完成声明。
按授权范围只创建任务分支本地提交，不合并、不部署、不清理工作区。
