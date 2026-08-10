# 86JP Architecture

这是 86JP 服务端架构重构工作区。

当前分支已经移除旧 `Server` 单体项目、旧客户端 `Patch` 项目，以及它们专属的构建、发布、启动和抓包脚本，并在此基础上建立新的服务端骨架。

旧实现仍完整保留在相邻的 `E:\Project\86JP` 工作区中，迁移期间可用于行为对照。

## 保留内容

- `Tool/`：独立工具与 PVF 相关库
- `Docs/`：既有资料，暂作历史参考

## 新服务端骨架

- `Server/src/Dfo.Server.Host`：Generic Host 组合根
- `Server/src/Dfo.Server.Abstractions`：跨项目共享契约
- `Server/src/Dfo.Server.Networking`：Socket、游戏协议与 TouchSocket 实现边界
- `Server/src/Dfo.Server.Utilities`：无业务语义的共享小工具
- `Server/tests`：按生产项目对应的单元测试占位目录，以及独立的集成测试入口目录

客户端补丁若仍有需要，将作为独立适配项目重新引入。
