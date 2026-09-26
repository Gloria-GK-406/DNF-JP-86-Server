# 三仓库拆分验收（2026-09-26）

## 交付

| 仓库 | 本地根目录 | 所有权 |
|---|---|---|
| [Server](https://github.com/Gloria-GK-406/DNF-JP-86-Server) | E:/Project/DNF/DNF-JP-86-Server | 服务端、认证HTTP API、协议知识库、服务端工具 |
| [ClientHack](https://github.com/Gloria-GK-406/DNF-JP-86-ClientHack) | E:/Project/DNF/DNF-JP-86-ClientHack | 补丁、客户端调试/VMP研究及相关证据 |
| [Launcher](https://github.com/Gloria-GK-406/DNF-JP-86-Launcher) | E:/Project/DNF/DNF-JP-86-Launcher | WPF登录器、测试及历史PvfProxy工具 |

三者均为private。当前不合并Launcher和patch，也不实现patch退役、Linux部署或CI/CD。

## 验收边界

服务端main不再维护ClientHack/、Launcher/或Tool/PvfProxy/，服务端解决方案不再引用WPF客户端。Dfo.Server.LauncherApi是服务端认证端点，仍保留。

协议解析、服务端业务、70端算法参考、实机验收证据留在服务端；客户端补丁实现、脱壳/VMP工程和调试运行资料迁往ClientHack。原任务物品调查中两个PVF探针源文件只读服务端Dfo.Pvf，因此留在服务端，不构成客户端构建依赖。

服务器已有历史提交、远端旧分支及其他正在使用的任务工作区不重写；它们是历史快照，不能据此在服务端继续开发客户端。旧调试工作区已移到Backups，新调试工作区位于ClientHack/.worktrees/client-debug-lab。

## 历史与数据保护

- 原main基线：5e7c5ce21ebc2ab8f3fd983ce0fc9b7efca13cd0；原debug基线：b6f1d7e062e84b1efea061818ef7e42725e7b0b4。
- 两个新仓库按路径提取历史，保留作者、时间、相关演进；新SHA变化，映射在各仓Document/commit-map.txt。
- ClientHack另保留client-debug-lab及legacy-patch-source；未把实验分支强行合并到生产main。
- 提取后逐Git blob比较main选定文件，debug分支也独立比对；随后仅调整仓库入口、路径、文档引用及调试构建preset。
- 路径归属清单：[repository-split-paths.json](repository-split-paths.json)，共迁出540个main跟踪文件。
- Git bundle备份：E:/Project/DNF/.task-evidence/repo-split-20260926/pre-split.bundle。
- 旧构建目录及原debug工作区：E:/Project/DNF/Backups/repo-split-20260926。构建缓存不复用，新目录重新构建。
- debug运行目录两份重建脚本仅更新源码路径，备份在上述目录的runtime-scripts；未运行重建、未替换exe/dll。
- 正在运行的服务器、数据库、游戏客户端没有停止或替换。历史未跟踪诊断产物不作为三个项目的构建输入。

## 验证

- Server Release全解决方案构建：0警告、0错误。
- 服务端LauncherApi测试50项、Application架构相关测试38项通过。
- 独立Launcher Release测试63项通过；win-x64发布及历史PvfProxy构建通过。
- ClientHack main全量Release构建、stage产出、CTest 11项通过。
- ClientHack debug全量Release构建、stage产出、CTest 8项通过，保留PROTECT_DRX=ON。
- debug旧preset指向不存在的VS2026，改为与此前实际缓存一致的VS2022；4个VMP脚本改为本文件相对导入路径。
- 项目引用全部在各自仓库内；服务器slnx所有Project均在Server目录内；服务器原客户端三个目录均已不存在。
- 文档跨所有权引用改为GitHub链接；历史绝对采样路径仅为来源，不宣称跨机器可直接重放。

本次只验证结构和构建不破坏现有能力，不宣称已通过Linux运行或全游戏实机回归。
