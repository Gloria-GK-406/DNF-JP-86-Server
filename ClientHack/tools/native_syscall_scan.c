#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <psapi.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>

static int is_executable(DWORD protect)
{
    DWORD base = protect & 0xffu;
    return base == PAGE_EXECUTE || base == PAGE_EXECUTE_READ ||
        base == PAGE_EXECUTE_READWRITE || base == PAGE_EXECUTE_WRITECOPY;
}

static void print_bytes(HANDLE process, uintptr_t address)
{
    unsigned char bytes[48];
    SIZE_T read = 0;
    uintptr_t start = address >= 24 ? address - 24 : 0;
    size_t i;
    ZeroMemory(bytes, sizeof(bytes));
    if (!ReadProcessMemory(process, (LPCVOID)start, bytes, sizeof(bytes),
        &read)) return;
    wprintf(L" bytes=");
    for (i = 0; i < read; ++i) wprintf(L"%02X", bytes[i]);
}

int wmain(int argc, wchar_t **argv)
{
    HANDLE process;
    SYSTEM_INFO info;
    uintptr_t cursor;
    uintptr_t maximum;
    unsigned long pid;
    unsigned long long hits = 0;
    const SIZE_T chunk_size = 1024u * 1024u;
    unsigned char *buffer;

    if (argc != 2) {
        fwprintf(stderr, L"usage: native_syscall_scan.exe <pid>\n");
        return 2;
    }
    pid = wcstoul(argv[1], NULL, 10);
    process = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ,
        FALSE, (DWORD)pid);
    if (process == NULL) {
        fwprintf(stderr, L"OpenProcess failed error=%lu\n", GetLastError());
        return 3;
    }
    buffer = (unsigned char *)malloc(chunk_size + 1u);
    if (buffer == NULL) {
        CloseHandle(process);
        return 4;
    }
    GetNativeSystemInfo(&info);
    cursor = (uintptr_t)info.lpMinimumApplicationAddress;
    maximum = (uintptr_t)info.lpMaximumApplicationAddress;
    while (cursor < maximum) {
        MEMORY_BASIC_INFORMATION mbi;
        SIZE_T queried = VirtualQueryEx(process, (LPCVOID)cursor, &mbi,
            sizeof(mbi));
        uintptr_t region_start;
        uintptr_t region_end;
        uintptr_t offset;
        if (queried == 0) break;
        region_start = (uintptr_t)mbi.BaseAddress;
        region_end = region_start + mbi.RegionSize;
        if (region_end <= cursor) break;
        if (mbi.State == MEM_COMMIT && is_executable(mbi.Protect) &&
            (mbi.Protect & (PAGE_GUARD | PAGE_NOACCESS)) == 0) {
            for (offset = region_start; offset < region_end;) {
                SIZE_T wanted = (SIZE_T)(region_end - offset);
                SIZE_T read = 0;
                size_t i;
                wchar_t mapped[MAX_PATH];
                if (wanted > chunk_size) wanted = chunk_size;
                if (ReadProcessMemory(process, (LPCVOID)offset, buffer,
                    wanted, &read) && read >= 2) {
                    for (i = 0; i + 1 < read; ++i) {
                        if (buffer[i] == 0x0f && buffer[i + 1] == 0x05) {
                            uintptr_t hit = offset + i;
                            mapped[0] = L'\0';
                            GetMappedFileNameW(process, mbi.AllocationBase,
                                mapped, MAX_PATH);
                            wprintf(L"hit=0x%016llX alloc=0x%016llX type=0x%lX protect=0x%lX mapped=%ls",
                                (unsigned long long)hit,
                                (unsigned long long)(uintptr_t)mbi.AllocationBase,
                                mbi.Type, mbi.Protect,
                                mapped[0] ? mapped : L"<private>");
                            print_bytes(process, hit);
                            wprintf(L"\n");
                            ++hits;
                        }
                    }
                }
                offset += wanted;
            }
        }
        cursor = region_end;
    }
    wprintf(L"total=%llu\n", hits);
    free(buffer);
    CloseHandle(process);
    return 0;
}
