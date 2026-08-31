"""Generate the compiled-in sample table for the 0150_WrapperPilot plugin.

The pilot rewrites a handful of named Themida wrapper ENTRIES at runtime to
jump straight to the resolved API, using the per-convention templates from the
rewrite plan in Docs/themida-api-wrapper-recovery.md. Whether a sample is safe
to overwrite was decided by wrapper_rewrite_safety.py; this script turns that
output plus the wrapper census into a header the plugin compiles in.

A sample is eligible only if
  * its convention is one of the four canonical groups the templates cover;
  * no control transfer from still-running code lands inside its first 16
    bytes (host absent from wrapper-prefix-refs-all.csv);
  * the next named wrapper entry is at least GAP_MIN bytes away, so the
  * patch cannot run into it;
    patch cannot run into it;
  * the module is loaded before the startup gate (kernel32/user32/gdi32),
    because the plugin resolves and patches at init time.

Each wrapper serves exactly one call site, so the caller column prefers
.text-sourced rows: a wrapper whose call site is game code is the one that
actually executes during a session.

Usage:
  python generate_wrapper_pilot.py --dump <dumpdir> \
      --wrappers wrapper-apis-a21.csv \
      --prefix-refs wrapper-prefix-refs-all.csv \
      --out ../plugins/0150_wrapper_pilot/wrapper_pilot_samples.h
"""
import argparse
import csv
from pathlib import Path

PATCH_LEN = 10   # longest template: A-group is 10 bytes
GAP_MIN = 11     # patch must not run into the next named entry

# (module, function, convention) -> why this sample: a function the client
# exercises every session. Conventions pin the exact group because the same
# API appears at several entries under different groups.
WHITELIST = [
    ("user32.dll", "GetAsyncKeyState", "esp+0/R+1", "input polling"),
    ("user32.dll", "GetWindowRect", "esp+0/R+1", "UI geometry"),
    ("user32.dll", "PtInRect", "esp+0/R+1", "hit testing"),
    ("user32.dll", "GetClientRect", "esp+0/R+1", "UI geometry"),
    ("gdi32.dll", "DeleteObject", "esp+0/R+1", "GDI teardown"),
    ("gdi32.dll", "TextOutW", "esp+0/R+1", "text rendering"),
    ("user32.dll", "GetSystemMetrics", "esp+4/R", "startup metrics"),
    ("user32.dll", "InvalidateRect", "esp+4/R", "UI redraw"),
    ("user32.dll", "PostMessageW", "esp+4/R", "UI messaging"),
    ("user32.dll", "IsWindowVisible", "esp+4/R", "UI state"),
    ("gdi32.dll", "SelectObject", "esp+4/R", "GDI draw loop"),
    ("gdi32.dll", "SetTextColor", "esp+4/R", "GDI draw loop"),
    ("user32.dll", "PeekMessageW", "esp+4/a1", "message loop"),
    ("user32.dll", "GetMessageW", "esp+4/a1", "message loop"),
    ("user32.dll", "ShowCursor", "esp+4/a1", "cursor control"),
    ("kernel32.dll", "GetModuleHandleA", "esp+4/a1", "module lookup"),
    ("user32.dll", "SetFocus", "esp+8/a2", "focus changes"),
    ("user32.dll", "OffsetRect", "esp+8/a2", "UI geometry"),
    ("user32.dll", "GetSystemMetrics", "esp+8/a2", "startup metrics"),
    ("gdi32.dll", "TextOutW", "esp+8/a2", "text rendering"),
]

CONVENTION_CODE = {
    "esp+0/R+1": "WRAPPER_PILOT_CONV_B",      # retaddr +1
    "esp+4/R": "WRAPPER_PILOT_CONV_A",        # swallow caller's first push
    "esp+4/a1": "WRAPPER_PILOT_CONV_C",       # retaddr replaced by a1
    "esp+8/a2": "WRAPPER_PILOT_CONV_D",       # swallow two, a2 as retaddr
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", required=True)
    ap.add_argument("--wrappers", default="wrapper-apis-a21.csv")
    ap.add_argument("--prefix-refs", default="wrapper-prefix-refs-all.csv")
    ap.add_argument("--out", required=True)
    a = ap.parse_args()

    rows = list(csv.DictReader(open(a.wrappers, encoding="utf-8-sig")))
    dirty = {r["host_entry"].lower()
             for r in csv.DictReader(open(a.prefix_refs, encoding="utf-8-sig"))}
    all_entries = sorted({int(r["entry_va"], 16) for r in rows})
    img = (Path(a.dump) / "DNF-memory-rva.bin").read_bytes()
    base = 0x400000

    samples = []
    for module, function, convention, why in WHITELIST:
        cands = [r for r in rows
                 if r["module"] == module and r["function"] == function
                 and r["convention"] == convention
                 and r["entry_va"].lower() not in dirty]
        # a call site in .text is game code and will actually run
        cands.sort(key=lambda r: (r["source_section"] != ".text",
                                  int(r["entry_va"], 16)))
        picked = None
        for r in cands:
            va = int(r["entry_va"], 16)
            nxt = min((e for e in all_entries if e > va), default=va + GAP_MIN)
            if nxt - va >= GAP_MIN:
                picked = r
                break
        if picked is None:
            print("SKIP %-11s %-22s %s: no eligible clean entry"
                  % (convention, function, module))
            continue
        va = int(picked["entry_va"], 16)
        expected = img[va - base:va - base + PATCH_LEN]
        samples.append((picked, va, expected, why))
        print("PICK %-11s %-22s entry=%s call=%s(%s) why=%s"
              % (convention, function, picked["entry_va"],
                 picked["call_site_va"], picked["source_section"], why))

    with open(a.out, "w", encoding="utf-8", newline="\n") as fh:
        w = fh.write
        w("/* Generated by ClientHack/tools/generate_wrapper_pilot.py from\n"
          " * wrapper-apis-a21.csv + wrapper-prefix-refs-all.csv + the\n"
          " * dnf-dump-20260822-232754 dump -- do not edit by hand.\n"
          " *\n"
          " * Every entry below passed the safety census: no still-running\n"
          " * code lands in its first 16 bytes, the next named entry is at\n"
          " * least %d bytes away, and the calling site is .text game code.\n"
          " */\n" % GAP_MIN)
        w("#ifndef CLIENTHACK_WRAPPER_PILOT_SAMPLES_H\n"
          "#define CLIENTHACK_WRAPPER_PILOT_SAMPLES_H\n\n"
          "#include <stdint.h>\n\n"
          "typedef enum wrapper_pilot_convention {\n"
          "    WRAPPER_PILOT_CONV_B = 0,  /* esp+0/R+1: inc [esp]          */\n"
          "    WRAPPER_PILOT_CONV_A = 1,  /* esp+4/R:   pop/pop/push shift */\n"
          "    WRAPPER_PILOT_CONV_C = 2,  /* esp+4/a1:  add esp,4          */\n"
          "    WRAPPER_PILOT_CONV_D = 3   /* esp+8/a2:  add esp,8          */\n"
          "} wrapper_pilot_convention_t;\n\n"
          "typedef struct wrapper_pilot_sample {\n"
          "    uintptr_t entry_rva;\n"
          "    wrapper_pilot_convention_t convention;\n"
          "    const char* module;\n"
          "    const char* function;\n"
          "    uint32_t call_site_rva;   /* the one caller this wrapper serves */\n"
          "    uint8_t expected[10];     /* dump bytes at the entry */\n"
          "} wrapper_pilot_sample_t;\n\n"
          "static const wrapper_pilot_sample_t WRAPPER_PILOT_SAMPLES[] = {\n")
        for r, va, expected, _why in samples:
            w("    { 0x%08Xu, %s, \"%s\", \"%s\", 0x%08Xu,\n"
              "      { %s } },\n"
              % (va - base, CONVENTION_CODE[r["convention"]],
                 r["module"], r["function"],
                 int(r["call_site_va"], 16) - base,
                 ", ".join("0x%02X" % b for b in expected)))
        w("};\n\n"
          "#define WRAPPER_PILOT_SAMPLE_COUNT %du\n\n"
          "#endif\n" % len(samples))
    print("wrote %s (%d samples)" % (a.out, len(samples)))


if __name__ == "__main__":
    main()
