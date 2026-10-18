---
kind: rks-review
verdict: approved
---

# 婚房交互与婚戒通知独立审核

审核对象：2026-10-18 当前未提交工作树；服务端基于 `2510330a4a284f0ce0a13af7d67bd576005d11b3`，ClientHack 基于 `6a6baac9715f5ba427dd3444a800485631901d83`。覆盖服务端 13 个代码/测试修改、ClientHack 的 CMake/模块接线修改及新增 `housing_interaction_fix.c`、`test_housing_interaction.c`。采用 `reviewing-work/SKILL.md` 与 `references/code.md`；只读检查代码、运行已有客户端测试产物，没有构建、提交或部署。

## 已解决的阻塞发现

首次审核发现 `MarriageEngagementPayload.InfoState` 仍把婚戒等级写入 03D0 正文 `body[5]`。固定 A21 镜像中，`008959B0` 将 `raw[14]` 读入中间结构 `+0x14`，`008952E8..008952EB` 再写入婚姻管理器 `+0x70`，`00895346..0089534A` 同步角色 `+0x386`；03F4/03D1 共用的 `00892960` 也写这两个婚戒字段。原来的恢复用 03D1 掩盖了该偏差，移除后会把婚戒恢复成正文末字节 0。

复审已确认生产改为 `result[14] = snapshot.RingLevel`，`body[5]` 保留 0；新增恢复测试改为断言 `frame[29] == 1`、`frame[20] == 0`。该阻塞已消除。随后复核两仓新增实施文档与三个 protocol 更新，均与代码及静态证据一致。修正后应用回归日志 334/334，协议回归日志 1543 通过、60 条件跳过；正确偏移断言在修正生产前得到 Expected 1 / Actual 0 的失败。本代码审核不替代部署核验或实机验收。

## 通过的检查

- `HousingReadView.InfoAsync` 直接经关系账本读取对象、阶段、结婚时刻、个人心意点、共享婚戒等级，不加载配偶外观，不回调婚姻用例或效果编排；新读取路径没有形成 DI 环。
- 选角、当前角色外观重建与恢复投影改发静默 03D0；升级购买者仍由成功 CMD03F4 驱动原生事件，`HousingChangedHandler` 仅向另一方投递一次 NOTI03D1。新增测试分别覆盖当前事实和本人/配偶投递边界。
- 原生命中函数 `0087FC90` 为 ECX-self、两个栈参数、`ret 8`；布局查询 `0273CF60` 为 ECX-self、一个栈参数、`ret 4`。两个 fastcall 适配与此一致。`_ReturnAddress() == base + 0087FD7E` 仅过滤 hover 中的布局查询，返回 NULL 后原生循环继续；其它布局查询仍返回原结果。
- `01F01820` 查询 UI 管理器 `+0xFC + id*20` 的活动窗口 vector 是否非空。`00880B60` 仅在成功状态且 flag 0 时调用 `008806D0` 打开 01BD；不是查询缓存窗口分配。`0273D360` 原生检查布局是否含 0/1，补丁复用该函数过滤地板/壁纸。
- 婚戒改动仅将 `008929F9` 的条件跳转改为两个 NOP；原生当前角色存在检查、等级赋值、后续属性/UI 刷新保留。被跨过的门只包围提示文字构造/显示。
- 所有新增签名与固定镜像匹配；模块在写入前统一验证。宿主 plugin checkpoint 在安装失败后撤除已装 hook 和 patch，成功前不会保留部分模块改动。
- 独立运行已有 x86 `clienthack_housing_interaction_tests.exe` 成功。测试中的可执行调用桩保留 `0087FD7E` 返回地址与 callee-pop ABI，覆盖关闭/成功打开/地板壁纸/普通家具/关闭后及其它调用点。

## 证据与残余边界

只读工具 `Document/outputs/marriage-housing-20261018/a21/a21lib.py` 自动核验 EXE SHA-256 `CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14`、dump SHA-256 `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`，本审核直接反汇编上述地址核对，证据等级为 ClientStatic。

客户端测试的窗口、布局和特殊类型查询为 stub，验证的是 hook 控制流与 ABI，不是完整原生容器或实际 UI。真实窗口关闭/跨房、双端互斥失败、家具重叠区域、实际升级一次提示、跨房/重登/外观/请求重放无提示，仍需要用户双端实机验收。本审核不宣称 ClientVerified。
