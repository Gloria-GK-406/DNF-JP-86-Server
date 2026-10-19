# 收集箱常驻说明与PVF同步

日期：2026-10-19，源码基线`98c3b09615f217efd15566ca4ab49fd951fb6ad0`。用户授权将三套说明改为常驻、去掉历史到期与延期券文案，并同步实际客户端／服务端PVF。

## 当前内容

仅替换`localization/collectbox/collectboxinfo.etc`中Index 1、2、3的三个`[Box Explain]`字符串：

- SAO收集箱常驻启用，属性永久有效。 每个稀有宝珠额外增加6点力量、 智力、 体力和精神。 每个神器宝珠额外增加12点力量、 智力、 体力和精神。
- 生肖之灵收集箱常驻启用，属性永久有效。 每个生肖宝珠额外增加8点物理/魔法攻击力， 额外增加12点独立攻击力。 (收集箱的效果不适用于决斗场)
- 三国志卡片收集箱常驻启用，属性永久有效。 每个三国志卡片额外增加8点物理/魔法攻击力， 额外增加12点独立攻击力。 (收集箱的效果不适用于决斗场)

物品、坐标、奖励、决斗场限制以及其他字段保持原值。历史`[base expiration date]`、`[max expiration date]`和`[Extend Item]`属于保留的资源数据，本服常驻由服务端永久状态负责；它们不再出现在上述说明中。

## 实际使用的文件

| 目标 | 路径 | 新SHA-256 |
|---|---|---|
| 服务端 | `E:\Project\DNF\DNF-JP-86-Server\artifacts\collection-box-text-20261019\Script.server.pvf` | `7E679699E814F60CE2B9315E661DF62A31F7D784DD49FE221FB9F2BE66EA425B` |
| 测试客户端1 | `E:\Project\DNF\DNFClient-A21-protocol\Script.pvf` | `1D80B312EACEA7B18C32465269BBE01FCA2BAA071CFC5F8F7F2302665278B5A5` |
| 测试客户端2 | `E:\Project\DNF\DNFClient-A21-protocol2\Script.pvf` | `55F5D2BF84C795896AB70F41BADBE55F71ADFDF8586D097DFE35340B5BAEFCC0` |

两客户端各自在原PVF基础上修改，没有用服务端PVF覆盖客户端。每份归档共712010个文件条目，712009个非目标条目不变；既有压缩数据块、哈希表、原有字符串偏移和尾部数据保留，目标脚本只改三个字符串引用。三个新字符串在两客户端和服务端读回一致。

## 运行与检查

- 服务端仍为`0.2.7+ca0e62fe6507851a9a8c13b90802c7e7a68baf19`，12个程序程序集哈希不变，实际加载PVF改为上表新路径，运行配置只改变`Pvf.ArchivePath`。
- 当前PID `42168`，最终核验时间`2026-10-19T12:28:44.8790443+08:00`。Launcher bootstrap和GM health均ready，GM页面HTTP200，31个TCP／2个UDP监听集合不变，stderr为空。
- 构建补丁工具0警告／0错误；新服务端PVF下内容6项、收集箱业务21项回归通过，均exit0。
- 安装后使用独立PvfProbe重新读取三份实际目标，三个说明逐字一致，历史年份和延期券文案不再被这些字段引用。
- 切换前备份和启动后快照均通过SQLite完整性／外键检查；原106张表逐表内容指纹完全一致，无新增表。

补丁工具：[PvfCollectionBoxTextPatch](../Tool/PvfCollectionBoxTextPatch/Program.cs)。命令为`dotnet run --project Tool/PvfCollectionBoxTextPatch -- <source.pvf> <new.pvf>`，拒绝未知／已修改的说明、原地修改和覆盖输出，附带完整回读校验及JSON报告。

## 备份与继续测试

`artifacts/collection-box-text-20261019/backups/`保留三份原PVF及原运行配置；原服务端PVF`E:/Project/DNF/DNF-JP-86-Server/artifacts/wedding-ticket-pvf-fix-20261018/Script.server.pvf`仍在，SHA-256 `BCF99E4E4CC691D43DD89373519EF67D632B83FB0BDAD560454E101415768D63`。`artifacts/collection-box-text-20261019/stopped.db`保留切换前玩家库，日常文案回退只需配对恢复PVF和配置，不应为了文案恢复而覆盖玩家新进度。

`sync-receipt.json`、`final-verification.json`、三份PVF的`.json`、独立`.readback.txt`和回归日志保存在同一产物目录。重新启动测试客户端后检查三套说明，再继续[原有验收步骤](CollectionBox-Implementation-20261019.md)。尚未代替用户确认原生界面显示或玩法效果。
