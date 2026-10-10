# 0.2.4 阶段收尾

## 授权与范围

2026-10-11 用户授权提交当前成果、提升 PATCH 版本、创建并合并 PR，并从主干打 tag 发布 Release。基线为 main d0427ce3；工作分支 task/monster-declaration-parser。版本 0.2.3 → 0.2.4，同一 PR 只提升一次。

本次集成分支累计的单人玩法成果：成就固定事实补检、史诗制作、装备品级与魔法封印调级、装扮合成与透明装扮外观、循环地下城、任务击杀目标解析、镜像阿拉德、入场次数与 DirectAsync 事件、异界融合副本与掉落、装备／装扮开孔镶嵌，以及相关协议、内容修复工具和探索记录。黄龙／青龙、绝望之塔与祭坛等本次仅收集证据，未实现。

## 验收边界

- 循环地下城：单人基本循环已验收；完整失败矩阵、组队与长期重复待后续。见 [循环收尾](CycleDungeonCloseout-20261010.md)，GitHub #68 保持开放。
- 镜像阿拉德：单人入场、次数、地图组装、等级与经验已验收；组队与全候选覆盖未宣称完成。见 [镜像收尾](MirrorAradCloseout-20261010.md)，#70 保持开放。
- 异界：一期基本可玩与掉落已验收；机制协调单列 #145，#74 保持开放。见 [异界收尾](DimensionDungeonCloseout-20261010.md)。
- 开孔：装备与装扮开孔及说明文本已实机验收；镶嵌／替换／合成等完整边界仍见 #53。见 [开孔记录](EquipmentSockets-20261011.md)。
- 史诗制作、调级箱、装扮合成与透明外观已有用户 happy path 验收；其他边界按各功能记录保留。固定成就扩展的完整实机检查仍待补。
- [特殊单人副本探索](SpecialSoloDungeonsExploration-20261011.md) 区分 70Reference、ClientStatic、CurrentImplementation 与未知，不能据此宣布原生客户端验证完成。

## 发布验证与资源

协议文档同步扫描：143 个 C2S 注册、207 个 S2C 编码身份、356 个目录身份，缺口 0。Release 构建零警告／零错误；GM Web 148 个测试及生产构建、Release guard 11 个测试、WireSmoke 5 个测试通过。最终 .NET 回归及 GitHub 双平台结果以 PR／Release Actions 和 TRX 为准。

本地完整内容回归必须使用已部署的服务端 PVF：SHA-256 49D4CAFA402DAA93E9C60EFAC8550C20789BB5BD9F99DD7B9235FD0F20666E7A。首次误用客户端 PVF，触发三条已知地图布局不兼容测试；保留失败日志后改用上述资源。统一任务目标解析重构使同一资源的目录从旧主干的 2814 条变为 3252 条，任务快照断言随之校准，字段与具体任务奖励断言保留。

Release 包不包含 Script.pvf、角色数据库或客户端二进制。已有 PVF 修改由 Tool/PvfMirrorLayoutPatch、Tool/PvfDimensionLayoutPatch、Tool/PvfAvatarSocketTextPatch 等工具及其记录交付；新安装必须自行配置匹配资源。当前用户已验收的部署不在本轮重启或替换。

Windows 首轮 CI 在 45 分钟总上限处取消：已完成的六份测试报告均为 0 failed，Application 耗时约 29 分钟，Persistence 尚未完成。Windows 总预算调整为 90 分钟；测试范围、顺序及 3 分钟无进展保护保持不变。失败／取消日志和 TRX 留存，必须重跑通过后合并。

## 集成方式

保留分支已有提交，采用 merge commit；检查通过后合并。v0.2.4 只指向 main 上的最终合并提交，由 Server Release 工作流构建 Windows x64／Linux x64 自包含包和 SHA-256 文件。未完成的父 issue 保持开放，发布不等同于所有范围验收。
