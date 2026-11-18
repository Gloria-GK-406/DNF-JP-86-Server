import json
from pathlib import Path
import subprocess
import sys


ABSTRACTIONS = "Dfo.Server.Abstractions"
DATA = "Dfo.Server.Data"
CONTRACTS = (ABSTRACTIONS, DATA)

# Direct dependency policy for the production projects in Server/src.
ALLOWED_PROJECT_REFERENCES = {
    "Dfo.Pvf": (),
    ABSTRACTIONS: (DATA,),
    "Dfo.Server.Application": CONTRACTS,
    DATA: (),
    "Dfo.Server.GameServer": (
        ABSTRACTIONS,
        "Dfo.Server.Infrastructure",
        "Dfo.Server.Networking",
        "Dfo.Server.Protocol",
    ),
    "Dfo.Server.GmWeb": (*CONTRACTS, "Dfo.Server.Application", "Dfo.Server.HttpApi"),
    "Dfo.Server.Host": (
        ABSTRACTIONS,
        "Dfo.Server.Application",
        "Dfo.Server.GameServer",
        "Dfo.Server.GmWeb",
        "Dfo.Server.HttpApi",
        "Dfo.Server.Infrastructure",
        "Dfo.Server.Persistence",
    ),
    "Dfo.Server.HttpApi": (*CONTRACTS, "Dfo.Server.Application"),
    "Dfo.Server.Infrastructure": (*CONTRACTS, "Dfo.Pvf"),
    "Dfo.Server.Networking": CONTRACTS,
    "Dfo.Server.Persistence": CONTRACTS,
    "Dfo.Server.Protocol": (*CONTRACTS, "Dfo.Server.Application", "Dfo.Server.Networking"),
    "Dfo.Server.Utilities": (),
}


def project_file(production_directory: Path, name: str) -> Path:
    return production_directory / name / f"{name}.csproj"


def direct_references(project: Path, repository: Path) -> set[Path]:
    result = subprocess.run(
        [
            "dotnet", "msbuild", str(project), "-nologo",
            "-p:Configuration=Release", "-p:ContinuousIntegrationBuild=true",
            "-getItem:ProjectReference",
        ],
        cwd=repository,
        capture_output=True,
        encoding="utf-8",
    )
    if result.returncode:
        details = "\n".join(output.strip() for output in (result.stdout, result.stderr) if output.strip())
        raise RuntimeError(f"MSBuild exited with code {result.returncode}.\n{details}")

    items = json.loads(result.stdout)["Items"]["ProjectReference"]
    return {Path(item["FullPath"]).resolve() for item in items}


def display_path(path: Path, repository: Path) -> str:
    return path.relative_to(repository).as_posix() if path.is_relative_to(repository) else str(path)


def main(production_directory: Path) -> int:
    production_directory = production_directory.resolve()
    repository = production_directory.parent.parent
    project_paths = set(production_directory.rglob("*.csproj"))
    if not project_paths:
        print(f"No production projects found under {production_directory}.", file=sys.stderr)
        return 1

    policies = {
        project_file(production_directory, name): targets
        for name, targets in ALLOWED_PROJECT_REFERENCES.items()
    }
    failures = []
    reference_count = 0
    for project in sorted(project_paths):
        source = display_path(project, repository)
        if project not in policies:
            failures.append(f"{source}: no dependency policy for this production project.")
            continue

        try:
            references = direct_references(project, repository)
        except (OSError, RuntimeError, ValueError, KeyError) as error:
            failures.append(f"{source}: unable to evaluate ProjectReference. {error}")
            continue

        allowed_names = policies[project]
        allowed_paths = {project_file(production_directory, name) for name in allowed_names}
        reference_count += len(references)
        for target in sorted(references - allowed_paths):
            allowed = ", ".join(sorted(allowed_names)) or "none"
            failures.append(
                f"{source} -> {display_path(target, repository)}: forbidden direct reference. "
                f"Allowed direct references: {allowed}."
            )
        for target in sorted((references & allowed_paths) - project_paths):
            failures.append(f"{source} -> {display_path(target, repository)}: project file is missing.")

    if failures:
        print("\n".join(failures), file=sys.stderr)
        return 1

    print(f"Checked {len(project_paths)} production projects and {reference_count} direct project references.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(Path(__file__).resolve().parents[2] / "Server" / "src"))
