/* Records fatal first-chance exceptions at their origin.
 *
 * The client installs its own unhandled-exception filter that returns
 * EXCEPTION_EXECUTE_HANDLER, so a primary fault is swallowed before WER ever
 * sees it: the process either dies with no Application event at all, or WER
 * records a SECONDARY fault raised inside the crash reporter, which points at
 * the reporter rather than at the bug. A vectored handler at first-chance
 * priority is the only place the real fault is observable.
 *
 * Diagnostics only -- always returns EXCEPTION_CONTINUE_SEARCH, so code that
 * relies on exceptions (including the protector's anti-debug paths) behaves
 * exactly as it would without this plugin. Not a required plugin: losing the
 * recorder must never stop the client from starting.
 *
 * This is the same recorder S4A12's 0006_B1StartupGuard carries, lifted out so
 * a client with no ported guard can still be debugged, and with two additions:
 *
 *   * a call stack, without which the record says where the process died but
 *     not who led it there;
 *   * an in-image test bounded by SizeOfImage. The guard's version tests only
 *     `at >= dnf_base`, which is true of nearly every DLL as well, so DLL
 *     addresses were printed as image RVAs -- a difference of exactly the image
 *     base that reads convincingly like a corrupted pointer. It is not; it is
 *     the log lying. Bounding the test removes the trap.
 */
#include "clienthack/plugin_api.h"

#include <windows.h>
#include <stdio.h>

#define FAULT_MAX_RECORDS 64
#define FAULT_STACK_DEPTH 12

static const ch_host_api* g_host;
static wchar_t g_log_path[MAX_PATH];
static PVOID g_veh_handle;
static volatile LONG g_records;
static uintptr_t g_image_base;
static uintptr_t g_image_end;

static void build_log_path(wchar_t* out, size_t cap, const wchar_t* file_name)
{
    DWORD n = GetModuleFileNameW(NULL, out, (DWORD)cap);
    wchar_t* slash;
    if (n == 0 || n >= cap) {
        wcscpy_s(out, cap, file_name);
        return;
    }
    slash = wcsrchr(out, L'\\');
    if (slash == NULL) {
        wcscpy_s(out, cap, file_name);
        return;
    }
    slash[1] = L'\0';
    if (wcslen(out) + wcslen(file_name) + 1 > cap) {
        wcscpy_s(out, cap, file_name);
        return;
    }
    wcscat_s(out, cap, file_name);
}

/* Bound the image so a DLL address is never mistaken for an image RVA. */
static void resolve_image_range(uintptr_t base)
{
    const IMAGE_DOS_HEADER* dos = (const IMAGE_DOS_HEADER*)base;
    const IMAGE_NT_HEADERS32* nt;
    g_image_base = base;
    g_image_end = base;
    if (base == 0 || dos->e_magic != IMAGE_DOS_SIGNATURE) return;
    nt = (const IMAGE_NT_HEADERS32*)(base + (uintptr_t)dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return;
    g_image_end = base + nt->OptionalHeader.SizeOfImage;
}

/* Codes the runtime raises constantly are skipped so the earliest genuine
 * fault stays easy to find. */
static int is_fatal_code(DWORD code)
{
    switch (code) {
    case 0xE06D7363u:   /* C++ EH */
    case 0x406D1388u:   /* SetThreadName */
    case 0x80000003u:   /* breakpoint */
    case 0x80000004u:   /* single step */
    case 0x4001000Au:   /* debug string */
        return 0;
    default:
        return (code & 0xC0000000u) == 0xC0000000u;
    }
}

/* "0x0128ABCD" for an address inside the client image (an RVA), or
 * "mod+0x1234" style raw absolute otherwise. Kept as one helper so every
 * address in the record is formatted by the same rule. */
static void format_address(char* out, size_t cap, uintptr_t address)
{
    if (address >= g_image_base && address < g_image_end) {
        _snprintf_s(out, cap, _TRUNCATE, "\"0x%08lX\"",
            (unsigned long)(address - g_image_base));
    } else {
        _snprintf_s(out, cap, _TRUNCATE, "\"abs:0x%08lX\"",
            (unsigned long)address);
    }
}

static LONG CALLBACK fault_recorder(PEXCEPTION_POINTERS info)
{
    static volatile LONG reentry;
    EXCEPTION_RECORD* rec;
    FILE* f = NULL;
    LONG seen;
    char text[64];

    if (info == NULL || info->ExceptionRecord == NULL)
        return EXCEPTION_CONTINUE_SEARCH;
    rec = info->ExceptionRecord;
    if (!is_fatal_code(rec->ExceptionCode)) return EXCEPTION_CONTINUE_SEARCH;

    seen = InterlockedIncrement(&g_records);
    if (seen > FAULT_MAX_RECORDS) return EXCEPTION_CONTINUE_SEARCH;
    /* A fault raised while writing the record would recurse; bail instead. */
    if (InterlockedCompareExchange(&reentry, 1, 0) != 0)
        return EXCEPTION_CONTINUE_SEARCH;

    if (_wfopen_s(&f, g_log_path, L"a") == 0 && f != NULL) {
        void* frames[FAULT_STACK_DEPTH];
        USHORT captured;
        USHORT i;
        format_address(text, sizeof(text), (uintptr_t)rec->ExceptionAddress);
        fprintf(f,
            "{\"event\":\"first-chance\",\"code\":\"0x%08lX\",\"at\":%s,"
            "\"thread\":%lu,\"nth\":%ld",
            (unsigned long)rec->ExceptionCode, text,
            (unsigned long)GetCurrentThreadId(), (long)seen);
        if (rec->ExceptionCode == (DWORD)EXCEPTION_ACCESS_VIOLATION &&
            rec->NumberParameters >= 2) {
            static const char* kind[3] = { "read", "write", "execute" };
            ULONG_PTR op = rec->ExceptionInformation[0];
            format_address(text, sizeof(text),
                (uintptr_t)rec->ExceptionInformation[1]);
            fprintf(f, ",\"access\":\"%s\",\"target\":%s",
                (op <= 2u) ? kind[op] : "?", text);
        }
        if (info->ContextRecord != NULL) {
            const CONTEXT* c = info->ContextRecord;
            fprintf(f,
                ",\"regs\":{\"eax\":\"0x%08lX\",\"ecx\":\"0x%08lX\","
                "\"edx\":\"0x%08lX\",\"ebx\":\"0x%08lX\",\"esp\":\"0x%08lX\","
                "\"ebp\":\"0x%08lX\",\"esi\":\"0x%08lX\",\"edi\":\"0x%08lX\"}",
                (unsigned long)c->Eax, (unsigned long)c->Ecx,
                (unsigned long)c->Edx, (unsigned long)c->Ebx,
                (unsigned long)c->Esp, (unsigned long)c->Ebp,
                (unsigned long)c->Esi, (unsigned long)c->Edi);
        }
        captured = RtlCaptureStackBackTrace(1UL, (ULONG)FAULT_STACK_DEPTH,
            frames, NULL);
        fputs(",\"stack\":[", f);
        for (i = 0; i < captured; ++i) {
            format_address(text, sizeof(text), (uintptr_t)frames[i]);
            fprintf(f, "%s%s", (i != 0) ? "," : "", text);
        }
        fputs("]}\n", f);
        fclose(f);
    }
    InterlockedExchange(&reentry, 0);
    return EXCEPTION_CONTINUE_SEARCH;
}

void CH_CALL ch_plugin_shutdown(void)
{
    if (g_veh_handle != NULL) {
        RemoveVectoredExceptionHandler(g_veh_handle);
        g_veh_handle = NULL;
    }
}

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)
{
    FILE* f = NULL;
    g_host = host;
    if (host == NULL || host->struct_size < sizeof(*host) ||
        host->abi_version != CLIENTHACK_ABI_VERSION) {
        return CH_STATUS_UNSUPPORTED_ABI;
    }
    resolve_image_range(host->dnf_base);
    build_log_path(g_log_path, MAX_PATH, L"clienthack-faults.log");
    /* Truncate: a record from a previous run read as this run's is worse than
     * no record at all. */
    if (_wfopen_s(&f, g_log_path, L"w") == 0 && f != NULL) {
        fprintf(f,
            "{\"event\":\"session-start\",\"pid\":%lu,\"image\":\"0x%08lX\","
            "\"image_end\":\"0x%08lX\"}\n",
            (unsigned long)GetCurrentProcessId(),
            (unsigned long)g_image_base, (unsigned long)g_image_end);
        fclose(f);
    }
    g_veh_handle = AddVectoredExceptionHandler(1UL, fault_recorder);
    if (g_veh_handle == NULL) return CH_STATUS_UNSUPPORTED_ABI;
    if (host->log != NULL)
        host->log(3, "FaultRecorder", "first-chance recorder armed");
    return CH_STATUS_OK;
}
