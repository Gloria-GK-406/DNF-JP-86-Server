#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <psapi.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>

static int executable(DWORD protect)
{
    DWORD base = protect & 0xffu;
    return base == PAGE_EXECUTE || base == PAGE_EXECUTE_READ ||
        base == PAGE_EXECUTE_READWRITE || base == PAGE_EXECUTE_WRITECOPY;
}

static void report(HANDLE process, uintptr_t address, const wchar_t* kind,
    const MEMORY_BASIC_INFORMATION* mbi)
{
    unsigned char bytes[64];
    SIZE_T read = 0;
    wchar_t mapped[MAX_PATH];
    uintptr_t start = address >= 24 ? address - 24 : 0;
    size_t i;
    mapped[0] = L'\0';
    GetMappedFileNameW(process, mbi->AllocationBase, mapped, MAX_PATH);
    ReadProcessMemory(process, (LPCVOID)start, bytes, sizeof(bytes), &read);
    wprintf(L"kind=%ls hit=0x%016llX alloc=0x%016llX type=0x%lX protect=0x%lX mapped=%ls bytes=",
        kind, (unsigned long long)address,
        (unsigned long long)(uintptr_t)mbi->AllocationBase,
        mbi->Type, mbi->Protect, mapped[0] ? mapped : L"<private>");
    for (i = 0; i < read; ++i) wprintf(L"%02X", bytes[i]);
    wprintf(L"\n");
}

int wmain(int argc, wchar_t** argv)
{
    HANDLE process;
    SYSTEM_INFO info;
    uintptr_t cursor;
    uintptr_t maximum;
    unsigned long pid;
    unsigned long hits = 0;
    const SIZE_T chunk_size = 1024u * 1024u;
    unsigned char* buffer;
    if (argc != 2) {
        fwprintf(stderr, L"usage: heavens_gate_scan.exe <pid>\n");
        return 2;
    }
    pid = wcstoul(argv[1], NULL, 10);
    process = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ,
        FALSE, (DWORD)pid);
    if (process == NULL) return 3;
    buffer = (unsigned char*)malloc(chunk_size + 64u);
    if (buffer == NULL) return 4;
    GetNativeSystemInfo(&info);
    cursor = (uintptr_t)info.lpMinimumApplicationAddress;
    maximum = (uintptr_t)info.lpMaximumApplicationAddress;
    while (cursor < maximum) {
        MEMORY_BASIC_INFORMATION mbi;
        uintptr_t begin;
        uintptr_t end;
        uintptr_t offset;
        if (VirtualQueryEx(process, (LPCVOID)cursor, &mbi, sizeof(mbi)) == 0)
            break;
        begin = (uintptr_t)mbi.BaseAddress;
        end = begin + mbi.RegionSize;
        if (end <= cursor) break;
        if (mbi.State == MEM_COMMIT && executable(mbi.Protect) &&
            (mbi.Protect & (PAGE_GUARD | PAGE_NOACCESS)) == 0) {
            for (offset = begin; offset < end;) {
                SIZE_T wanted = (SIZE_T)(end - offset);
                SIZE_T read = 0;
                size_t i;
                if (wanted > chunk_size) wanted = chunk_size;
                if (ReadProcessMemory(process, (LPCVOID)offset, buffer,
                    wanted, &read)) {
                    for (i = 0; i + 7 <= read; ++i) {
                        if ((buffer[i] == 0xea || buffer[i] == 0x9a) &&
                            buffer[i + 5] == 0x33 && buffer[i + 6] == 0) {
                            report(process, offset + i,
                                buffer[i] == 0xea ? L"far-jmp-33" :
                                    L"far-call-33", &mbi);
                            ++hits;
                        }
                        if ((buffer[i] == 0x6a && buffer[i + 1] == 0x33) ||
                            (i + 5 <= read && buffer[i] == 0x68 &&
                             buffer[i + 1] == 0x33 && buffer[i + 2] == 0 &&
                             buffer[i + 3] == 0 && buffer[i + 4] == 0)) {
                            size_t j;
                            size_t limit = i + 40u < read ? i + 40u : read;
                            for (j = i + 2u; j < limit; ++j) {
                                if (buffer[j] == 0xcb ||
                                    (buffer[j] == 0xca && j + 2u < read)) {
                                    report(process, offset + i,
                                        L"push-33-retf", &mbi);
                                    ++hits;
                                    break;
                                }
                            }
                        }
                    }
                }
                offset += wanted;
            }
        }
        cursor = end;
    }
    wprintf(L"total=%lu\n", hits);
    free(buffer);
    CloseHandle(process);
    return 0;
}
