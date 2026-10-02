---
kind: rks-review
verdict: changes_required
---

# Spec006 独立完整结果审查

目标：HEAD `aa5b663672c1beaac82484e57c3bbbeff5429d44` 上未提交的完整7产品冻结清单。Spec SHA256 `A83EC50E978A9A89D112A490CFD86474046A6404EE896354EC17146B8A78C3CB`；manifest SHA256 `8B3EF4C5D8E4345B158C1F88ACE30EE45B0D82DEDE2CEEC685C7377861FFACB2`；7份产品实际SHA均与清单一致。

审查者为fresh独立上下文，生产者为root。读取根/child TaskContract、Spec、完整产品源码/文档与相对基线全diff，采用 reviewing-work 的 Code、Deliverable 判据，核对相关Host配置、发布资源复制和数据库/PVF启动调用。Lite不要求挑战集，本次未提供。未修改产品、运行服务、前后端构建或Git写操作。

## 阻塞发现

**F-001：冒烟继承环境的等价配置键可绕过隔离覆盖（RQ-004 / AC-002）。** `Tool/Release/release.py:172-177` 只归一过滤 `gmweb:bootstrap:*`，随后以双下划线键写入隔离配置。继承的 `Database:Path`、`Networking:Enabled`、`LauncherAuth:Enabled`、`GmWeb:ListenUrl` 等冒号别名仍进入子进程；Linux上不同大小写的同键也能共存。Host 的 `ServerConfiguration.Load` 使用 `AddEnvironmentVariables()`，冒号及双下划线归一后指向同一配置，当前实现没有保证固定值胜过继承别名。若数据库别名胜出，`PersistenceModule` 读取该路径，`SqliteDatabase` 构造执行 `CharacterDatabaseSchema.Ensure`，会对外部数据库打开并执行schema维护，而不是fresh临时库。Game/Launcher别名胜出也可能开启原监听。HTTP/initialized状态检查在进程启动之后，不能阻止前述副作用。

另一个直接证据是继承的 `Pvf__ArchivePath` 完全未清除：`InfrastructureModule` 将其绑定为 `PvfContentOptions.ArchivePath`，`ResolveArchivePath` 优先采用它，其优先级高于此处清空的 `PVF_ARCHIVE_PATH`。`GameServerStartupService.StartingAsync` 无论网络是否关闭，都对该档案执行预热。这使候选冒烟继续依赖部署环境PVF，而非说明中声称的隔离环境。

最小充分修正：对需固定的隔离配置键先按 `__`→`:` 且大小写不敏感归一，删除继承的全部等价键，再写入明确的临时库、独立GM地址、组件开关及PVF隔离值；继续清除全部bootstrap输入。亦可用Host最后优先的命令行配置固定关键值，并清理PVF/bootstrap直接环境入口。以这些别名作为实际隔离验证输入，证明只访问fresh库/临时端口、不开Game/Launcher、不依赖外部PVF即可；不需要扩展业务或增加治理流程。

## 其余决定性证据

- CI diff仅增加固定Node22.19.0及官方setup-node v7 SHA后的 `npm ci → test → build`，随后才进入.NET restore/build/test；Windows/Linux矩阵、原Action SHA、tag/main守卫和发布权限未改变。
- package每次调用带 `check=True` 的npm ci/build；发布后校验HTML本地JS/CSS引用、目录边界、完整dist文件集合与逐文件字节。默认三个开关false、bootstrap秘密、.env、环境配置及原DB/PVF/log排除具有明确拒绝路径。原5个真实Git历史守卫测试未改。
- 原始RED日志确实记录旧工具生成缺JS的zip且拒绝断言失败；当前Python日志10项通过，前端日志54项通过，TS/Vite构建成功。这些是生产者执行证据，未替代实际源码检查。
- 对当前zip独立只读复核：CRC通过，SHA256 `ca2212e902ecc9d8f0c14fb07b525883c07bcccbbc1625119fb831cda23236a3`；3份GM资源与zip、release.json及当前dist字节一致；8份文档所有相对链接目标存在。原始package-final.log包含完整自包含publish及HTTP资产/未初始化/401成功结果。smoke使用发布副本并在finally只结束自己启动的进程，archive在smoke成功后才形成。
- 文档与GM01..21涵盖根合同查询、真实会话、权限、邮件草稿/模板/附件/明确批量、三币按账号发放、重试/收据与旧流程；Chrome、CurrentImplementation、A21待验和远程Linux/CI未执行均明确分开。真实现服未切换边界清楚，包内文档入口可接手。

待fresh完整.NET/PVF回归属于随后completion verification，本次不以该时序缺口阻塞review。当前zip的release.commit标识基线而非干净接受提交，报告已如实限定为未提交候选；根收尾应另产干净提交候选。F-001解决后只需重新审查修正及其直接回归，其他未变产品证据可按本报告引用。
