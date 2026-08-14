#include "host_internal.h"

#include <stdio.h>
#include <string.h>
#include <wchar.h>

#ifndef GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS
#define GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS 0x00000004
#endif
#ifndef GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT
#define GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT 0x00000002
#endif

#define CH_TRACE_MAX_SECTIONS 32u

int ch_section_index_for_rva(const ch_image_section* sections,
    uint32_t count, uint32_t rva)
{
    uint32_t i;
    if (sections == NULL) return -1;
    for (i = 0; i < count; ++i) {
        uint32_t start = sections[i].va;
        uint32_t size = sections[i].size;
        /* Guard against a wrapped [start, start+size) range. */
        if (size == 0) continue;
        if (rva >= start && (rva - start) < size) return (int)i;
    }
    return -1;
}

/* Reads the mapped DNF image section table into caller storage. Returns the
 * number of sections written, or 0 on any structural mismatch. */
static uint32_t ch_read_dnf_sections(uintptr_t image_base,
    ch_image_section* out, uint32_t capacity, uint32_t* size_of_image)
{
    const IMAGE_DOS_HEADER* dos = (const IMAGE_DOS_HEADER*)image_base;
    const IMAGE_NT_HEADERS32* nt;
    const IMAGE_SECTION_HEADER* section;
    uint32_t count;
    uint32_t i;
    if (image_base == 0 || dos->e_magic != IMAGE_DOS_SIGNATURE) return 0;
    nt = (const IMAGE_NT_HEADERS32*)(image_base + (uintptr_t)dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE ||
        nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC) return 0;
    if (size_of_image != NULL)
        *size_of_image = nt->OptionalHeader.SizeOfImage;
    count = nt->FileHeader.NumberOfSections;
    if (count > capacity) count = capacity;
    section = IMAGE_FIRST_SECTION(nt);
    for (i = 0; i < count; ++i) {
        memcpy(out[i].name, section[i].Name, 8);
        out[i].name[8] = '\0';
        out[i].va = section[i].VirtualAddress;
        out[i].size = section[i].Misc.VirtualSize;
        out[i].characteristics = section[i].Characteristics;
    }
    return count;
}

static int ch_trace_env_enabled(void)
{
    wchar_t value[8];
    DWORD length = GetEnvironmentVariableW(L"CLIENTHACK_STARTUP_TRACE", value,
        sizeof(value) / sizeof(value[0]));
    return length == 1 && value[0] == L'1';
}

static int ch_trace_output_path(wchar_t path[MAX_PATH],
    const ch_startup_gate_capture* cap)
{
    wchar_t executable[MAX_PATH];
    wchar_t dumps[MAX_PATH];
    wchar_t* slash;
    SYSTEMTIME time;
    DWORD length = GetModuleFileNameW(NULL, executable, MAX_PATH);
    if (length == 0 || length >= MAX_PATH) return 0;
    slash = wcsrchr(executable, L'\\');
    if (slash == NULL) return 0;
    *slash = L'\0';
    if (_snwprintf_s(dumps, MAX_PATH, _TRUNCATE, L"%s\\dumps", executable) <= 0)
        return 0;
    if (!CreateDirectoryW(dumps, NULL) &&
        GetLastError() != ERROR_ALREADY_EXISTS) return 0;
    GetLocalTime(&time);
    return _snwprintf_s(path, MAX_PATH, _TRUNCATE,
        L"%s\\startup-stack-%04u%02u%02u-%02u%02u%02u-%03u-%lu.json",
        dumps, (unsigned)time.wYear, (unsigned)time.wMonth,
        (unsigned)time.wDay, (unsigned)time.wHour, (unsigned)time.wMinute,
        (unsigned)time.wSecond, (unsigned)time.wMilliseconds,
        (unsigned long)cap->thread_id) > 0;
}

static int ch_trace_write_text(HANDLE file, const char* text)
{
    DWORD written = 0;
    size_t length = strlen(text);
    return length <= 0xFFFFFFFFu &&
        WriteFile(file, text, (DWORD)length, &written, NULL) &&
        written == length;
}

/* Best-effort module identification for one runtime address. Fills a short
 * ASCII basename (or "" when unknown) and the module base/offset. */
static void ch_trace_describe_module(uint32_t addr, char basename[32],
    uint32_t* module_base, uint32_t* offset)
{
    HMODULE module = NULL;
    basename[0] = '\0';
    *module_base = 0;
    *offset = 0;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
            GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            (LPCWSTR)(uintptr_t)addr, &module) || module == NULL) {
        return;
    }
    *module_base = (uint32_t)(uintptr_t)module;
    *offset = addr - *module_base;
    {
        wchar_t wpath[MAX_PATH];
        DWORD length = GetModuleFileNameW(module, wpath, MAX_PATH);
        if (length > 0 && length < MAX_PATH) {
            wchar_t* slash = wcsrchr(wpath, L'\\');
            const wchar_t* name = slash != NULL ? slash + 1 : wpath;
            (void)WideCharToMultiByte(CP_UTF8, 0, name, -1, basename, 32,
                NULL, NULL);
            basename[31] = '\0';
        }
    }
}

static void ch_trace_json_escape(const char* in, char* out, size_t out_size)
{
    size_t o = 0;
    size_t i;
    for (i = 0; in[i] != '\0' && o + 2 < out_size; ++i) {
        char c = in[i];
        if (c == '"' || c == '\\') {
            out[o++] = '\\';
            out[o++] = c;
        } else if ((unsigned char)c < 0x20) {
            out[o++] = ' ';
        } else {
            out[o++] = c;
        }
    }
    out[o] = '\0';
}

int ch_startup_trace_write(void)
{
    const ch_startup_gate_capture* cap;
    ch_image_section sections[CH_TRACE_MAX_SECTIONS];
    uint32_t section_count;
    uint32_t size_of_image = 0;
    wchar_t path[MAX_PATH];
    HANDLE file;
    char line[512];
    uint32_t i;

    if (!ch_trace_env_enabled()) return 1;
    cap = ch_startup_gate_capture_get();
    if (cap == NULL) {
        ch_log(1, "StartupTrace", "no startup-gate capture available");
        return 1;
    }
    section_count = ch_read_dnf_sections(g_ch_dnf_base, sections,
        CH_TRACE_MAX_SECTIONS, &size_of_image);
    if (!ch_trace_output_path(path, cap)) {
        ch_log(1, "StartupTrace", "could not build output path");
        return 0;
    }
    file = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_NEW,
        FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) {
        ch_log(1, "StartupTrace", "could not create trace file");
        return 0;
    }

    _snprintf_s(line, sizeof(line), _TRUNCATE,
        "{\r\n"
        "  \"format\": \"clienthack-startup-stack-v1\",\r\n"
        "  \"pid\": %lu,\r\n"
        "  \"tid\": %lu,\r\n"
        "  \"dnf_base\": \"0x%08lX\",\r\n"
        "  \"size_of_image\": \"0x%08lX\",\r\n"
        "  \"eflags\": \"0x%08lX\",\r\n"
        "  \"handler_esp\": \"0x%08lX\",\r\n"
        "  \"handler_ebp\": \"0x%08lX\",\r\n"
        "  \"return_slot_va\": \"0x%08lX\",\r\n"
        "  \"client_esp_at_call\": \"0x%08lX\",\r\n"
        "  \"xcookie_return\": \"0x%08lX\",\r\n"
        "  \"client_return\": \"0x%08lX\",\r\n",
        (unsigned long)GetCurrentProcessId(),
        (unsigned long)cap->thread_id,
        (unsigned long)g_ch_dnf_base,
        (unsigned long)size_of_image,
        (unsigned long)cap->eflags,
        (unsigned long)cap->esp,
        (unsigned long)cap->ebp,
        (unsigned long)cap->return_slot_va,
        (unsigned long)(cap->return_slot_va + (uint32_t)sizeof(void*)),
        (unsigned long)cap->xcookie_return,
        (unsigned long)cap->client_return);
    if (!ch_trace_write_text(file, line)) goto fail;

    /* Client GP registers at the XCookie intercept. ebx/esi/edi/ebp survive the
     * cookie call unchanged, so they are the register state at client_return. */
    _snprintf_s(line, sizeof(line), _TRUNCATE,
        "  \"regs_at_xcookie\": { \"eax\": \"0x%08lX\", \"ecx\": \"0x%08lX\", "
        "\"edx\": \"0x%08lX\", \"ebx\": \"0x%08lX\", \"esi\": \"0x%08lX\", "
        "\"edi\": \"0x%08lX\" },\r\n",
        (unsigned long)cap->eax, (unsigned long)cap->ecx,
        (unsigned long)cap->edx, (unsigned long)cap->ebx,
        (unsigned long)cap->esi, (unsigned long)cap->edi);
    if (!ch_trace_write_text(file, line)) goto fail;

    if (!ch_trace_write_text(file, "  \"sections\": [\r\n")) goto fail;
    for (i = 0; i < section_count; ++i) {
        char name[16];
        ch_trace_json_escape(sections[i].name, name, sizeof(name));
        _snprintf_s(line, sizeof(line), _TRUNCATE,
            "    { \"name\": \"%s\", \"rva\": \"0x%08lX\", "
            "\"size\": \"0x%08lX\", \"characteristics\": \"0x%08lX\" }%s\r\n",
            name, (unsigned long)sections[i].va,
            (unsigned long)sections[i].size,
            (unsigned long)sections[i].characteristics,
            (i + 1 < section_count) ? "," : "");
        if (!ch_trace_write_text(file, line)) goto fail;
    }
    if (!ch_trace_write_text(file, "  ],\r\n")) goto fail;

    if (!ch_trace_write_text(file, "  \"stack\": [\r\n")) goto fail;
    for (i = 0; i < cap->stack_count; ++i) {
        uint32_t value = cap->stack[i];
        uint32_t slot_va = cap->return_slot_va + i * (uint32_t)sizeof(uint32_t);
        char basename[32];
        char basename_escaped[64];
        uint32_t module_base = 0;
        uint32_t offset = 0;
        int in_dnf;
        uint32_t rva = 0;
        int section_index = -1;

        ch_trace_describe_module(value, basename, &module_base, &offset);
        ch_trace_json_escape(basename, basename_escaped, sizeof(basename_escaped));
        in_dnf = (size_of_image != 0 && value >= g_ch_dnf_base &&
            value < g_ch_dnf_base + size_of_image);
        if (in_dnf) {
            rva = value - (uint32_t)g_ch_dnf_base;
            section_index = ch_section_index_for_rva(sections, section_count, rva);
        }
        if (in_dnf && section_index >= 0) {
            char sec[16];
            ch_trace_json_escape(sections[section_index].name, sec, sizeof(sec));
            _snprintf_s(line, sizeof(line), _TRUNCATE,
                "    { \"i\": %lu, \"slot_va\": \"0x%08lX\", "
                "\"value\": \"0x%08lX\", \"module\": \"%s\", "
                "\"module_base\": \"0x%08lX\", \"offset\": \"0x%08lX\", "
                "\"in_dnf\": true, \"rva\": \"0x%08lX\", \"section\": \"%s\" }%s\r\n",
                (unsigned long)i, (unsigned long)slot_va,
                (unsigned long)value, basename_escaped,
                (unsigned long)module_base, (unsigned long)offset,
                (unsigned long)rva, sec,
                (i + 1 < cap->stack_count) ? "," : "");
        } else {
            _snprintf_s(line, sizeof(line), _TRUNCATE,
                "    { \"i\": %lu, \"slot_va\": \"0x%08lX\", "
                "\"value\": \"0x%08lX\", \"module\": \"%s\", "
                "\"module_base\": \"0x%08lX\", \"offset\": \"0x%08lX\", "
                "\"in_dnf\": %s }%s\r\n",
                (unsigned long)i, (unsigned long)slot_va,
                (unsigned long)value, basename_escaped,
                (unsigned long)module_base, (unsigned long)offset,
                in_dnf ? "true" : "false",
                (i + 1 < cap->stack_count) ? "," : "");
        }
        if (!ch_trace_write_text(file, line)) goto fail;
    }
    if (!ch_trace_write_text(file, "  ]\r\n}\r\n")) goto fail;

    if (!FlushFileBuffers(file)) goto fail;
    CloseHandle(file);
    {
        char narrow[MAX_PATH];
        if (WideCharToMultiByte(CP_UTF8, 0, path, -1, narrow, sizeof(narrow),
                NULL, NULL) > 0) {
            char message[MAX_PATH + 64];
            _snprintf_s(message, sizeof(message), _TRUNCATE,
                "startup stack trace written: %s", narrow);
            ch_log(3, "StartupTrace", message);
        }
    }
    return 1;

fail:
    CloseHandle(file);
    ch_log(1, "StartupTrace", "failed while writing trace file");
    return 0;
}
