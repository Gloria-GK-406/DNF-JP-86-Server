# GM 后台物品图标包

GM 后台的物品图标来自客户端 `ImagePacks2` 中的 NPK 图片，按物品 ID 打包成一个 zip 部署在服务端，后台直接读取 zip，不解压。图标是 Nexon 美术资源，**只放在部署目录，绝不提交仓库或随发布包分发**（项目开源）。`.gitignore` 已忽略 `gm-icons.zip` 与 `*.gm-icons.zip`。

## 包格式

| 条目 | 内容 |
|---|---|
| `icons/{itemId}.png` | 28×28 RGBA PNG（极少数客户端帧不是 28×28，保持原尺寸） |
| `manifest.json` | `{ "generatedAt": ISO8601 UTC, "pvfSha256": 小写 hex, "count": 图标数 }` |

`pvfSha256` 是生成时所用 PVF 文件的 SHA-256，可与 `GET /api/gm/v1/server-info` 的 `pvfSha256` 对比，判断图标包是否与服务端当前 PVF 一致。PVF 改动后整包重建。

## 重建工具 `Tool/GmIconPack`

```powershell
dotnet build Tool/GmIconPack/GmIconPack.csproj -c Release
dotnet Tool/GmIconPack/bin/Release/net10.0/GmIconPack.dll --pvf <Script.pvf> --imagepacks <客户端 ImagePacks2 目录> --out <部署目录>\Data\gm-icons.zip
```

- 枚举 `equipment/equipment.lst` 与 `stackable/stackable.lst`，读取每个物品根部的 `[icon]`（`<img 路径> <帧号>`），在所有 NPK 的目录索引中定位 `sprite/` + 小写路径，解码对应帧。
- 默认范围：非时装、非宠物的装备 + 全部堆叠物；`--all` 额外包含时装与宠物装备。分类规则与 `PvfEntryLifecycleContentSource` 一致（`[creature]` 为宠物，以 `avatar]` 结尾为时装）。
- IMG 支持 v2（0x0E ARGB1555、0x0F ARGB4444、0x10 ARGB8888、0x11 帧链接，0x06 zlib 压缩）与 v4 调色板（sprite_item 中未出现 v4，调色板字节序按 BGRA 推定、未经实证）；链接帧按 8 字节索引项解析（对全部 sprite_item 校验过索引长度）。
- 无法解析的物品跳过并在结束时按原因汇总（缺少 `[icon]`、IMG 不存在、帧号越界、格式不支持），每类给出 3 个示例。
- 先写同目录临时文件再改名。服务端运行时持有旧包（只共享删除），工具会先删除旧包再改名，服务端下一次请求即读到新包。
- 只读 PVF 与 ImagePacks2，不修改客户端目录。

2026-10-16 冒烟（`Script.server.pvf` + A21 upstream 客户端 ImagePacks2）：候选 93,716，成功 93,182，缺失 534（无 `[icon]` 229、IMG 不存在 23、帧号越界 282，后两类疑为客户端图片版本与 PVF 不一致）；包约 157 MiB；27650、3037、2681114 均为 28×28，与 Python 原型逐像素一致。

## 部署与配置

`GmWeb:IconPack` 指定 zip 路径，默认 `Data/gm-icons.zip`，相对路径按可执行文件目录解析（与 `Database:Path` 相同）。文件不存在时后台照常运行，前端以名称首字方块代替图标。

## 接口

均需登录，任意管理员权限即可（`GmPermission.None`）。

- `GET /api/gm/v1/items/icon-pack` → `{ available, count, generatedAt, pvfSha256 }`。包不存在或无法打开时 `available=false`、其余为 null；包可用但 manifest 缺失/损坏时 `available=true`、其余为 null。
- `GET /api/gm/v1/items/{itemId}/icon` → `image/png`，`Cache-Control: private, max-age=86400`。包或条目不存在、条目超过 64 KiB 时 404 `{ message }`（`no-store`）。

服务端懒打开 zip 并缓存中央目录，文件大小或修改时间变化时重新打开；ZipArchive 非线程安全，条目读取串行化。浏览器按物品 ID 缓存一天，重建后如需立即刷新可清浏览器缓存。
