#include "clienthack/plugin_api.h"
#include "iat_table.h"

#include <stdio.h>
#include <string.h>
#include <wchar.h>
#include <windows.h>

#define DNF_PREFERRED_BASE 0x00400000u
#define DNF_SIZE_OF_IMAGE 0x06693000u
#define DNF_ENTRYPOINT_RVA 0x06176731u
#define DNF_TIMESTAMP 0x5601674Eu
#define DNF_SECTION_COUNT 15u
#define DNF_XCOOKIE_RVA 0x0213BBBFu
#define DNF_PROTECTED_CALL_RVA 0x04ECD542u
#define DUMP_BLOCK_SIZE (64u * 1024u)
#define DUMP_PAGE_SIZE 4096u

_Static_assert(CH_IAT_ENTRY_COUNT == 406u,
    "MemoryDump requires the reviewed 406-entry IAT table");

typedef struct dump_statistics {
    unsigned __int64 bytes_written;
    unsigned long failed_blocks;
    unsigned long skipped_regions;
    unsigned long private_execute_regions;
    unsigned long iat_matches;
    unsigned long iat_expected_unavailable;
    unsigned long iat_read_failures;
} dump_statistics;

static const ch_host_api* g_host;

static void dump_log(uint32_t level, const char* message)
{
    if (g_host != NULL && g_host->log != NULL)
        g_host->log(level, "MemoryDump", message);
}

static int create_directory_if_missing(const wchar_t* path)
{
    if (CreateDirectoryW(path, NULL)) return 1;
    return GetLastError() == ERROR_ALREADY_EXISTS;
}

static int join_path(wchar_t output[MAX_PATH], const wchar_t* left,
    const wchar_t* right)
{
    return _snwprintf_s(output, MAX_PATH, _TRUNCATE,
        L"%s\\%s", left, right) > 0;
}

static int get_output_directory(wchar_t output[MAX_PATH])
{
    wchar_t executable[MAX_PATH];
    wchar_t dump_root[MAX_PATH];
    wchar_t name[96];
    wchar_t* slash;
    SYSTEMTIME time;
    DWORD length = GetModuleFileNameW(NULL, executable, MAX_PATH);
    if (length == 0 || length >= MAX_PATH) return 0;
    slash = wcsrchr(executable, L'\\');
    if (slash == NULL) return 0;
    *slash = L'\0';
    if (!join_path(dump_root, executable, L"dumps") ||
        !create_directory_if_missing(dump_root)) return 0;
    GetLocalTime(&time);
    if (_snwprintf_s(name, sizeof(name) / sizeof(name[0]), _TRUNCATE,
            L"dnf-dump-%04u%02u%02u-%02u%02u%02u-%03u-%lu",
            (unsigned)time.wYear, (unsigned)time.wMonth,
            (unsigned)time.wDay, (unsigned)time.wHour,
            (unsigned)time.wMinute, (unsigned)time.wSecond,
            (unsigned)time.wMilliseconds,
            (unsigned long)GetCurrentProcessId()) <= 0) return 0;
    if (!join_path(output, dump_root, name)) return 0;
    return CreateDirectoryW(output, NULL) != 0;
}

static HANDLE create_output_file(const wchar_t* directory,
    const wchar_t* name)
{
    wchar_t path[MAX_PATH];
    if (!join_path(path, directory, name)) return INVALID_HANDLE_VALUE;
    return CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ, NULL,
        CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
}

static int write_bytes(HANDLE file, const void* data, DWORD length)
{
    DWORD written = 0;
    return WriteFile(file, data, length, &written, NULL) && written == length;
}

static int write_text(HANDLE file, const char* text)
{
    size_t length = strlen(text);
    return length <= 0xFFFFFFFFu &&
        write_bytes(file, text, (DWORD)length);
}

static int write_at(HANDLE file, unsigned long offset,
    const void* data, DWORD length)
{
    LARGE_INTEGER position;
    position.QuadPart = offset;
    return SetFilePointerEx(file, position, NULL, FILE_BEGIN) &&
        write_bytes(file, data, length);
}

static int validate_image(uintptr_t image_base)
{
    const IMAGE_DOS_HEADER* dos = (const IMAGE_DOS_HEADER*)image_base;
    const IMAGE_NT_HEADERS32* nt;
    const uint8_t xcookie[5] = { 0x8B, 0xFF, 0x55, 0x8B, 0xEC };
    const uint8_t protected_call[2] = { 0xFF, 0xD0 };
    if (image_base != DNF_PREFERRED_BASE || dos->e_magic != IMAGE_DOS_SIGNATURE)
        return 0;
    nt = (const IMAGE_NT_HEADERS32*)(image_base + (uintptr_t)dos->e_lfanew);
    return nt->Signature == IMAGE_NT_SIGNATURE &&
        nt->FileHeader.Machine == IMAGE_FILE_MACHINE_I386 &&
        nt->FileHeader.NumberOfSections == DNF_SECTION_COUNT &&
        nt->FileHeader.TimeDateStamp == DNF_TIMESTAMP &&
        nt->OptionalHeader.Magic == IMAGE_NT_OPTIONAL_HDR32_MAGIC &&
        nt->OptionalHeader.ImageBase == DNF_PREFERRED_BASE &&
        nt->OptionalHeader.SizeOfImage == DNF_SIZE_OF_IMAGE &&
        nt->OptionalHeader.AddressOfEntryPoint == DNF_ENTRYPOINT_RVA &&
        memcmp((const void*)(image_base + DNF_XCOOKIE_RVA),
            xcookie, sizeof(xcookie)) == 0 &&
        memcmp((const void*)(image_base + DNF_PROTECTED_CALL_RVA),
            protected_call, sizeof(protected_call)) == 0;
}

static int is_readable_region(const MEMORY_BASIC_INFORMATION* info)
{
    return info->State == MEM_COMMIT &&
        (info->Protect & (PAGE_NOACCESS | PAGE_GUARD)) == 0;
}

static int dump_memory_image(const wchar_t* directory, uintptr_t image_base,
    dump_statistics* statistics)
{
    HANDLE memory_file = INVALID_HANDLE_VALUE;
    HANDLE pages_file = INVALID_HANDLE_VALUE;
    HANDLE process = GetCurrentProcess();
    uint8_t* buffer = NULL;
    uintptr_t cursor = image_base;
    const uintptr_t image_end = image_base + DNF_SIZE_OF_IMAGE;
    LARGE_INTEGER final_size;
    int success = 0;

    memory_file = create_output_file(directory, L"DNF-memory-rva.bin");
    pages_file = create_output_file(directory, L"DNF-pages.csv");
    if (memory_file == INVALID_HANDLE_VALUE || pages_file == INVALID_HANDLE_VALUE)
        goto cleanup;
    final_size.QuadPart = DNF_SIZE_OF_IMAGE;
    if (!SetFilePointerEx(memory_file, final_size, NULL, FILE_BEGIN) ||
        !SetEndOfFile(memory_file) ||
        !write_text(pages_file,
            "start_va,end_va,rva,size,state,type,protect,bytes_written,status\r\n"))
        goto cleanup;
    buffer = (uint8_t*)HeapAlloc(GetProcessHeap(), 0, DUMP_BLOCK_SIZE);
    if (buffer == NULL) goto cleanup;

    while (cursor < image_end) {
        MEMORY_BASIC_INFORMATION info;
        uintptr_t region_start;
        uintptr_t region_end;
        unsigned __int64 region_written = 0;
        const char* status = "skipped";
        char line[256];
        if (VirtualQuery((const void*)cursor, &info, sizeof(info)) != sizeof(info)) {
            region_start = cursor;
            region_end = cursor + DUMP_PAGE_SIZE;
            if (region_end > image_end) region_end = image_end;
            ++statistics->failed_blocks;
            status = "query_failed";
            ZeroMemory(&info, sizeof(info));
        } else {
            region_start = (uintptr_t)info.BaseAddress;
            if (region_start < cursor) region_start = cursor;
            region_end = (uintptr_t)info.BaseAddress + info.RegionSize;
            if (region_end > image_end) region_end = image_end;
            if (region_end <= region_start) goto cleanup;
            if (is_readable_region(&info)) {
                uintptr_t position = region_start;
                status = "ok";
                while (position < region_end) {
                    SIZE_T requested = region_end - position;
                    SIZE_T read = 0;
                    if (requested > DUMP_BLOCK_SIZE) requested = DUMP_BLOCK_SIZE;
                    if (ReadProcessMemory(process, (const void*)position,
                            buffer, requested, &read) && read == requested) {
                        if (!write_at(memory_file,
                                (unsigned long)(position - image_base),
                                buffer, (DWORD)requested)) goto cleanup;
                        region_written += requested;
                    } else {
                        SIZE_T page_offset = 0;
                        status = "partial";
                        while (page_offset < requested) {
                            SIZE_T page_size = requested - page_offset;
                            if (page_size > DUMP_PAGE_SIZE) page_size = DUMP_PAGE_SIZE;
                            read = 0;
                            if (ReadProcessMemory(process,
                                    (const void*)(position + page_offset),
                                    buffer, page_size, &read) && read == page_size) {
                                if (!write_at(memory_file,
                                        (unsigned long)(position + page_offset - image_base),
                                        buffer, (DWORD)page_size)) goto cleanup;
                                region_written += page_size;
                            } else {
                                ++statistics->failed_blocks;
                            }
                            page_offset += page_size;
                        }
                    }
                    position += requested;
                }
            } else {
                ++statistics->skipped_regions;
            }
        }
        statistics->bytes_written += region_written;
        _snprintf_s(line, sizeof(line), _TRUNCATE,
            "0x%08lX,0x%08lX,0x%08lX,%lu,0x%08lX,0x%08lX,0x%08lX,%I64u,%s\r\n",
            (unsigned long)region_start, (unsigned long)region_end,
            (unsigned long)(region_start - image_base),
            (unsigned long)(region_end - region_start),
            (unsigned long)info.State, (unsigned long)info.Type,
            (unsigned long)info.Protect, region_written, status);
        if (!write_text(pages_file, line)) goto cleanup;
        cursor = region_end;
    }
    if (!FlushFileBuffers(memory_file) || !FlushFileBuffers(pages_file))
        goto cleanup;
    success = 1;

cleanup:
    if (buffer != NULL) HeapFree(GetProcessHeap(), 0, buffer);
    if (memory_file != INVALID_HANDLE_VALUE) CloseHandle(memory_file);
    if (pages_file != INVALID_HANDLE_VALUE) CloseHandle(pages_file);
    return success;
}

static int dump_iat(const wchar_t* directory, uintptr_t image_base,
    dump_statistics* statistics)
{
    HANDLE file = create_output_file(directory, L"DNF-iat.csv");
    size_t index;
    if (file == INVALID_HANDLE_VALUE) return 0;
    if (!write_text(file,
            "slot_va,slot_rva,module,function,resolved_va,expected_va,match\r\n")) {
        CloseHandle(file);
        return 0;
    }
    for (index = 0; index < CH_IAT_ENTRY_COUNT; ++index) {
        const ch_iat_entry* entry = &g_ch_iat_table[index];
        uintptr_t rva = entry->preferred_va - DNF_PREFERRED_BASE;
        DWORD resolved = 0;
        FARPROC expected = NULL;
        HMODULE module = GetModuleHandleA(entry->module_name);
        SIZE_T read = 0;
        char line[1024];
        int match = 0;
        if (!ReadProcessMemory(GetCurrentProcess(),
                (const void*)(image_base + rva), &resolved,
                sizeof(resolved), &read) || read != sizeof(resolved)) {
            ++statistics->iat_read_failures;
        }
        if (module != NULL)
            expected = GetProcAddress(module, entry->function_name);
        if (expected == NULL) ++statistics->iat_expected_unavailable;
        else if (resolved == (DWORD)(uintptr_t)expected) {
            ++statistics->iat_matches;
            match = 1;
        }
        _snprintf_s(line, sizeof(line), _TRUNCATE,
            "0x%08lX,0x%08lX,\"%s\",\"%s\",0x%08lX,0x%08lX,%s\r\n",
            (unsigned long)(image_base + rva), (unsigned long)rva,
            entry->module_name, entry->function_name,
            (unsigned long)resolved, (unsigned long)(uintptr_t)expected,
            match ? "true" : "false");
        if (!write_text(file, line)) {
            CloseHandle(file);
            return 0;
        }
    }
    if (!FlushFileBuffers(file)) {
        CloseHandle(file);
        return 0;
    }
    CloseHandle(file);
    return 1;
}

static int dump_private_execute_inventory(const wchar_t* directory,
    uintptr_t image_base, dump_statistics* statistics)
{
    HANDLE file = create_output_file(directory, L"private-exec-regions.csv");
    SYSTEM_INFO system;
    uintptr_t cursor;
    uintptr_t maximum;
    if (file == INVALID_HANDLE_VALUE) return 0;
    if (!write_text(file,
            "start_va,end_va,size,state,type,protect,allocation_base\r\n")) {
        CloseHandle(file);
        return 0;
    }
    GetSystemInfo(&system);
    cursor = (uintptr_t)system.lpMinimumApplicationAddress;
    maximum = (uintptr_t)system.lpMaximumApplicationAddress;
    while (cursor < maximum) {
        MEMORY_BASIC_INFORMATION info;
        uintptr_t end;
        if (VirtualQuery((const void*)cursor, &info, sizeof(info)) != sizeof(info))
            break;
        end = (uintptr_t)info.BaseAddress + info.RegionSize;
        if (end <= cursor) break;
        if (info.State == MEM_COMMIT && info.Type == MEM_PRIVATE &&
            (info.Protect & 0xF0u) != 0 &&
            (end <= image_base || cursor >= image_base + DNF_SIZE_OF_IMAGE)) {
            char line[256];
            _snprintf_s(line, sizeof(line), _TRUNCATE,
                "0x%08lX,0x%08lX,%lu,0x%08lX,0x%08lX,0x%08lX,0x%08lX\r\n",
                (unsigned long)(uintptr_t)info.BaseAddress,
                (unsigned long)end, (unsigned long)info.RegionSize,
                (unsigned long)info.State, (unsigned long)info.Type,
                (unsigned long)info.Protect,
                (unsigned long)(uintptr_t)info.AllocationBase);
            if (!write_text(file, line)) {
                CloseHandle(file);
                return 0;
            }
            ++statistics->private_execute_regions;
        }
        cursor = end;
    }
    if (!FlushFileBuffers(file)) {
        CloseHandle(file);
        return 0;
    }
    CloseHandle(file);
    return 1;
}

static int write_manifest(const wchar_t* directory,
    const dump_statistics* statistics)
{
    HANDLE file = create_output_file(directory, L"manifest.json");
    char json[2048];
    if (file == INVALID_HANDLE_VALUE) return 0;
    _snprintf_s(json, sizeof(json), _TRUNCATE,
        "{\r\n"
        "  \"format\": \"clienthack-memory-dump-v1\",\r\n"
        "  \"pid\": %lu,\r\n"
        "  \"image_base\": \"0x%08lX\",\r\n"
        "  \"size_of_image\": %lu,\r\n"
        "  \"entrypoint_rva\": \"0x%08lX\",\r\n"
        "  \"section_count\": %u,\r\n"
        "  \"x_cookie_rva\": \"0x%08lX\",\r\n"
        "  \"x_cookie_bytes\": \"8BFF558BEC\",\r\n"
        "  \"protected_call_rva\": \"0x%08lX\",\r\n"
        "  \"protected_call_bytes\": \"FFD0\",\r\n"
        "  \"bytes_written\": %I64u,\r\n"
        "  \"hole_bytes\": %I64u,\r\n"
        "  \"failed_blocks\": %lu,\r\n"
        "  \"skipped_regions\": %lu,\r\n"
        "  \"iat_total\": %u,\r\n"
        "  \"iat_matches\": %lu,\r\n"
        "  \"iat_expected_unavailable\": %lu,\r\n"
        "  \"iat_read_failures\": %lu,\r\n"
        "  \"private_execute_regions\": %lu,\r\n"
        "  \"post_clienthack_iat_repair\": true,\r\n"
        "  \"pre_feature_plugins\": true,\r\n"
        "  \"early_antidebug_active\": true,\r\n"
        "  \"analysis_only\": true\r\n"
        "}\r\n",
        (unsigned long)GetCurrentProcessId(),
        (unsigned long)DNF_PREFERRED_BASE,
        (unsigned long)DNF_SIZE_OF_IMAGE,
        (unsigned long)DNF_ENTRYPOINT_RVA,
        (unsigned)DNF_SECTION_COUNT,
        (unsigned long)DNF_XCOOKIE_RVA,
        (unsigned long)DNF_PROTECTED_CALL_RVA,
        statistics->bytes_written,
        (unsigned __int64)DNF_SIZE_OF_IMAGE - statistics->bytes_written,
        statistics->failed_blocks, statistics->skipped_regions,
        (unsigned)CH_IAT_ENTRY_COUNT, statistics->iat_matches,
        statistics->iat_expected_unavailable,
        statistics->iat_read_failures,
        statistics->private_execute_regions);
    if (!write_text(file, json) || !FlushFileBuffers(file)) {
        CloseHandle(file);
        return 0;
    }
    CloseHandle(file);
    return 1;
}

static int write_dump_log(const wchar_t* directory,
    const dump_statistics* statistics)
{
    HANDLE file = create_output_file(directory, L"dump.log");
    char line[512];
    if (file == INVALID_HANDLE_VALUE) return 0;
    _snprintf_s(line, sizeof(line), _TRUNCATE,
        "Memory dump complete. bytes=%I64u holes=%I64u failed_blocks=%lu "
        "iat=%u matches=%lu private_exec=%lu\r\n",
        statistics->bytes_written,
        (unsigned __int64)DNF_SIZE_OF_IMAGE - statistics->bytes_written,
        statistics->failed_blocks, (unsigned)CH_IAT_ENTRY_COUNT,
        statistics->iat_matches, statistics->private_execute_regions);
    if (!write_text(file, line) || !FlushFileBuffers(file)) {
        CloseHandle(file);
        return 0;
    }
    CloseHandle(file);
    return 1;
}

static int write_complete_marker(const wchar_t* directory)
{
    HANDLE file = create_output_file(directory, L"COMPLETE");
    int success;
    if (file == INVALID_HANDLE_VALUE) return 0;
    success = write_text(file, "ok\r\n") && FlushFileBuffers(file);
    CloseHandle(file);
    return success;
}

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)
{
    wchar_t output_directory[MAX_PATH];
    dump_statistics statistics;
    uintptr_t image_base;
    if (host == NULL || host->struct_size < sizeof(*host) ||
        host->abi_version != CLIENTHACK_ABI_VERSION || host->log == NULL)
        return CH_STATUS_UNSUPPORTED_ABI;
    g_host = host;
    image_base = host->dnf_base;
    ZeroMemory(&statistics, sizeof(statistics));
    dump_log(3, "starting analysis-only memory dump");
    if (!validate_image(image_base)) {
        dump_log(1, "DNF identity or runtime landmarks do not match");
        return CH_STATUS_INCOMPATIBLE_MEMORY;
    }
    if (!get_output_directory(output_directory) ||
        !dump_memory_image(output_directory, image_base, &statistics) ||
        !dump_iat(output_directory, image_base, &statistics) ||
        !dump_private_execute_inventory(output_directory, image_base,
            &statistics) ||
        !write_manifest(output_directory, &statistics) ||
        !write_dump_log(output_directory, &statistics) ||
        !write_complete_marker(output_directory)) {
        dump_log(1, "memory dump failed; partial output was retained");
        return CH_STATUS_SYSTEM_ERROR;
    }
    dump_log(3, "analysis-only memory dump COMPLETE marker written");
    return CH_STATUS_OK;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(instance);
    return TRUE;
}
