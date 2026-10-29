# 数据库结构版本与迁移

服务端只支持当前这一种表结构。旧库的升级不在服务端里做，而由独立工具 `Tool/DbMigrate` 在停服时执行。

## 服务端：只认当前版本

- 表结构的唯一声明在 `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs`，版本号是其中的 `Version`，记在数据库文件头的 `PRAGMA user_version`。
- 打开数据库时（`SqliteDatabase` 构造）：
  - 没有任何表的空库：按声明建出全部表、触发器和索引，写入当前版本号。
  - 版本号等于 `Version`：直接使用，不做任何结构改动。
  - 其余情况（包括没有版本号的旧库）：拒绝启动，提示停服、备份后运行迁移工具。
- 服务端不再补列、不再执行数据迁移，也不保留迁移标记表。

## 迁移工具：Tool/DbMigrate

```bash
dotnet run --project Tool/DbMigrate -- <数据库路径>
```

- 在数据库所在目录先用 `VACUUM INTO` 生成一致备份 `<数据库>.v<原版本>-<UTC 时间>.bak`，再逐步执行迁移，最后写入新版本号。`--no-backup` 可跳过备份，仅用于测试。
- 已是当前版本时直接退出；版本比工具认识的更新、或库里一张表都没有时报错退出。
- 每一步把版本 N 带到 N+1，登记在 `Migrator.Steps`。一步可能分多个事务执行，失败时版本号不变，用备份恢复后排查。
- 版本 0 → 1（`Legacy/LegacyUpgrade.cs`）：针对版本号出现之前的旧库，按旧服务端启动时的顺序执行建缺失表、补缺失列、外观、邮件金币、婚姻关系、婚姻房屋、共享材料、马战 ID 等全部旧迁移，然后删除退役列（`RetiredColumnMigration`）和两张迁移标记表。各步骤先查实际表结构再动手，不假设旧库来自哪个版本。
- 迁移工具不认识的表和列原样保留。

## 修改表结构时

任何改变 `CharacterDatabaseSchema` 声明的改动（新增表、列、索引，修改约束，删除列），在同一个改动里完成：

1. `Version` 加一。
2. 在 `Tool/DbMigrate` 增加从上一版本到新版本的步骤，并登记到 `Migrator.Steps`。
3. 在 `Tool/DbMigrate.Tests` 补用例。`SchemaVersionTests.A_migrated_database_has_exactly_the_shape_the_server_builds` 要求迁移后的结构与服务端新建库完全一致，漏了步骤会失败。

不同分支各自加版本会冲突，合并时需要按合并顺序重新排定版本号和步骤。

## 部署升级

1. 停服。
2. 运行迁移工具（自动备份）。
3. 启动新版本服务端。

列一旦删除，旧版本程序不能直接接着用这个库；回滚需恢复迁移前的备份和对应版本的程序。

## 测试

- 迁移相关用例在 `Tool/DbMigrate.Tests`，已加入 `Server/86JP.Server.slnx`，随解决方案构建和测试。
- 服务端测试中手写精简表结构的用例，通过 `Support/HandWrittenShape.Adopt` 调用迁移工具把库带到当前版本后再打开。
