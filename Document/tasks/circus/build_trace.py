"""Build a task-private 0050 tracing variant; never deploy or modify a shared client.

The original plugin owns all three cipher/socket hooks. Extend that owner rather
than loading NetworkHook beside it and competing for the same patch sites.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument("--source", type=Path, default=Path(r"E:\Project\DNF\DNF-JP-86-ClientHack\ClientHack"))
parser.add_argument("--output", type=Path, default=Path("artifacts/circus-client-trace"))
args = parser.parse_args()
source = args.source.resolve()
output = args.output.resolve()
workspace = Path.cwd().resolve()
if not output.is_relative_to(workspace):
    raise SystemExit("Trace build output must stay inside this task worktree")
output.mkdir(parents=True, exist_ok=True)
original = source / "plugins/0050_a21_runtime_fix/a21_runtime_fix.c"
raw = original.read_bytes()
code = raw.decode("utf-8-sig")

def replace_once(old, new):
    global code
    if code.count(old) != 1:
        raise SystemExit(f"Source shape changed; review required before applying trace adaptation: {old[:75]!r}")
    code = code.replace(old, new)

replace_once("#include <wchar.h>", "#include <wchar.h>\n#include <intrin.h>")
replace_once("static SRWLOCK g_packet_trace_lock = SRWLOCK_INIT;", """static SRWLOCK g_packet_trace_lock = SRWLOCK_INIT;
static void* g_original_linked_setter;
""")
replace_once("(packet_type == 0x0023 || packet_type == 0x0045\n            || packet_type == 0x0046 || packet_type == 0x0047)",
    """(packet_type == 0x000F || packet_type == 0x0010 || packet_type == 0x0016
            || packet_type == 0x001B || packet_type == 0x001D || packet_type == 0x001F
            || packet_type == 0x0023 || packet_type == 0x0027 || packet_type == 0x002B
            || packet_type == 0x0045 || packet_type == 0x0046 || packet_type == 0x0047
            || packet_type == 0x0048 || packet_type == 0x0227 || packet_type == 0x0282
            || (strcmp(direction, "send") == 0 && packet_type != 0x0001 && packet_type != 0x0002)
            || strcmp(direction, "setter") == 0)""")
replace_once('L"clienthack-a21-card-trace.log"', 'L"circus-native-trace.jsonl"')
replace_once('L"A21CardTrace", L"Enabled", 0, ini)', 'L"A21CircusTrace", L"Enabled", 0, ini)')
replace_once('"card packet trace enabled for types 0x0023 and 0x0045-0x0047"',
    '"task-private circus trace enabled; no LOGIN/USERINFO bodies recorded"')
replace_once('    fputs("\\\"}\\n", file);', r'''    {
        void* stack[24];
        USHORT frames = CaptureStackBackTrace(2, 24, stack, NULL);
        fputs("\",\"stack_rva\":[", file);
        for (USHORT index = 0; index < frames; ++index) {
            uintptr_t address = (uintptr_t)stack[index];
            uintptr_t rva = g_host != NULL && address >= g_host->dnf_base
                && address < g_host->dnf_base + 0x0746E000u ? address - g_host->dnf_base : 0;
            fprintf(file, "%s%lu", index ? "," : "", (unsigned long)rva);
        }
        fputs("]}\n", file);
    }''')
setter = r'''
/* Exact setter reached by NOTI 0282 at VA 01149609 in the inspected A21 image.
 * Record the actual values and retain the original call. "apply" is a runtime
 * observation, not an independently observed wire frame or proven field name.
 */
/* ECX=this, EDX unused, and two stack arguments matches the original thiscall
 * setter's ABI; MSVC C does not accept a __thiscall function-pointer type. */
typedef void (__fastcall *circus_linked_setter_fn)(void*, void*, int, int);
static void __fastcall circus_linked_setter(void* self, void* unused, int first, int second)
{
    int fields[2] = { first, second };
    int from_noti = g_host != NULL
        && (uintptr_t)_ReturnAddress() == g_host->dnf_base + 0x00D4960Eu;
    (void)unused;
    /* This setter is shared by unrelated native effects. Only the inspected
     * NOTI caller may identify an observation as 0282; other calls use -1. */
    trace_selected_packet(from_noti ? "apply" : "setter", from_noti ? 0x0282 : -1,
        (const char*)fields, sizeof(fields));
#pragma warning(push)
#pragma warning(disable: 4055)
    ((circus_linked_setter_fn)g_original_linked_setter)(self, unused, first, second);
#pragma warning(pop)
}

'''
# Function spelling is anchored to the original owner's public initialization.
replace_once("ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)",
    setter + "ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)")
replace_once("    status = redirect_cthread_callsite();", """    if (g_card_trace_enabled) {
        static const uint8_t signature[] = { 0x55, 0x8B, 0xEC, 0x8B, 0x45, 0x08 };
#pragma warning(push)
#pragma warning(disable: 4055)
        status = create_hook(0x003DB370u, signature, sizeof(signature),
            (void*)circus_linked_setter, &g_original_linked_setter, "circus-linked-setter-observation");
#pragma warning(pop)
        if (status != CH_STATUS_OK) return status;
    }
    status = redirect_cthread_callsite();""")
generated = output / "a21_runtime_fix_trace.c"
generated.write_text(code, encoding="utf-8")
cmake = """cmake_minimum_required(VERSION 3.25)
project(CircusNativeTrace C)
add_library(circus_trace SHARED a21_runtime_fix_trace.c
  "@ROOT@/plugins/plugin_entry.c" "@ROOT@/plugins/plugin.def")
target_include_directories(circus_trace PRIVATE "@ROOT@/include" "@ROOT@/plugins")
target_compile_definitions(circus_trace PRIVATE CLIENTHACK_CLIENT_A21=1
  CLIENTHACK_PLUGIN_NAME="A21RuntimeFix" CLIENTHACK_PLUGIN_FLAGS=CH_PLUGIN_REQUIRED)
target_compile_options(circus_trace PRIVATE /W4 /WX)
set_target_properties(circus_trace PROPERTIES PREFIX "" OUTPUT_NAME "0050_A21RuntimeFix")
add_executable(trace_harness "@HARNESS@")
target_include_directories(trace_harness PRIVATE "@ROOT@/include")
target_compile_options(trace_harness PRIVATE /W4 /WX)
""".replace("@ROOT@", source.as_posix())
cmake = cmake.replace("@HARNESS@", (Path(__file__).parent / "trace_harness.c").resolve().as_posix())
(output / "CMakeLists.txt").write_text(cmake, encoding="utf-8")
subprocess.run(["cmake", "-S", str(output), "-B", str(output / "build"), "-G", "Visual Studio 17 2022", "-A", "Win32"], check=True)
subprocess.run(["cmake", "--build", str(output / "build"), "--config", "Release", "--target", "circus_trace", "trace_harness", "--parallel", "1"], check=True)
dll = output / "build/Release/0050_A21RuntimeFix.dll"
fixture_root = dll.parent
(fixture_root / "ClientHack.ini").write_text("[A21CircusTrace]\nEnabled=1\n", encoding="utf-8")
fixture_log = fixture_root / "circus-native-trace.jsonl"
if fixture_log.exists():
    fixture_log.unlink()
subprocess.run([str(fixture_root / "trace_harness.exe"), str(dll)], check=True, cwd=fixture_root)
fixture = [json.loads(line) for line in fixture_log.read_text(encoding="utf-8").splitlines()]
assert [(row["dir"], row["type"], row["hex"]) for row in fixture] == [
    ("send", 0x0048, "0103"), ("send", 0x0357, "0103"),
    ("recv", 0x0282, "1210000000000000"), ("setter", -1, "1210000000000000")]
assert all(row["dumped"] == row["body_len"] and row["stack_rva"] for row in fixture)
manifest = {"source": str(original), "source_sha256": hashlib.sha256(raw).hexdigest(),
    "generated_sha256": hashlib.sha256(generated.read_bytes()).hexdigest(),
    "dll": str(dll), "dll_sha256": hashlib.sha256(dll.read_bytes()).hexdigest(),
    "deployed": False, "client_verified": False, "synthetic_harness_passed": True,
    "log": "circus-native-trace.jsonl", "configuration": "[A21CircusTrace] Enabled=1",
    "limits": "No LOGIN/USERINFO capture; inbound selector is unknown; apply rows are hook observations"}
(output / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
print(json.dumps(manifest, indent=2))
