# TASK-04 当前 A21 最终实机准备

## 冻结基线

- Git accepted baseline / 当前 HEAD：`d1351ce6047ea84db1c764efc3ad9466781f07af`；spec-002 变更仍为
  worktree 内未提交增量，未混入其他分支提交。
- `DNFClient-A21-protocol/DNF.exe`：
  `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。
- `DNFClient-A21-protocol2/DNF.exe`：同上。
- `DNFClient-A21-protocol/DNF.unpacked-b1-debug.exe`：
  `CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14`。
- `DNFClient-A21-protocol/dstr.dat`：
  `60E9857773831B341BAE0D490FCAEAF0AF184B9776717E2FCF293E1BC7F92F10`。
- `DNFClient-A21-protocol/Script.pvf`：
  `19F5FA85FF73CCE94E299E0902269DE2F0164934128F15B8A7B99249C881BE1A`。
- 两份当前客户端 `ClientHack.ini`：
  `40B97549F44ABEAE5B960C29C148FA0FB342A3B3A5A9097847B12796F7145579`。

## 自动准备检查

- spec-001 设置探针：`byte_anchors=14/14 PASS`。
- Normal 专项探针：`byte_anchors=5/5 PASS`。
- 定向捕获 self-test：既有 split/coalesced + create 顺序 `PASS`；空 body `0x000D` allowlist `PASS`。
- 首次探针调用漏传必填 EXE/dstr 参数，只产生 argparse usage 且无状态变更；按命令行契约重跑后得到上述
  PASS。
- 启动前未发现 `DNF`、`Dfo.Server.Host` 或本任务 Python 中转；`7001/10010/10011`、
  `17001/18080/20010/20011` 均无监听。
- `dotnet build Server/86JP.Server.slnx -c Release --no-restore`：0 warning / 0 error。

## 本轮隔离拓扑

- 客户端标准端口：Channel `7001`，Game `10010/10011`。
- Host 后端端口：Channel `17001`，Game `20010/20011`；Launcher API `18080`。
- Channel 对客户端仍广告 `10010/10011`；定向中转只落 Game `0x000C/0x000D/0x0099/0x000B/0x0009`。
- 最终帧写入同目录 `live-final-targeted-frames.jsonl`；Host 原始输出不入库，结束后仅提取 Party allowlist
  摘录。
