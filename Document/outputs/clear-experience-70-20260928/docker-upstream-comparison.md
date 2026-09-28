# Docker 数据与最新参照服经验算法对照

日期：2026-09-28。

## 结果与对前一轮结论的修正

70 ELF 确实采用击杀数驱动的通关算法；但这不足以证明 A21 必须采用相同算法。最新 ServerS4A21 有意采用不同的、注释为“90-version”的公式。前一轮 `findings.md` 将修复方向直接定为恢复 70 算法过于确定：目前应把它视为候选设计，不能把版本差异当作已证实的 A21 bug。

## Docker 取数

来源：https://github.com/1995chen/dnf 。

- CentOS 5 镜像启动退出码 139；单独启动 bash 同样不能正常执行。未修改宿主内核。
- 改用 `1995chen/dnf:centos7-2.1.9.fix1`，digest `sha256:0c2f1b8c66cce9375c07fa4501f2498659df874727ea820fc2d1ec4e48006a7a`。
- 容器 `dnf70-reference-c7`，独立 volumes `dnf70-reference-db`、`dnf70-reference-data`，network=none，无宿主端口映射。
- MySQL 初始化成功，查询 `taiwan_cain.monster_reward_ref`：100 行，等级 1..100，exp 合计 133578。
- 已导出 `monster-reward-ref.tsv`，SHA256 `9A050A1D26CFCBD2D15E3243AF05C988474579736B168D3B038C1D0A041BC1A4`。
- 这是第三方镜像种子数据库，证明该发行包的数据，不证明原厂未修改数据库。
- 镜像自带 df_game_r SHA256 为 `DD0AE672B1DCE3FF9C6C403BEAEC71B3EF666B14DEB567D4CAB0DA99D8711F9E`，与前轮单独分析的70等级补丁 ELF 不同，不能混称同一二进制。
- **未完成游戏登录验收**：隔离网络下频道脚本在等待 PUBLIC_IP 监控文件；为了本次只读数据库调查，已停止三个频道启动脚本。不能把 supervisor 的 RUNNING 当作游戏核心已初始化成功。
- 原有 JP86 Windows 服务和 Linux 测试容器未改动。

| 等级 | Docker 数据库 | 上游常量表 | 本服曲线 |
|---|---:|---:|---:|
| 1 | 30 | 30 | 30 |
| 15 | 170 | 170 | 170 |
| 64 | 1630 | 1630 | 1630 |
| 70 | 1908 | 1908 | 1908 |
| 80 | 2419 | 2419 | 2419 |
| 85 | 2697 | 2697 | 2697 |
| 99 | 3555 | 3555 | 3555 |
| 100 | 3575 | 3575 | 3621 |

数据库与上游 100 项逐项一致；本服曲线 1..99 一致，100级有既知差异。当前86级上限不受100级差异影响。

## 上游更新

`E:/Project/DNF/ServerS4A21` master 使用 `git pull --ff-only`：

- 原 HEAD：`4c6c442c607fdf1803a3f0a4e7502bb53165a7d5`
- 新 HEAD：`74a077a`，42 个新增提交，93 文件变动。
- 原有未跟踪 `_reference-backup-20260826/` 保留，没有修改业务源码。
- 本次更新没有更改 DungeonExperienceCalculator；相关差异此前已经存在。

### 最新上游击杀经验

`Server/DfoServer/Game/Dungeon/DungeonExperienceCalculator.cs`：

```text
shared = floor(MonsterRewardTable[monsterLevel]
             * (partyRate + partyEventRate)
             * difficultyRate * dungeonWeight * storyWeight
             * monsterKindRate * namedRate)
personal = floor(shared * levelPenalty(characterLevel, dungeonStandardLevel)
                        * memberPenalty / partyMemberCount)
```

与本服当前规则的重要差异：上游普通击杀不再除以2；等级差优先使用地下城标准等级；低等级打高等级地图时系数直接1，不完整照搬70等级差曲线。绿名额外3倍且避免与类别倍率重复叠加。

### 最新上游通关经验

```text
base = floor(questRewardTable[dungeonStandardLevel]
           * difficultyRate * dungeonWeight * storyWeight)
```

不乘击杀数，不除队伍人数，不对基础通关经验应用等级差。虽然 context 仍保留这些字段，但当前标准通关函数没有使用。各类通关加成另算。

关键历史提交 `625e2cc95ab3bde523e753bd2d92ce730616505e`（2026-08-23，“调整经验计算逻辑”）同时移除了击杀 `/2`，并将通关从击杀数模式改为上述任务表模式；代码注释称“90-version”。本轮未找到该命名对应的独立原厂证据，不能把上游实现或自测当成 A21 权威。

### 本次更新里的经验道具修复

`e32c91b` 修复异次元裂缝成长胶囊 `expupbycrackofdimension` 的无参数脚本：按角色等级索引任务经验数组并乘56%，向下取整；其 helper 用 level+1 抵消内部1基索引。提交说明有多等级实测，属于上游自述实测，尚未在本服复核。

## 下一步建议

基础怪物经验曲线在当前等级范围已获得实际发行包数据交叉验证，可以保留。先明确选择 A21 的规则目标：保留现有后期通关模型，或采用70击杀数模型。未经进一步 A21 证据或用户指定，不直接用70规则覆盖现有算法。统一 ExperienceBreakdown 可以承载两种模型，不需要推翻已完成的架构重构。
