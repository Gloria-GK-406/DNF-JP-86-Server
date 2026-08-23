/* PageGuardTrace -- which pages of the protector's sections does the client
 * actually touch, and who touches them?
 *
 * The clean-exe programme needs to know what sections 4-11 are for. Dropping a
 * section and seeing whether the client survives cannot answer that: it costs a
 * launch per hypothesis, it says nothing about *what* the section holds, and it
 * silently lies about anything touched only on a rare path (sections 4, 7 and 9
 * each passed the drop test once and failed it once).
 *
 * This measures it directly instead. Every page of every watched section is
 * marked PAGE_GUARD; the first access to a page raises
 * STATUS_GUARD_PAGE_VIOLATION, which a vectored handler records before
 * returning EXCEPTION_CONTINUE_EXECUTION. The kernel has already cleared the
 * guard bit by the time the exception is delivered, so the faulting instruction
 * simply re-executes and succeeds. The client's behaviour is unchanged and one
 * gameplay session yields the live page set of every protector section at once,
 * with the address of whoever touched each page first.
 *
 * One hit per page, by design. Re-arming would need a single-step dance to put
 * the guard back after the access completes, and the question here is "which
 * pages are live and who reaches them", not "how often".
 *
 * Timing: plugin initialization runs at the host's startup gate, which is after
 * the rebuilt entry stub has already written the cross-module relocation sites
 * in .,_W and .mackt. That is deliberate -- those are our own writes and would
 * otherwise be logged as client dependencies. The cost is that anything touched
 * between the stub and the gate is missed; the log records the arm time so that
 * gap stays visible.
 *
 * Known hazard, and the reason arming is deferred: an access made by the KERNEL
 * on behalf of a system call does not raise in user mode. The probe simply fails
 * with STATUS_GUARD_PAGE_VIOLATION and the call returns an error, so no vectored
 * handler ever runs and nothing is recorded.
 *
 * The first run hit exactly that. NetworkHook hooks socket-write staging at RVA
 * 0x048FF9AB, which is inside section 8, and the host validates a hook's
 * expected bytes with a kernel-side read. Arming during plugin initialization
 * made that read fail, the required plugin failed to initialize, and the host
 * aborted -- with zero records written, because the guard was never delivered as
 * an exception at all.
 *
 * ArmDelayMs therefore holds the guards back until every plugin has installed
 * its hooks and patches. The cost is that touches before the deadline are
 * missed; the arm timestamp is in the log so that window stays visible. A
 * section that still misbehaves can be dropped from Sections= without a rebuild.
 */

#include "clienthack/plugin_api.h"
#include "plugin_logic.h"

#include <stdio.h>
#include <string.h>
#include <wchar.h>
#include <windows.h>

#ifndef STATUS_GUARD_PAGE_VIOLATION
#define STATUS_GUARD_PAGE_VIOLATION ((DWORD)0x80000001u)
#endif

#define PGT_MAX_RECORDS 32768u
#define PGT_MAX_WATCHED 32u
#define PGT_STACK_WORDS 4u

typedef struct pgt_record {
    uint32_t address;                  /* the faulting address */
    uint32_t eip;                      /* the instruction that faulted */
    uint32_t access;                   /* 0 read, 1 write, 8 execute-DEP */
    uint32_t thread;
    uint32_t esp;
    uint32_t stack[PGT_STACK_WORDS];   /* [esp]..[esp+12], best effort */
    volatile LONG ready;               /* published last; see pgt_flush */
} pgt_record;

typedef struct pgt_watch {
    uint32_t index;
    uint32_t start;                    /* absolute VA */
    uint32_t end;
    char name[9];
} pgt_watch;

static const ch_host_api* g_host;
static pgt_record g_records[PGT_MAX_RECORDS];
static volatile LONG g_reserved;       /* slots handed out */
static volatile LONG g_dropped;
static uint32_t g_written;             /* slots already flushed (writer only) */
static pgt_watch g_watch[PGT_MAX_WATCHED];
static uint32_t g_watch_count;
static uint32_t g_watch_low;           /* cheap bounds test in the handler */
static uint32_t g_watch_high;
static PVOID g_veh;
static HANDLE g_flush_thread;
static volatile LONG g_stopping;
static wchar_t g_log_path[MAX_PATH];
static int g_enabled;
static uint32_t g_selected[PGT_MAX_WATCHED];
static uint32_t g_selected_count;
static unsigned long g_armed_pages;
static unsigned long g_arm_failures;
static uint32_t g_arm_delay_ms;
static uintptr_t g_base;

static unsigned long arm_range(uint32_t start, uint32_t end);
static void write_header(uintptr_t base);

static void pgt_log(uint32_t level, const char* message)
{
    if (g_host != NULL && g_host->log != NULL)
        g_host->log(level, "PageGuardTrace", message);
}

static void build_path(wchar_t* out, size_t cap, const wchar_t* file_name)
{
    wchar_t* slash = NULL;
    size_t i;
    DWORD n = GetModuleFileNameW(NULL, out, (DWORD)cap);
    if (n == 0 || n >= cap) { wcscpy_s(out, cap, file_name); return; }
    for (i = 0; out[i]; ++i)
        if (out[i] == L'\\' || out[i] == L'/') slash = out + i;
    if (slash) slash[1] = L'\0';
    else out[0] = L'\0';
    wcscat_s(out, cap, file_name);
}

/* "4,5,6,7" -> the set of section indices to guard. An empty or absent value
 * selects nothing, which disables the plugin's effect entirely: a watch list
 * that silently expanded to "everything" would be a very expensive typo. */
static uint32_t parse_sections(const wchar_t* text, uint32_t* out, uint32_t cap)
{
    uint32_t count = 0;
    const wchar_t* p = text;
    if (text == NULL) return 0;
    while (*p != L'\0' && count < cap) {
        wchar_t* end = NULL;
        unsigned long value;
        if (*p < L'0' || *p > L'9') { ++p; continue; }
        value = wcstoul(p, &end, 10);
        if (end == p) { ++p; continue; }
        p = end;
        if (value < 96u) out[count++] = (uint32_t)value;
    }
    return count;
}

static void load_config(void)
{
    wchar_t ini[MAX_PATH];
    wchar_t buffer[256];
    wchar_t name[MAX_PATH];

    build_path(ini, MAX_PATH, L"ClientHack.ini");
    g_enabled = GetPrivateProfileIntW(L"PageGuardTrace", L"Enabled", 0, ini) != 0;
    GetPrivateProfileStringW(L"PageGuardTrace", L"Sections", L"",
        buffer, (DWORD)(sizeof(buffer) / sizeof(buffer[0])), ini);
    g_selected_count = parse_sections(buffer, g_selected, PGT_MAX_WATCHED);
    g_arm_delay_ms = (uint32_t)GetPrivateProfileIntW(
        L"PageGuardTrace", L"ArmDelayMs", 8000, ini);
    if (GetPrivateProfileStringW(L"PageGuardTrace", L"LogFile",
            L"clienthack-pageguard.log", name,
            (DWORD)(sizeof(name) / sizeof(name[0])), ini) == 0) {
        wcscpy_s(name, MAX_PATH, L"clienthack-pageguard.log");
    }
    build_path(g_log_path, MAX_PATH, name);
}

static int is_selected(uint32_t index)
{
    uint32_t i;
    for (i = 0; i < g_selected_count; ++i)
        if (g_selected[i] == index) return 1;
    return 0;
}

static void append_text(const char* text, size_t length)
{
    DWORD written = 0;
    HANDLE file = CreateFileW(g_log_path, FILE_APPEND_DATA,
        FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_ALWAYS,
        FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) return;
    WriteFile(file, text, (DWORD)length, &written, NULL);
    CloseHandle(file);
}

/* The handler must not allocate, lock or do I/O: it runs on whatever thread
 * faulted, with whatever locks that thread already holds. It only fills a
 * pre-reserved slot. This thread turns those slots into a file. */
static void pgt_flush(void)
{
    char line[256];
    for (;;) {
        const pgt_record* r;
        LONG reserved = g_reserved;
        if ((LONG)g_written >= reserved) break;
        r = &g_records[g_written];
        /* Reserved but not yet filled: stop and pick it up next tick, so the
         * file never contains a half-written record. */
        if (r->ready == 0) break;
        _snprintf_s(line, sizeof(line), _TRUNCATE,
            "{\"seq\":%u,\"addr\":\"0x%08X\",\"eip\":\"0x%08X\",\"access\":%u,"
            "\"tid\":%u,\"esp\":\"0x%08X\",\"stack\":[\"0x%08X\",\"0x%08X\","
            "\"0x%08X\",\"0x%08X\"]}\n",
            g_written, r->address, r->eip, r->access, r->thread, r->esp,
            r->stack[0], r->stack[1], r->stack[2], r->stack[3]);
        append_text(line, strlen(line));
        ++g_written;
    }
}

/* Arming happens here rather than in ch_plugin_initialize so that every other
 * plugin has finished installing its hooks first -- see the header comment. */
static void pgt_arm_now(void)
{
    char message[256];
    uint32_t i;
    for (i = 0; i < g_watch_count; ++i)
        g_armed_pages += arm_range(g_watch[i].start, g_watch[i].end);
    write_header(g_base);
    _snprintf_s(message, sizeof(message), _TRUNCATE,
        "armed %lu page(s) across %u section(s), %lu protect failure(s); "
        "watching 0x%08X..0x%08X", g_armed_pages, g_watch_count,
        g_arm_failures, g_watch_low, g_watch_high);
    pgt_log(3, message);
}

static DWORD WINAPI pgt_worker(LPVOID parameter)
{
    uint32_t waited = 0;
    (void)parameter;
    while (g_stopping == 0 && waited < g_arm_delay_ms) {
        Sleep(200);
        waited += 200;
    }
    if (g_stopping != 0) return 0;
    pgt_arm_now();
    while (g_stopping == 0) {
        pgt_flush();
        Sleep(1000);
    }
    pgt_flush();
    return 0;
}

static LONG CALLBACK pgt_veh(EXCEPTION_POINTERS* info)
{
    EXCEPTION_RECORD* record;
    CONTEXT* context;
    uint32_t address;
    LONG slot;
    pgt_record* out;
    uint32_t i;

    if (info == NULL) return EXCEPTION_CONTINUE_SEARCH;
    record = info->ExceptionRecord;
    context = info->ContextRecord;
    if (record == NULL || context == NULL) return EXCEPTION_CONTINUE_SEARCH;
    if (record->ExceptionCode != STATUS_GUARD_PAGE_VIOLATION)
        return EXCEPTION_CONTINUE_SEARCH;
    if (record->NumberParameters < 2) return EXCEPTION_CONTINUE_SEARCH;

    address = (uint32_t)(uintptr_t)record->ExceptionInformation[1];
    /* Guard pages the client or the protector armed for their own purposes are
     * none of our business: leaving them to the next handler keeps this plugin
     * from changing behaviour it is only supposed to observe. */
    if (address < g_watch_low || address >= g_watch_high)
        return EXCEPTION_CONTINUE_SEARCH;

    slot = InterlockedIncrement(&g_reserved) - 1;
    if (slot < 0 || (uint32_t)slot >= PGT_MAX_RECORDS) {
        InterlockedIncrement(&g_dropped);
        return EXCEPTION_CONTINUE_EXECUTION;
    }
    out = &g_records[slot];
    out->address = address;
    out->eip = (uint32_t)context->Eip;
    out->access = (uint32_t)record->ExceptionInformation[0];
    out->thread = (uint32_t)GetCurrentThreadId();
    out->esp = (uint32_t)context->Esp;
    for (i = 0; i < PGT_STACK_WORDS; ++i) out->stack[i] = 0;
    /* An execute fault reports Eip inside the guarded page itself, so the
     * caller is only recoverable from the stack. Reading it is best effort:
     * Esp is whatever the faulting code left behind. */
    __try {
        const uint32_t* sp = (const uint32_t*)(uintptr_t)context->Esp;
        for (i = 0; i < PGT_STACK_WORDS; ++i) out->stack[i] = sp[i];
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        /* leave the zeros */
    }
    /* Published last: the flush thread treats a zero here as "not filled yet". */
    InterlockedExchange(&out->ready, 1);

    /* The kernel already turned the guard off for this page before delivering
     * the exception, so resuming re-executes the access and it succeeds. */
    return EXCEPTION_CONTINUE_EXECUTION;
}

static unsigned long arm_range(uint32_t start, uint32_t end)
{
    unsigned long pages = 0;
    uintptr_t cursor = start;
    while (cursor < end) {
        MEMORY_BASIC_INFORMATION mbi;
        SIZE_T span;
        DWORD previous = 0;
        if (VirtualQuery((LPCVOID)cursor, &mbi, sizeof(mbi)) != sizeof(mbi)) break;
        span = mbi.RegionSize - (cursor - (uintptr_t)mbi.BaseAddress);
        if (span == 0) break;
        if (cursor + span > end) span = end - cursor;
        /* Skip anything not committed, already guarded, or deliberately
         * inaccessible -- a dropped section's placeholder window is
         * PAGE_NOACCESS and adding a guard bit to it means nothing. */
        if (mbi.State == MEM_COMMIT &&
            (mbi.Protect & (PAGE_GUARD | PAGE_NOACCESS)) == 0 &&
            mbi.Protect != 0) {
            if (VirtualProtect((LPVOID)cursor, span, mbi.Protect | PAGE_GUARD,
                    &previous)) {
                pages += (unsigned long)(span / 4096u);
            } else {
                ++g_arm_failures;
            }
        }
        cursor += span;
    }
    return pages;
}

static int collect_sections(uintptr_t base)
{
    const IMAGE_DOS_HEADER* dos = (const IMAGE_DOS_HEADER*)base;
    const IMAGE_NT_HEADERS32* nt;
    const IMAGE_SECTION_HEADER* section;
    WORD count;
    WORD i;

    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return 0;
    nt = (const IMAGE_NT_HEADERS32*)(base + (uintptr_t)dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return 0;
    section = IMAGE_FIRST_SECTION(nt);
    count = nt->FileHeader.NumberOfSections;

    for (i = 0; i < count && g_watch_count < PGT_MAX_WATCHED; ++i) {
        DWORD size;
        pgt_watch* w;
        if (!is_selected((uint32_t)i)) continue;
        size = section[i].Misc.VirtualSize;
        if (size < section[i].SizeOfRawData) size = section[i].SizeOfRawData;
        if (size == 0) continue;
        w = &g_watch[g_watch_count++];
        w->index = (uint32_t)i;
        w->start = (uint32_t)(base + section[i].VirtualAddress);
        w->end = w->start + size;
        memcpy(w->name, section[i].Name, 8);
        w->name[8] = '\0';
    }
    return g_watch_count > 0;
}

static void write_header(uintptr_t base)
{
    char line[512];
    SYSTEMTIME t;
    uint32_t i;
    GetLocalTime(&t);
    _snprintf_s(line, sizeof(line), _TRUNCATE,
        "{\"event\":\"arm\",\"base\":\"0x%08X\",\"time\":\"%04u-%02u-%02u "
        "%02u:%02u:%02u\",\"pages\":%lu,\"failures\":%lu,\"sections\":[",
        (uint32_t)base, t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute,
        t.wSecond, g_armed_pages, g_arm_failures);
    append_text(line, strlen(line));
    for (i = 0; i < g_watch_count; ++i) {
        _snprintf_s(line, sizeof(line), _TRUNCATE,
            "%s{\"index\":%u,\"name\":\"%s\",\"start\":\"0x%08X\",\"end\":\"0x%08X\"}",
            (i == 0) ? "" : ",", g_watch[i].index, g_watch[i].name,
            g_watch[i].start, g_watch[i].end);
        append_text(line, strlen(line));
    }
    append_text("]}\n", 3);
}

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)
{
    uintptr_t base;
    uint32_t i;
    char message[256];

    if (host == NULL || host->struct_size < sizeof(ch_host_api))
        return CH_STATUS_INVALID_ARGUMENT;
    g_host = host;
    load_config();

    if (!g_enabled) {
        pgt_log(3, "disabled ([PageGuardTrace] Enabled=0)");
        return CH_STATUS_OK;
    }
    if (g_selected_count == 0) {
        pgt_log(2, "enabled but [PageGuardTrace] Sections is empty: nothing armed");
        return CH_STATUS_OK;
    }

    base = host->dnf_base;
    if (base == 0 || !collect_sections(base)) {
        pgt_log(1, "no watched section resolved; not arming");
        return CH_STATUS_OK;
    }

    g_watch_low = g_watch[0].start;
    g_watch_high = g_watch[0].end;
    for (i = 1; i < g_watch_count; ++i) {
        if (g_watch[i].start < g_watch_low) g_watch_low = g_watch[i].start;
        if (g_watch[i].end > g_watch_high) g_watch_high = g_watch[i].end;
    }

    /* First in the chain: a guard fault must be recorded before any other
     * handler can swallow it. Everything not ours returns CONTINUE_SEARCH. */
    g_veh = AddVectoredExceptionHandler(1, pgt_veh);
    if (g_veh == NULL) {
        pgt_log(1, "AddVectoredExceptionHandler failed; not arming");
        return CH_STATUS_OK;
    }

    g_base = base;
    g_flush_thread = CreateThread(NULL, 0, pgt_worker, NULL, 0, NULL);
    if (g_flush_thread == NULL) {
        RemoveVectoredExceptionHandler(g_veh);
        g_veh = NULL;
        pgt_log(1, "worker thread creation failed; not arming");
        return CH_STATUS_OK;
    }

    _snprintf_s(message, sizeof(message), _TRUNCATE,
        "%u page-aligned range(s) resolved over 0x%08X..0x%08X; arming in %u ms",
        g_watch_count, g_watch_low, g_watch_high, g_arm_delay_ms);
    pgt_log(3, message);
    return CH_STATUS_OK;
}

void CH_CALL ch_plugin_shutdown(void)
{
    char message[160];
    InterlockedExchange(&g_stopping, 1);
    if (g_flush_thread != NULL) {
        WaitForSingleObject(g_flush_thread, 3000);
        CloseHandle(g_flush_thread);
        g_flush_thread = NULL;
    }
    pgt_flush();
    if (g_veh != NULL) {
        RemoveVectoredExceptionHandler(g_veh);
        g_veh = NULL;
    }
    if (g_enabled && g_watch_count > 0) {
        _snprintf_s(message, sizeof(message), _TRUNCATE,
            "recorded %ld touched page(s), %ld dropped",
            g_reserved, g_dropped);
        pgt_log(3, message);
    }
}
