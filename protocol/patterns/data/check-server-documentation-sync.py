#!/usr/bin/env python3
"""Cross-check the server's reachable Game wire identities against protocol docs.

This is deliberately a *coverage* check, not a correctness oracle.  A decoder or
encoder registration proves what this repository can currently consume or emit; it
does not prove that its type, layout, response or gameplay effect is correct for A21.

The ordinary mode reads the current C# source, the catalog and the protocol pages.  It
fails when a registered C->S decoder or an encoder-backed S->C identity is absent from
the catalog, when a page or catalog-local Markdown link is missing, or when an outbound
registration cannot be mapped to a wire identity.  ``--self-test`` runs the same
comparisons against a deliberately broken in-memory fixture so a permanently-green
checker is not accepted.
"""

from __future__ import annotations

import argparse
import pathlib
import re
import sys
import tempfile
from dataclasses import dataclass


@dataclass(frozen=True, order=True)
class Identity:
    direction: str
    family: str
    type_value: int

    @property
    def type_text(self) -> str:
        return f"0x{self.type_value:04X}"

    @property
    def command_text(self) -> str:
        return "0x01" if self.family == "CMD" else "0x00"

    @property
    def doc(self) -> str:
        if self.direction == "C2S":
            return f"game/client-to-server/{self.type_text}.md"
        if self.family == "CMD":
            # A CMD response belongs to the request page.  The old LOGIN response page
            # is a preserved historical exception, not the general naming rule.
            return f"game/client-to-server/{self.type_text}.md"
        return f"game/server-to-client/NOTI-{self.type_text}.md"


@dataclass(frozen=True)
class Registration:
    identity: Identity
    symbol: str
    source: str


CONST_RE = re.compile(
    r"(?:public|internal|private)\s+const\s+(?:byte|ushort|uint|int)\s+"
    r"(?P<name>[A-Za-z_]\w*)\s*=\s*(?P<expr>[^;]+);"
)


def load_sources(source_root: pathlib.Path) -> dict[pathlib.Path, str]:
    return {
        path: path.read_text(encoding="utf-8")
        for path in source_root.rglob("*.cs")
    }


def resolve_constants(sources: dict[pathlib.Path, str]) -> dict[str, int]:
    pending: dict[str, str] = {}
    values: dict[str, int] = {}
    for text in sources.values():
        for match in CONST_RE.finditer(text):
            pending[match.group("name")] = match.group("expr").strip()

    for _ in range(len(pending) + 1):
        changed = False
        for name, expr in list(pending.items()):
            value = resolve_expr(expr, values)
            if value is None:
                continue
            values[name] = value
            del pending[name]
            changed = True
        if not changed:
            break
    return values


def resolve_expr(expr: str, constants: dict[str, int]) -> int | None:
    expr = expr.strip().rstrip("uUlL")
    expr = re.sub(r"^(?:checked\s*\()?\((?:byte|ushort|uint|int)\)\s*", "", expr)
    expr = expr.rstrip(") ")
    if re.fullmatch(r"0[xX][0-9A-Fa-f]+", expr):
        return int(expr, 16)
    if re.fullmatch(r"\d+", expr):
        return int(expr, 10)
    name = expr.rsplit(".", 1)[-1]
    return constants.get(name)


def relative(path: pathlib.Path, root: pathlib.Path) -> str:
    return path.relative_to(root).as_posix()


def inbound_registrations(
    sources: dict[pathlib.Path, str], source_root: pathlib.Path, constants: dict[str, int]
) -> tuple[list[Registration], list[str]]:
    pattern = re.compile(
        r"AddGameCmdMessageDecoder<\s*(?P<decoder>[A-Za-z_]\w*)\s*>\s*"
        r"\(\s*(?P<type>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+|0[xX][0-9A-Fa-f]+)",
        re.S,
    )
    rows: list[Registration] = []
    failures: list[str] = []
    for path, text in sources.items():
        for match in pattern.finditer(text):
            value = resolve_expr(match.group("type"), constants)
            if value is None:
                failures.append(
                    f"cannot resolve inbound type {match.group('type')} for {match.group('decoder')}"
                )
                continue
            rows.append(
                Registration(
                    Identity("C2S", "CMD", value),
                    match.group("decoder"),
                    relative(path, source_root),
                )
            )
    return rows, failures


def class_body(text: str, symbol: str) -> str | None:
    match = re.search(rf"\bclass\s+{re.escape(symbol)}\b[^{{]*{{", text)
    if not match:
        return None
    start = match.end() - 1
    depth = 0
    for index in range(start, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[start + 1 : index]
    return None


def record_body(text: str, symbol: str) -> str | None:
    match = re.search(rf"\brecord\s+{re.escape(symbol)}\b.*?{{", text, re.S)
    if not match:
        return None
    start = match.end() - 1
    depth = 0
    for index in range(start, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[start + 1 : index]
    return None


def registration_symbols(
    sources: dict[pathlib.Path, str], source_root: pathlib.Path
) -> tuple[list[tuple[str, str, str, str]], list[str]]:
    """Return (family, message symbol, encoder symbol, source) registrations."""
    direct_re = re.compile(
        r"AddGame(?P<family>Cmd|Noti)MessageEncoder<\s*"
        r"(?P<message>[A-Za-z_]\w*)\s*,\s*(?P<encoder>[A-Za-z_]\w*)\s*>\s*\(\s*\)",
        re.S,
    )
    generic_re = re.compile(
        r"\bAdd(?P<family>Cmd|Noti)<(?P<message>[A-Za-z_]\w*)>\(services\);"
    )
    rows: list[tuple[str, str, str, str]] = []
    failures: list[str] = []
    for path, text in sources.items():
        src = relative(path, source_root)
        for match in direct_re.finditer(text):
            # The generic helper implementation registers TMessage itself and is not a
            # concrete reachable message registration.
            if match.group("message") == "TMessage":
                continue
            rows.append(
                (
                    match.group("family").upper(),
                    match.group("message"),
                    match.group("encoder"),
                    src,
                )
            )
        if "StateSynchronization" in src:
            for match in generic_re.finditer(text):
                rows.append(
                    (
                        match.group("family").upper(),
                        match.group("message"),
                        "<generic>",
                        src,
                    )
                )
    duplicates = sorted({row for row in rows if rows.count(row) > 1})
    if duplicates:
        failures.extend(f"duplicate outbound registration {row}" for row in duplicates)
    return rows, failures


def load_overrides(path: pathlib.Path) -> dict[tuple[str, str], list[int]]:
    out: dict[tuple[str, str], list[int]] = {}
    if not path.exists():
        return out
    lines = path.read_text(encoding="utf-8").splitlines()
    if not lines:
        return out
    header = lines[0].split("\t")
    for line in lines[1:]:
        if not line or line.startswith("#"):
            continue
        row = dict(zip(header, line.split("\t")))
        key = (row["family"].strip().upper(), row["symbol"].strip())
        out.setdefault(key, []).append(int(row["type"], 16))
    return out


def validate_overrides(
    path: pathlib.Path,
    source_root: pathlib.Path,
    registered: set[tuple[str, str]],
) -> list[str]:
    failures: list[str] = []
    lines = path.read_text(encoding="utf-8").splitlines()
    header = lines[0].split("\t") if lines else []
    seen: set[tuple[str, str, int]] = set()
    for line in lines[1:]:
        if not line or line.startswith("#"):
            continue
        row = dict(zip(header, line.split("\t")))
        key = (row["family"].strip().upper(), row["symbol"].strip())
        identity = (*key, int(row["type"], 16))
        if key not in registered:
            failures.append(f"outbound override has no matching registration: {key}")
        source = source_root / row["source"]
        if not source.exists():
            failures.append(f"outbound override source does not exist: {row['source']}")
        if identity in seen:
            failures.append(f"duplicate outbound override: {identity}")
        seen.add(identity)
    return failures


def outbound_registrations(
    sources: dict[pathlib.Path, str],
    source_root: pathlib.Path,
    constants: dict[str, int],
    overrides: dict[tuple[str, str], list[int]],
) -> tuple[list[Registration], list[str]]:
    symbols, failures = registration_symbols(sources, source_root)
    rows: list[Registration] = []
    for family, message, encoder, registration_source in symbols:
        key = (family, message)
        values = overrides.get(key)
        evidence_source = registration_source

        if values is None and encoder == "<generic>":
            for path, text in sources.items():
                body = record_body(text, message)
                if body is None:
                    continue
                match = re.search(r"override\s+ushort\s+Type\s*=>\s*([^;]+);", body)
                if match:
                    value = resolve_expr(match.group(1), constants)
                    if value is not None:
                        values = [value]
                        evidence_source = relative(path, source_root)
                break

        if values is None and encoder != "<generic>":
            for path, text in sources.items():
                body = class_body(text, encoder)
                if body is None:
                    continue
                expressions = re.findall(r"\bOnePayload\s*\(\s*([^,\r\n]+)", body)
                found = [resolve_expr(expr, constants) for expr in expressions]
                values = sorted({value for value in found if value is not None}) or None
                evidence_source = relative(path, source_root)
                break

        if values is None:
            failures.append(
                f"outbound registration {family} {message}/{encoder} has no resolvable wire type; "
                "add a source-backed row to server-wire-overrides.tsv"
            )
            continue
        for value in values:
            rows.append(
                Registration(
                    Identity("S2C", family, value), message, evidence_source
                )
            )
    return rows, failures


CATALOG_ROW_RE = re.compile(
    r"^\|\s*`(?P<command>0x(?:00|01))`(?:\s+(?P<family>CMD|NOTI))?\s*\|\s*"
    r"`(?P<type>0x[0-9A-Fa-f]{4})`\s*\|",
    re.M,
)


def catalog_identities(text: str) -> set[Identity]:
    out: set[Identity] = set()
    for match in CATALOG_ROW_RE.finditer(text):
        family = match.group("family") or ("CMD" if match.group("command") == "0x01" else "NOTI")
        # Rows without an explicit family belong to the C->S table; rows that spell the
        # family belong to the S->C table.  The generated coverage section always spells it.
        direction = "S2C" if match.group("family") else "C2S"
        out.add(Identity(direction, family, int(match.group("type"), 16)))
    return out


def compare(
    inbound: list[Registration],
    outbound: list[Registration],
    catalog: set[Identity],
    protocol_root: pathlib.Path,
) -> list[str]:
    failures: list[str] = []
    expected_in = {row.identity for row in inbound}
    expected_out = {row.identity for row in outbound}
    for identity in sorted(expected_in - catalog):
        failures.append(f"catalog missing registered inbound {identity.family} {identity.type_text}")
    for identity in sorted(expected_out - catalog):
        failures.append(f"catalog missing encoder-backed outbound {identity.family} {identity.type_text}")
    for identity in sorted(expected_in | expected_out):
        path = protocol_root / identity.doc
        if not path.exists():
            failures.append(
                f"protocol page missing for {identity.direction} {identity.family} "
                f"{identity.type_text}: {identity.doc}"
            )
    return failures


def source_scan_failures(
    source_root: pathlib.Path,
    sources: dict[pathlib.Path, str],
    inbound: list[Registration],
    outbound_symbols: list[tuple[str, str, str, str]],
) -> list[str]:
    """Reject a coverage check whose source registration scan has gone blind."""
    failures: list[str] = []
    if not source_root.is_dir() or not sources:
        return [f"server protocol source root is missing or empty: {source_root}"]
    if not inbound:
        failures.append("no C2S decoder registrations extracted from server source")
    for family in ("CMD", "NOTI"):
        if not any(row[0] == family for row in outbound_symbols):
            failures.append(f"no S2C {family} encoder registrations extracted from server source")

    inbound_site = re.compile(r"\.\s*AddGameCmdMessageDecoder\s*<")
    direct_site = re.compile(
        r"\.\s*AddGame(?P<family>Cmd|Noti)MessageEncoder\s*<\s*(?!TMessage\b)"
    )
    generic_site = re.compile(
        r"\bAdd(?P<family>Cmd|Noti)\s*<\s*(?!TMessage\b)[A-Za-z_]\w*\s*>"
    )
    for path, source in sources.items():
        name = relative(path, source_root)
        observed = len(inbound_site.findall(source))
        extracted = sum(row.source == name for row in inbound)
        if observed != extracted:
            failures.append(
                f"unparsed C2S decoder registration in {name}: "
                f"{observed} call sites, {extracted} extracted"
            )
        for family in ("CMD", "NOTI"):
            observed_direct = sum(
                match.group("family").upper() == family
                for match in direct_site.finditer(source)
            )
            extracted_direct = sum(
                row[0] == family and row[2] != "<generic>" and row[3] == name
                for row in outbound_symbols
            )
            if observed_direct != extracted_direct:
                failures.append(
                    f"unparsed S2C {family} encoder registration in {name}: "
                    f"{observed_direct} call sites, {extracted_direct} extracted"
                )
            if "StateSynchronization" in name:
                observed_generic = sum(
                    match.group("family").upper() == family
                    for match in generic_site.finditer(source)
                )
                extracted_generic = sum(
                    row[0] == family and row[2] == "<generic>" and row[3] == name
                    for row in outbound_symbols
                )
                if observed_generic != extracted_generic:
                    failures.append(
                        f"unparsed S2C {family} generic registration in {name}: "
                        f"{observed_generic} call sites, {extracted_generic} extracted"
                    )
    return failures


def catalog_link_failures(catalog_path: pathlib.Path, text: str) -> list[str]:
    """Return missing repository-local Markdown targets referenced by the catalog."""
    failures: list[str] = []
    for match in re.finditer(r"\[[^\]]*\]\((?P<target>[^)]+)\)", text):
        target = match.group("target").strip().strip("<>")
        if not target or target.startswith("#") or re.match(r"^[A-Za-z][A-Za-z0-9+.-]*:", target):
            continue
        path_text = target.split("#", 1)[0].split("?", 1)[0]
        if not path_text:
            continue
        resolved = (catalog_path.parent / path_text).resolve()
        if not resolved.exists():
            failures.append(f"catalog link target does not exist: {target}")
    return sorted(set(failures))


SERVER_COVERAGE_BEGIN = "<!-- rks:server-coverage:begin -->"
SERVER_COVERAGE_END = "<!-- rks:server-coverage:end -->"


def render_server_coverage(
    inbound: list[Registration], outbound: list[Registration]
) -> str:
    grouped: dict[Identity, set[str]] = {}
    for row in inbound + outbound:
        grouped.setdefault(row.identity, set()).add(row.symbol)

    lines = [
        SERVER_COVERAGE_BEGIN,
        "",
        "> **本区由 `patterns/data/check-server-documentation-sync.py --write-catalog` 从当前",
        "> `Dfo.Server.Protocol` 接入面生成。**它只回答“代码目前注册/可发送什么”，不证明",
        "> type、布局、响应或客户端效果正确；成熟度仍以本页上方进度表和协议页证据为准。",
        "",
        "### C→S：已注册 decoder",
        "",
        "| Command | Type | 当前代码入口 | 证据边界 | 文档 |",
        "|---:|---:|---|---|---|",
    ]
    for identity in sorted(key for key in grouped if key.direction == "C2S"):
        symbols = "、".join(f"`{item}`" for item in sorted(grouped[identity]))
        lines.append(
            f"| `{identity.command_text}` | `{identity.type_text}` | {symbols} | "
            f"仅证明 decoder 已注册 | [{identity.type_text}]({identity.doc}) |"
        )

    lines += [
        "",
        "### S→C：encoder-backed wire identity",
        "",
        "| Command | Type | 当前代码入口 | 证据边界 | 文档 |",
        "|---:|---:|---|---|---|",
    ]
    for identity in sorted(key for key in grouped if key.direction == "S2C"):
        symbols = "、".join(f"`{item}`" for item in sorted(grouped[identity]))
        lines.append(
            f"| `{identity.command_text}` {identity.family} | `{identity.type_text}` | {symbols} | "
            f"仅证明 encoder 可产生该 identity | [{identity.type_text}]({identity.doc}) |"
        )
    lines += ["", SERVER_COVERAGE_END]
    return "\n".join(lines)


def write_catalog_coverage(
    catalog_path: pathlib.Path,
    inbound: list[Registration],
    outbound: list[Registration],
) -> None:
    text = catalog_path.read_text(encoding="utf-8")
    rendered = render_server_coverage(inbound, outbound)
    if SERVER_COVERAGE_BEGIN in text and SERVER_COVERAGE_END in text:
        before = text.split(SERVER_COVERAGE_BEGIN, 1)[0]
        after = text.split(SERVER_COVERAGE_END, 1)[1]
        new_text = before + rendered + after
    else:
        new_text = (
            text.rstrip()
            + "\n\n## 当前服务端接入覆盖（实现现状，不是协议真相）\n\n"
            + "这张表补足上方进度表没有承担的代码覆盖职责。自动检查以此区与当前源码对账；"
              "发现差异后仍需回到 A21 逆向、线级记录与真实客户端行为裁决，不能按代码反推协议。\n\n"
            + rendered
            + "\n"
        )
    catalog_path.write_text(new_text, encoding="utf-8", newline="\n")


def self_test() -> int:
    with tempfile.TemporaryDirectory() as temp:
        source_root = pathlib.Path(temp) / "Server" / "src" / "Dfo.Server.Protocol"
        source_root.mkdir(parents=True)
        source = source_root / "ProtocolModule.cs"
        source.write_text(
            "public const ushort SampleType = 0x0084;\n"
            "services.AddGameCmdMessageDecoder<SampleDecoder>(GameProtocol.SampleType);\n"
            "services.AddGameCmdMessageEncoder<SampleCmd, SampleCmdEncoder>();\n"
            "services.AddGameNotiMessageEncoder<SampleNoti, SampleNotiEncoder>();\n",
            encoding="utf-8",
        )
        sources = load_sources(source_root)
        inbound, inbound_errors = inbound_registrations(
            sources, source_root, resolve_constants(sources)
        )
        symbols, symbol_errors = registration_symbols(sources, source_root)
        if inbound_errors or symbol_errors or len(inbound) != 1:
            print("self-test failed: valid source registration was not extracted")
            return 1
        if source_scan_failures(source_root, sources, inbound, symbols):
            print("self-test failed: valid source registration was rejected")
            return 1
        missing = compare(inbound, [], set(), pathlib.Path(temp) / "protocol")
        if not any("catalog missing registered inbound CMD 0x0084" in item for item in missing):
            print("self-test failed: extracted registration missing from catalog was not detected")
            return 1
        outbound = [
            Registration(Identity("S2C", "NOTI", 0x0013), "SampleNoti", "ProtocolModule.cs")
        ]
        missing = compare(
            inbound, outbound, {Identity("C2S", "CMD", 0x0084)},
            pathlib.Path(temp) / "protocol",
        )
        if not any("catalog missing encoder-backed outbound NOTI 0x0013" in item for item in missing):
            print("self-test failed: outbound registration missing from catalog was not detected")
            return 1
        qualified = {source: sources[source].replace("GameProtocol.SampleType", "CreaturePayloadCodec.SampleType")}
        qualified_inbound, qualified_errors = inbound_registrations(
            qualified, source_root, resolve_constants(qualified)
        )
        if qualified_errors or qualified_inbound != inbound:
            print("self-test failed: slice-qualified constant registration was not extracted")
            return 1
        helper_source = source_root / "Extensions.cs"
        qualified[helper_source] = "public static void AddGameCmdMessageDecoder<TDecoder>(this IServiceCollection services) {}"
        helper_inbound, helper_errors = inbound_registrations(
            qualified, source_root, resolve_constants(qualified)
        )
        if helper_errors or helper_inbound != inbound:
            print("self-test failed: generic extension declaration was treated as a registration")
            return 1
        unresolved = {source: sources[source].replace("GameProtocol.SampleType", "CreaturePayloadCodec.UnknownType")}
        _, unresolved_errors = inbound_registrations(unresolved, source_root, resolve_constants(unresolved))
        if not unresolved_errors:
            print("self-test failed: unresolved qualified constant was silently accepted")
            return 1
        drifted = {source: sources[source].replace("GameProtocol.SampleType", "ResolveType()")}
        drifted_inbound, _ = inbound_registrations(
            drifted, source_root, resolve_constants(drifted)
        )
        drifted_symbols, _ = registration_symbols(drifted, source_root)
        if not source_scan_failures(source_root, drifted, drifted_inbound, drifted_symbols):
            print("self-test failed: unparsed inbound registration was not detected")
            return 1
        drifted = {
            source: sources[source].replace(
                "AddGameNotiMessageEncoder<SampleNoti, SampleNotiEncoder>();",
                "AddGameNotiMessageEncoder<SampleNoti, SampleNotiEncoder>(new object());",
            )
        }
        drifted_inbound, _ = inbound_registrations(
            drifted, source_root, resolve_constants(drifted)
        )
        drifted_symbols, _ = registration_symbols(drifted, source_root)
        if not source_scan_failures(source_root, drifted, drifted_inbound, drifted_symbols):
            print("self-test failed: unparsed outbound registration was not detected")
            return 1
        if not source_scan_failures(source_root / "missing", {}, [], []):
            print("self-test failed: missing source root was not detected")
            return 1
    malformed = catalog_identities("| `0x00` NOTI | `0x0013` | X | Y | Z | bad.md |\n")
    if malformed != {Identity("S2C", "NOTI", 0x0013)}:
        print("self-test failed: CMD/NOTI catalog identity parsing is not stable")
        return 1
    print("=== server documentation sync self-test ===")
    print("  extracted inbound missing from catalog: CMD 0x0084 (detected)")
    print("  outbound missing from catalog: NOTI 0x0013 (detected)")
    print("  unparsed registrations and missing source root: detected")
    print("  same-type family parsing     : passed")
    print("failures: 0")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=pathlib.Path)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--write-catalog", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        return self_test()
    if args.repo_root is None:
        parser.error("--repo-root is required unless --self-test is used")

    repo_root = args.repo_root.resolve()
    source_root = repo_root / "Server" / "src" / "Dfo.Server.Protocol"
    protocol_root = repo_root / "protocol"
    sources = load_sources(source_root)
    constants = resolve_constants(sources)
    inbound, failures = inbound_registrations(sources, source_root, constants)
    outbound_symbols, symbol_failures = registration_symbols(sources, source_root)
    failures.extend(symbol_failures)
    failures.extend(source_scan_failures(source_root, sources, inbound, outbound_symbols))
    overrides = load_overrides(pathlib.Path(__file__).with_name("server-wire-overrides.tsv"))
    outbound, outbound_failures = outbound_registrations(
        sources, source_root, constants, overrides
    )
    failures.extend(outbound_failures)
    registered_symbols = {
        (family, message)
        for family, message, _encoder, _source in registration_symbols(sources, source_root)[0]
    }
    failures.extend(
        validate_overrides(
            pathlib.Path(__file__).with_name("server-wire-overrides.tsv"),
            source_root,
            registered_symbols,
        )
    )
    catalog_path = protocol_root / "catalog.md"
    if args.write_catalog and not failures:
        write_catalog_coverage(catalog_path, inbound, outbound)
    catalog_text = catalog_path.read_text(encoding="utf-8")
    catalog = catalog_identities(catalog_text)
    failures.extend(compare(inbound, outbound, catalog, protocol_root))
    failures.extend(catalog_link_failures(catalog_path, catalog_text))

    inbound_ids = {row.identity for row in inbound}
    outbound_ids = {row.identity for row in outbound}
    print("=== server documentation sync ===")
    print(f"  registered C2S decoders       : {len(inbound)} registrations / {len(inbound_ids)} identities")
    print(f"  encoder-backed S2C identities : {len(outbound_ids)}")
    print(
        "    CMD / NOTI                  : "
        f"{sum(1 for row in outbound_ids if row.family == 'CMD')} / "
        f"{sum(1 for row in outbound_ids if row.family == 'NOTI')}"
    )
    print(f"  catalog identities parsed     : {len(catalog)}")
    print(f"  explicit outbound overrides   : {sum(len(v) for v in overrides.values())} rows")
    print(f"failures: {len(failures)}")
    for failure in failures:
        print("  " + failure)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
