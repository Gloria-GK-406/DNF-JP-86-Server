---
kind: rks-task-contract-review
contract_path: E:\Project\DNF\DNF-JP-86-Server-adventure\.rks-agent\tasks\adventure-group\TaskContract.md
contract_revision: 7
reviewer: Claude Opus 5.5 子代理（全新上下文，未参与建模、意图对齐、路由选择；评审过 revision 6，未参与 revision 7 的起草）
selection_basis: 用户与项目政策没有要求最强评审者；按控制方要求使用新鲜、独立、胜任的评审者
verdict: approved
---

# TaskContract 评审：冒险团按账号派生、通关经验加成与升级推送（revision 7）

## 权威来源集

- 契约 Source Request 第 1–4 项。第 4 项本轮新增末条：静态分析查明服务端无法消除登录动画后，用户在三个选项中选了「本任务里顺带改 ClientHack」。选项说明是：在 ClientHack 仓建分支写补丁，部署到 protocol/protocol2 客户端实测。我拿不到对话原文，以契约引文和控制方背景为准。
- `git diff HEAD` 中 TaskContract 的修订（revision 6 → 7，未提交）。这次修订扩展了 Scope、Authorization、SC-10 和 Requires Confirmation，并新增 RI-12，属于实质修订，revision 6 的批准不能沿用。
- 静态分析证据 `Document/tasks/adventure-group/re-login-banner/`（未提交）：
  - 是一组反汇编转储，包括绘制函数 `d_14E5CF0_draw.txt`、管理器簇 `d_manager_cluster_14E58F0.txt`、实体重启 `d_2672BF0_entity_restart.txt`、动画结束判定 `d_265FED0_anim_end.txt`、NOTI 注册 `noti_regs.txt` 等；
  - 转储中没有结论文档，也没有镜像 SHA-256 标识。
- `DNF-JP-86-ClientHack` 的 `AGENTS.md` 与 `README.md`，要点如下：
  - main 放生产兼容补丁，client-debug-lab 放调试与 VMP，不得用调试配置覆盖生产；
  - 地址以镜像 SHA-256 和 VA/RVA 为证据；
  - 不手工改 exe/dll，从构建产物部署，保留原配置；
  - 构建不等于部署许可；
  - 构建、ctest、`clienthack_stage` 的入口见 README。
- 现场核对：
  - ClientHack 主检出位于 `main`，工作区干净；
  - `DNFClient-A21-protocol`、`DNFClient-A21-protocol2` 的 `plugins/` 下已部署一批插件，含 `0200_GameplayCompatibility.dll` 和此前留下的 `.bak` 备份。
- 服务端仓 `AGENTS.md`、`Document/FeatureDevelopment.md` §4、`protocol/evidence.md`。

## 根意图核对

| Intent | 处置 | 核对结论 |
|---|---|---|
| RI-1 至 RI-10 | 与 revision 6 相同 | 内容未变，处置仍然成立 |
| RI-11 选人界面与 buff 显示、登录误报、「这次一起修」 | in_scope | 成立。登录误报的修正载体从「服务端修正」改为「修正」，再由 RI-12 落到 ClientHack，与用户的决定一致 |
| RI-12 「本任务里顺带改 ClientHack」 | in_scope | 成立。Scope In 写明了三点：在 ClientHack 仓的任务分支上开发，部署前备份并可回滚，只部署到 protocol/protocol2。Authorization 追加的范围与用户所选选项的说明逐项对应：<br>• 建分支：对应「任务分支」；<br>• 写补丁：对应「实现登录动画补丁」；<br>• 部署实测：对应「部署到两个目录」。<br>「本地提交」没有出现在选项原文里，但它是在本任务自己的分支上写补丁的自然组成，和服务端侧的检查点提交同类，不算扩权 |

没有重复或遗漏的意图，没有 `unresolved` 行，也没有 `preserved_upstream` 行。

## Scope Out 与授权边界

- revision 6 中「服务端无法消除时交给用户决定」的那条 Requires Confirmation 已经删除。删除有依据：静态分析给出了结论（ClientStatic，High），用户已在三个选项中作出决定，这个出口已经用完。
- 新增的 Requires Confirmation 条目把以下操作留给用户：
  - ClientHack 仓的合并、推送、PR；
  - 部署到 protocol/protocol2 以外的客户端目录。
  
  这与 ClientHack「构建不等于部署许可」的约定一致。
- 现在 SC-10 只对「装有补丁的客户端」成立。`DNFClient`、`DNFClient-OneKey`、玩家分发包里的客户端在 ClientHack 发布前仍会出现登录误报。这是用户所选选项的直接结果：选项说明只涉及 protocol/protocol2 的实测部署。因此这不是对用户意图的窄化。

## 阻塞性发现

无。

## 非阻塞建议

1. **ClientHack 分支基线与证据规范。**
   - 这是生产兼容补丁，任务分支应从 ClientHack `main` 切出，不要从 `client-debug-lab` 切出。
   - 补丁用到的地址按该仓规范，写明镜像 SHA-256 和 VA/RVA，并更新 `ClientHack/docs/address-provenance.md`。现有转储没有标镜像标识。写入协议记录（SC-10）时，应注明分析用的是哪个镜像。
   - 主检出现在位于 `main`。建议用独立 worktree 开分支，不要直接切换主检出。
2. **部署时替换文件的来源。** 如果补丁加在已部署的插件（例如 `0200_GameplayCompatibility.dll`）里，替换前要先确认现有 dll 是否来自比任务分支更新、或不同的构建，例如 20261112 会话标题那次部署。否则替换可能悄悄回退别的功能。新开一个插件编号可以避免这个问题。契约已经要求备份和可回滚，这条建议只是提醒先核对来源。
3. **部署时客户端正在运行。** 替换 dll 需要客户端进程已退出。应该请用户关闭客户端，不要由 Agent 结束 `DNF.exe`。只结束自己启动并核对过的进程。
4. **补丁误伤真实升级时的出口。** SC-10 要求 SC-5 的真实升级提示照常出现，`0x01A0` handler 走实体重启路径。如果补丁抑制了首播之后，真实升级的重播也被一起吞掉，而任务内修不好，应回到用户决定。契约没有单列这个出口，按通则处理即可，建议在 Spec 或测试流程中写明。
5. **revision 6 评审的建议 1、2 仍未采纳。**
   - SC-5 证据列中「登录后本来就会出现的…字样」这句，对已打补丁的客户端已经不成立；
   - RI-10 的 SC 列仍然缺 SC-10。
   
   两处都只是措辞问题。另外，RI-12、RI-11、RI-10 在表中是倒序排列。

## 五维评估

| 维度 | 证据类型 | 证据 | 后果 |
|---|---|---|---|
| quality | observed | 我核对了以下各项：<br>• revision 7 的 diff，RI-12、Scope In、Authorization、SC-10、Requires Confirmation 彼此一致，并与用户所选选项的说明逐项对应；<br>• ClientHack `AGENTS.md`、README 中的分支、证据、部署约定，契约没有违反；<br>• 两个目标客户端目录存在且已有插件部署；<br>• 静态分析证据目录存在。<br>剩下的是证据规范与部署安全方面的非阻塞提醒 | 可以作为 Spec 修订与执行的输入 |
| efficiency | proxy | 用户选择在本任务内顺带完成，没有另开任务；补丁只部署到两个实测目录 | 不需要再走一轮契约和路由，补丁的工作面限定在一个功能点 |
| cost | proxy | 新增工作：<br>• 一个跨仓分支；<br>• C 补丁与 ctest；<br>• 两个目录的部署与备份；<br>• 一轮实机重登验证。<br>两仓的协调和部署替换有回退别的功能的风险（建议 2） | 成本中等、可控；回滚依赖部署前的备份 |
| cognitive burden | proxy | 契约在 Current Situation 中写明了根因与证据的位置，跨仓边界（只部署哪两个目录、哪些需确认）表述清楚；SC-5 的一句措辞仍有轻微张力 | 执行者能直接据此开工 |
| actual human participation | proxy | 用户的三选一决定只以契约引文和控制方背景的形式出现，我拿不到对话原文。留给用户的事项：<br>• SC-4a、SC-5、SC-9、SC-10 的实机验收；<br>• 两仓的合并、推送、PR；<br>• 部署到其他客户端目录；<br>• 原地迁移、停服、改角色进度；<br>• `0x01A0` 降级 | 本评审不替代这些人工决定与实机反馈。用户明确反馈通过前，不得声称实机验收通过 |
