# CMD013D 极限祭坛难度响应

Game Server→Client，selector1，type013D。ClientStatic / CurrentImplementation，High；尚无本期实帧/实机接受。

成功Body=2B：success u8=1、difficulty u8。A21成功信封后RVA D060F0只读u8。请求为difficulty u8，RVA21724F0。

当前失败00 01；精确拒绝文案未闭合，不称ClientVerified。手动成功用ACK解选，超时才发NOTI0129单字节。

[来源及全流程](../../../Document/tasks/altar-dungeons/research-and-implementation.md)。
