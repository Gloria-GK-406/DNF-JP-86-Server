#include <windows.h>

#include "clienthack/sha256.h"
#include "iat_table.h"

#include <stdio.h>
#include <string.h>

static HMODULE g_capture_module;
static const uint8_t g_supported_hash[32] = {
    0xF7, 0x8F, 0x0A, 0xEA, 0x51, 0x37, 0xDF, 0x06,
    0xB6, 0x3E, 0xD0, 0xF7, 0x47, 0x0B, 0x66, 0xF3,
    0xA2, 0x4D, 0x06, 0x1B, 0x69, 0xD4, 0xDA, 0x13,
    0x75, 0x8E, 0x65, 0x57, 0x4A, 0x7F, 0xC0, 0x4C
};

typedef struct capture_target {
    const char* name;
    uintptr_t rva;
    uint32_t length;
} capture_target;

static const capture_target targets[] = {
    { "n5_check_1", 0x005F7CE0u, 32 },
    { "n5_check_2", 0x00D8A160u, 32 },
    { "n5_check_3", 0x00D8B1D0u, 32 },
    { "cipher_encrypt", 0x01C11360u, 64 },
    { "cipher_decrypt", 0x01C11420u, 64 },
    { "socket_write", 0x048FF9ABu, 64 },
    { "protection_005F80FD", 0x005F80FDu, 16 },
    { "protection_00914B81", 0x00914B81u, 16 },
    { "protection_00B9EB0A", 0x00B9EB0Au, 16 },
    { "protection_02122BDF", 0x02122BDFu, 16 },
    { "protection_01120157", 0x01120157u, 16 },
    { "protection_0189D104", 0x0189D104u, 16 },
    { "protection_018AD5FB", 0x018AD5FBu, 16 },
    { "protection_00AB5C80", 0x00AB5C80u, 16 },
    { "network_xor_00D8999B", 0x00D8999Bu, 16 },
    { "az_string_028D5C9C", 0x028D5C9Cu, 32 }
};

static int hash_file(const wchar_t* path, uint8_t digest[32])
{
    HANDLE file = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, NULL,
        OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN, NULL);
    uint8_t buffer[65536];
    DWORD amount;
    ch_sha256_context context;
    if (file == INVALID_HANDLE_VALUE) return 0;
    ch_sha256_init(&context);
    do {
        if (!ReadFile(file, buffer, sizeof(buffer), &amount, NULL)) {
            CloseHandle(file);
            return 0;
        }
        ch_sha256_update(&context, buffer, amount);
    } while (amount != 0);
    CloseHandle(file);
    ch_sha256_final(&context, digest);
    return 1;
}

static int module_directory(wchar_t path[MAX_PATH])
{
    DWORD length = GetModuleFileNameW(g_capture_module, path, MAX_PATH);
    wchar_t* slash;
    if (length == 0 || length >= MAX_PATH) return 0;
    slash = wcsrchr(path, L'\\');
    if (slash == NULL) return 0;
    *slash = L'\0';
    return 1;
}

static int readable_range(const void* address, size_t length)
{
    MEMORY_BASIC_INFORMATION memory;
    if (VirtualQuery(address, &memory, sizeof(memory)) != sizeof(memory) ||
        memory.State != MEM_COMMIT || (memory.Protect & (PAGE_GUARD | PAGE_NOACCESS)) != 0) return 0;
    return (uintptr_t)address + length <=
        (uintptr_t)memory.BaseAddress + memory.RegionSize;
}

static int wait_ready(uintptr_t base)
{
    static const uint8_t ready[] = { 0x8B, 0xFF, 0x55, 0x8B };
    DWORD elapsed = 0;
    while (elapsed < 120000u) {
        const uint8_t* address = (const uint8_t*)(base + 0x0213BBBFu);
        if (readable_range(address, sizeof(ready)) && memcmp(address, ready, sizeof(ready)) == 0) return 1;
        Sleep(10);
        elapsed += 10;
    }
    return 0;
}

static void write_hex(FILE* output, const uint8_t* bytes, size_t length)
{
    size_t index;
    for (index = 0; index < length; ++index) fprintf(output, "%02X", bytes[index]);
}

static void capture_targets(FILE* output, uintptr_t base, const char* phase)
{
    size_t index;
    for (index = 0; index < sizeof(targets) / sizeof(targets[0]); ++index) {
        const uint8_t* address = (const uint8_t*)(base + targets[index].rva);
        const int readable = readable_range(address, targets[index].length);
        fprintf(output, "{\"type\":\"target\",\"phase\":\"%s\",\"name\":\"%s\",\"rva\":\"%08lX\",\"readable\":%s,\"bytes\":\"",
            phase, targets[index].name, (unsigned long)targets[index].rva,
            readable ? "true" : "false");
        if (readable) write_hex(output, address, targets[index].length);
        fprintf(output, "\"}\r\n");
    }
}

static HMODULE get_or_load_module(const char* name)
{
    HMODULE module = GetModuleHandleA(name);
    if (module == NULL) {
        module = LoadLibraryExA(name, NULL,
            LOAD_LIBRARY_SEARCH_APPLICATION_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    }
    return module;
}

static int resolve_and_repair_iat(FILE* output, uintptr_t base)
{
    FARPROC resolved[CH_IAT_ENTRY_COUNT];
    uintptr_t current[CH_IAT_ENTRY_COUNT];
    size_t index;
    size_t resolved_count = 0;

    for (index = 0; index < CH_IAT_ENTRY_COUNT; ++index) {
        const ch_iat_entry* entry = &g_ch_iat_table[index];
        const uintptr_t rva = entry->preferred_va - 0x00400000u;
        const uintptr_t* slot = (const uintptr_t*)(base + rva);
        HMODULE module = get_or_load_module(entry->module_name);
        current[index] = readable_range(slot, sizeof(*slot)) ? *slot : 0;
        resolved[index] = module != NULL ? GetProcAddress(module, entry->function_name) : NULL;
        fprintf(output,
            "{\"type\":\"iat\",\"rva\":\"%08lX\",\"module\":\"%s\",\"function\":\"%s\",\"current\":\"%08lX\",\"resolved\":\"%08lX\",\"equal\":%s}\r\n",
            (unsigned long)rva, entry->module_name, entry->function_name,
            (unsigned long)current[index], (unsigned long)(uintptr_t)resolved[index],
            current[index] != 0 && current[index] == (uintptr_t)resolved[index] ? "true" : "false");
        if (resolved[index] != NULL) ++resolved_count;
    }
    fflush(output);
    if (resolved_count == 0) return 0;

    for (index = 0; index < CH_IAT_ENTRY_COUNT; ++index) {
        const uintptr_t rva = g_ch_iat_table[index].preferred_va - 0x00400000u;
        uintptr_t* slot = (uintptr_t*)(base + rva);
        DWORD old_protection;
        if (resolved[index] == NULL) continue;
        if (!VirtualProtect(slot, sizeof(*slot), PAGE_READWRITE, &old_protection)) return 0;
        *slot = (uintptr_t)resolved[index];
        if (!VirtualProtect(slot, sizeof(*slot), old_protection, &old_protection)) return 0;
    }
    FlushInstructionCache(GetCurrentProcess(), NULL, 0);
    fprintf(output, "{\"type\":\"iat_repair_summary\",\"resolved\":%lu,\"unresolved\":%lu}\r\n",
        (unsigned long)resolved_count,
        (unsigned long)(CH_IAT_ENTRY_COUNT - resolved_count));
    return 1;
}

static DWORD WINAPI capture_thread(void* parameter)
{
    wchar_t root[MAX_PATH];
    wchar_t dnf_path[MAX_PATH];
    wchar_t report_path[MAX_PATH];
    uint8_t actual_hash[32];
    uintptr_t base = (uintptr_t)GetModuleHandleW(NULL);
    FILE* output;
    int iat_repaired;
    (void)parameter;

    if (!module_directory(root) ||
        _snwprintf_s(dnf_path, MAX_PATH, _TRUNCATE, L"%s\\DNF.exe", root) <= 0 ||
        _snwprintf_s(report_path, MAX_PATH, _TRUNCATE, L"%s\\clienthack-capture-%lu.jsonl",
            root, (unsigned long)GetCurrentProcessId()) <= 0 ||
        !hash_file(dnf_path, actual_hash) || memcmp(actual_hash, g_supported_hash, 32) != 0) {
        MessageBoxW(NULL, L"Capture refused: unsupported DNF.exe", L"ClientHack Capture", MB_OK | MB_ICONERROR);
        TerminateProcess(GetCurrentProcess(), ERROR_BAD_EXE_FORMAT);
        return 1;
    }
    if (!wait_ready(base)) {
        MessageBoxW(NULL, L"Capture timeout waiting for unpacked client", L"ClientHack Capture", MB_OK | MB_ICONERROR);
        TerminateProcess(GetCurrentProcess(), WAIT_TIMEOUT);
        return 1;
    }
    if (_wfopen_s(&output, report_path, L"wb") != 0 || output == NULL) {
        TerminateProcess(GetCurrentProcess(), ERROR_WRITE_FAULT);
        return 1;
    }
    fprintf(output, "{\"type\":\"meta\",\"pid\":%lu,\"dnf_base\":\"%08lX\",\"iat_count\":%lu}\r\n",
        (unsigned long)GetCurrentProcessId(), (unsigned long)base,
        (unsigned long)CH_IAT_ENTRY_COUNT);
    capture_targets(output, base, "pre_iat");
    iat_repaired = resolve_and_repair_iat(output, base);
    fprintf(output, "{\"type\":\"iat_repair\",\"success\":%s}\r\n",
        iat_repaired ? "true" : "false");
    fflush(output);
    if (iat_repaired) {
        Sleep(3000);
        capture_targets(output, base, "post_iat_wait");
    }
    fclose(output);
    MessageBoxW(NULL, report_path, L"ClientHack capture complete", MB_OK | MB_ICONINFORMATION);
    TerminateProcess(GetCurrentProcess(), 0);
    return 0;
}

static void __cdecl ts_noop_cdecl(void) {}
static void __stdcall ts_noop_2(int a, int b) { (void)a; (void)b; }
static void __stdcall ts_noop_3(int a, int b, int c) { (void)a; (void)b; (void)c; }
static void __stdcall ts_out_zero(int a, int b, int c, int d, int e, int f, int* out)
{
    (void)a; (void)b; (void)c; (void)d; (void)e; (void)f;
    if (out != NULL) *out = 0;
}
static int __stdcall ts_return_zero(int a) { (void)a; return 0; }
static int __stdcall ts_return_one(int a) { (void)a; return 1; }
/* The legacy CreateObj ABI exposes untyped vtable slots. On Win32, code and
 * data pointers have the same representation; keep the compatibility table
 * explicit and confine MSVC's non-standard conversion warning here. */
#pragma warning(push)
#pragma warning(disable: 4152)
static void* g_vtable_a[10] = {
    ts_noop_3, ts_noop_cdecl, ts_noop_2, ts_noop_cdecl, ts_noop_cdecl,
    ts_noop_cdecl, ts_noop_cdecl, ts_noop_cdecl, ts_out_zero, NULL
};
static unsigned int g_vtable_b_data[6];
static void* g_vtable_b[20] = {
    ts_return_zero, ts_noop_cdecl, ts_noop_cdecl, ts_noop_cdecl, ts_noop_cdecl,
    ts_noop_cdecl, ts_noop_cdecl, ts_noop_cdecl, ts_noop_cdecl, ts_noop_cdecl,
    ts_noop_cdecl, ts_noop_2, ts_noop_cdecl, ts_noop_cdecl, ts_noop_cdecl,
    ts_noop_cdecl, ts_return_one, g_vtable_b_data, ts_noop_3, ts_noop_cdecl
};
#pragma warning(pop)
static void* g_tersafe_object[9];
static INIT_ONCE g_tersafe_once = INIT_ONCE_STATIC_INIT;
static BOOL CALLBACK ts_initialize(PINIT_ONCE once, PVOID parameter, PVOID* context)
{
    (void)once; (void)parameter; (void)context;
    g_tersafe_object[0] = g_vtable_a;
    g_tersafe_object[4] = g_vtable_b;
    return TRUE;
}
void** __cdecl CreateObj(char type)
{
    InitOnceExecuteOnce(&g_tersafe_once, ts_initialize, NULL, NULL);
    if (type == 3) return &g_tersafe_object[0];
    if (type == 7) return &g_tersafe_object[4];
    return NULL;
}
void __cdecl ClearRSInfo(void) {}
void __cdecl PutRSInfo(void) {}
void __cdecl Test(void) {}
const char* __cdecl ijlErrorStr(int error) { (void)error; return "capture"; }
void __cdecl ijlFree(void* value) { (void)value; }
int __cdecl ijlGetLibVersion(void) { return 0; }
int __cdecl ijlInit(void* value) { (void)value; return 0; }
int __cdecl ijlRead(void* value, int flags) { (void)value; (void)flags; return 0; }
int __cdecl ijlWrite(void* value, int flags) { (void)value; (void)flags; return 0; }

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, void* reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        HANDLE thread;
        g_capture_module = instance;
        DisableThreadLibraryCalls(instance);
        thread = CreateThread(NULL, 0, capture_thread, NULL, 0, NULL);
        if (thread == NULL) return FALSE;
        CloseHandle(thread);
    }
    return TRUE;
}
