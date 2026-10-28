param([int]$Port=7001,[string]$ExpectedName='')
$ErrorActionPreference='Stop'
$taskClient=[Net.Sockets.TcpClient]::new()
try {
    [void]$taskClient.ConnectAsync('127.0.0.1',$Port).WaitAsync([TimeSpan]::FromSeconds(3)).GetAwaiter().GetResult()
    $taskStream=$taskClient.GetStream();$taskStream.ReadTimeout=3000;$taskStream.WriteTimeout=3000
    function Read-Channel([byte]$Number){
        $taskHeader=[byte[]]@(0x7C,$Number,11,0,0,0,0,0,0,0,1)
        $taskStream.Write($taskHeader,0,$taskHeader.Length)
        $taskReply=[byte[]]::new(11);$taskStream.ReadExactly($taskReply,0,11)
        $taskLength=[BitConverter]::ToUInt32($taskReply,2)
        if($taskLength -lt 11 -or $taskLength -gt 1048576){throw 'Invalid channel frame length'}
        $taskBody=[byte[]]::new($taskLength-11);$taskStream.ReadExactly($taskBody,0,$taskBody.Length)
        [pscustomobject]@{Number=$taskReply[1];Body=$taskBody}
    }
    $taskConnect=Read-Channel 0x0B
    if($taskConnect.Number -ne 0x0C -or $taskConnect.Body.Length -ne 36){throw 'Invalid channel handshake'}
    $taskKey=[byte[]]$taskConnect.Body[4..19]
    $taskInfo=Read-Channel 0x11
    if($taskInfo.Number -ne 0x12){throw 'Invalid channel info response'}
    $taskInput=[IO.MemoryStream]::new([byte[]]$taskInfo.Body)
    $taskZlib=[IO.Compression.ZLibStream]::new($taskInput,[IO.Compression.CompressionMode]::Decompress)
    $taskOutput=[IO.MemoryStream]::new();$taskZlib.CopyTo($taskOutput)
    $taskAes=[Security.Cryptography.Aes]::Create();$taskAes.Key=$taskKey
    $taskPlain=$taskAes.DecryptEcb($taskOutput.ToArray(),[Security.Cryptography.PaddingMode]::None)
    $taskCount=[BitConverter]::ToInt32($taskPlain,2)
    if($taskCount -lt 0 -or $taskCount -gt 1000 -or $taskPlain.Length -lt 6+48*$taskCount){throw 'Invalid channel row count'}
    $taskRows=@(for($taskIndex=0;$taskIndex -lt $taskCount;$taskIndex++){
        $taskAt=6+48*$taskIndex
        [pscustomobject]@{Name=[Text.Encoding]::ASCII.GetString($taskPlain,$taskAt,20).TrimEnd([char]0);Host=[Text.Encoding]::ASCII.GetString($taskPlain,$taskAt+28,16).TrimEnd([char]0);Port=[BitConverter]::ToInt32($taskPlain,$taskAt+44);Capacity=[BitConverter]::ToInt32($taskPlain,$taskAt+20)}
    })
    [ordered]@{Count=$taskCount;Channels=$taskRows} | ConvertTo-Json -Depth 4
    if($ExpectedName -and -not @($taskRows | Where-Object Name -eq $ExpectedName).Count){throw 'Expected native channel identity absent'}
}finally{
    if($null -ne $taskAes){$taskAes.Dispose()};if($null -ne $taskZlib){$taskZlib.Dispose()};if($null -ne $taskInput){$taskInput.Dispose()};if($null -ne $taskOutput){$taskOutput.Dispose()};$taskClient.Dispose()
}
