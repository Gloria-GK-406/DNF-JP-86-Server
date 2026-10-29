"""Compare diagnostics with HEAD's tracked sources/catalog, without changing repository files."""
import contextlib
import importlib.util
import io
import subprocess
import sys
from pathlib import Path

sys.dont_write_bytecode = True
root = Path(__file__).resolve().parents[3]
catalog = root / "protocol/catalog.md"
tracked = set(subprocess.check_output(["git", "ls-tree", "-r", "--name-only", "HEAD"], cwd=root, text=True).splitlines())
changed = subprocess.check_output(["git", "diff", "--name-only", "HEAD"], cwd=root, text=True).splitlines()
head_text = {name: subprocess.check_output(["git", "show", "HEAD:" + name], cwd=root).decode("utf-8")
    for name in changed if name in tracked and (name.endswith(".cs") or name == "protocol/catalog.md")}
spec = importlib.util.spec_from_file_location("forgotten_land_doc_check", root / "protocol/patterns/data/check-server-documentation-sync.py")
checker = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = checker
spec.loader.exec_module(checker)
original = Path.read_text


def read_head_catalog(path, *args, **kwargs):
    try:
        relative = path.resolve().relative_to(root).as_posix()
    except ValueError:
        return original(path, *args, **kwargs)
    return head_text[relative] if relative in head_text else original(path, *args, **kwargs)


original_sources = checker.load_sources
def head_sources(source_root):
    return {path: source for path, source in original_sources(source_root).items()
        if path.resolve().relative_to(root).as_posix() in tracked}


output = io.StringIO()
sys.argv = [str(spec.origin), "--repo-root", str(root)]
try:
    Path.read_text = read_head_catalog
    checker.load_sources = head_sources
    with contextlib.redirect_stdout(output):
        result = checker.main()
finally:
    Path.read_text = original
text = output.getvalue()
out = Path(__file__).parent / "evidence"
(out / "documentation-sync-baseline.log").write_text(text, encoding="utf-8")
current = (out / "documentation-sync.log").read_text(encoding="utf-8-sig")
same = current.partition("\nfailures:")[2].splitlines() == text.partition("\nfailures:")[2].splitlines()
summary = f"HEAD baseline exit: {result}\nCurrent failure diagnostics identical to HEAD sources and catalog: {same}\n"
(out / "documentation-comparison.txt").write_text(summary, encoding="utf-8")
print(summary)
if not same:
    raise SystemExit(1)
