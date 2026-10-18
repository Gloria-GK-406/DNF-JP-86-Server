import os
import subprocess
from pathlib import Path

root = Path(__file__).resolve().parents[3]
env = {k: v for k, v in os.environ.items() if k.upper() != "PATH"}
env["PATH"] = os.environ.get("PATH", os.environ.get("Path", ""))
env["MSBUILDDISABLENODEREUSE"] = "1"
env["PVF_ARCHIVE_PATH"] = str(root / "artifacts/sao-ticket-20261014/Script.server.pvf")
targets = [
    ("Dfo.Server.Application.Tests", "FullyQualifiedName~MarriageHousing|FullyQualifiedName~HousingNetworkSettlement|FullyQualifiedName~CommandHandlerArchitecture|FullyQualifiedName~MarriageArchitecture|FullyQualifiedName~EffectOperationReplay"),
    ("Dfo.Server.Protocol.Tests", "FullyQualifiedName~MarriageHousingCodec"),
    ("Dfo.Server.Persistence.Tests", "FullyQualifiedName~MarriageRelationMigration"),
]
failed = False
for name, selector in targets:
    log = Path(__file__).with_name(f"review-{name}.log")
    command = ["dotnet", "test", str(root / f"Server/tests/{name}/{name}.csproj"), "--no-build", "--no-restore", "--filter", selector, "--logger", "console;verbosity=normal"]
    with log.open("wb") as stream:
        run = subprocess.run(command, cwd=root, env=env, stdout=stream, stderr=subprocess.STDOUT)
    print(f"{name}: exit={run.returncode}")
    print(log.read_text(encoding="utf-8", errors="replace")[-900:])
    failed |= run.returncode != 0
raise SystemExit(int(failed))
