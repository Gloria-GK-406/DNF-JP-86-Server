#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>
#include <stdio.h>
#include <wchar.h>

typedef LONG (NTAPI *nt_suspend_process_t)(HANDLE);
typedef LONG (NTAPI *nt_resume_process_t)(HANDLE);

static const unsigned char expected_stub[32] = {
    0x4C, 0x8B, 0xD1, 0xB8, 0x0D, 0x00, 0x00, 0x00,
    0xF6, 0x04, 0x25, 0x08, 0x03, 0xFE, 0x7F, 0x01,
    0x75, 0x03, 0x0F, 0x05, 0xC3, 0xCD, 0x2E, 0xC3,
    0x0F, 0x1F, 0x84, 0x00, 0x00, 0x00, 0x00, 0x00
};

/* Return STATUS_SUCCESS only for ThreadHideFromDebugger. Every other class
 * executes the original x64 NtSetInformationThread syscall stub. */
static const unsigned char filtered_stub[32] = {
    0x83, 0xFA, 0x11,             /* cmp edx, 11h */
    0x75, 0x03,                   /* jne original */
    0x31, 0xC0,                   /* xor eax, eax */
    0xC3,                         /* ret */
    0x4C, 0x8B, 0xD1,             /* original: mov r10, rcx */
    0xB8, 0x0D, 0x00, 0x00, 0x00, /* mov eax, 0Dh */
    0xF6, 0x04, 0x25, 0x08, 0x03, 0xFE, 0x7F, 0x01,
    0x75, 0x03, 0x0F, 0x05, 0xC3, 0xCD, 0x2E, 0xC3
};

static void sibling_path(wchar_t *path, size_t count, const wchar_t *name)
{
    wchar_t *slash;
    GetModuleFileNameW(NULL, path, (DWORD)count);
    slash = wcsrchr(path, L'\\');
    if (slash != NULL) slash[1] = L'\0';
    wcscat_s(path, count, name);
}

static void log_result(const wchar_t *format, ...)
{
    wchar_t path[MAX_PATH];
    FILE *stream = NULL;
    va_list args;
    sibling_path(path, MAX_PATH, L"native_ntdll_patch.log");
    if (_wfopen_s(&stream, path, L"a, ccs=UTF-8") != 0 || stream == NULL)
        return;
    va_start(args, format);
    vfwprintf(stream, format, args);
    va_end(args);
    fputwc(L'\n', stream);
    fclose(stream);
}

static DWORD find_target(const wchar_t *expected_path)
{
    HANDLE snapshot;
    PROCESSENTRY32W entry;
    DWORD result = 0;
    snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) return 0;
    ZeroMemory(&entry, sizeof(entry));
    entry.dwSize = sizeof(entry);
    if (Process32FirstW(snapshot, &entry)) {
        do {
            HANDLE process;
            wchar_t actual[MAX_PATH];
            DWORD length = MAX_PATH;
            if (_wcsicmp(entry.szExeFile, L"DNF.exe") != 0) continue;
            process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE,
                entry.th32ProcessID);
            if (process == NULL) continue;
            if (QueryFullProcessImageNameW(process, 0, actual, &length) &&
                _wcsicmp(actual, expected_path) == 0) {
                result = entry.th32ProcessID;
                CloseHandle(process);
                break;
            }
            CloseHandle(process);
        } while (Process32NextW(snapshot, &entry));
    }
    CloseHandle(snapshot);
    return result;
}

static int patch_target(DWORD pid)
{
    HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
    void *stub = (void *)GetProcAddress(ntdll, "NtSetInformationThread");
    nt_suspend_process_t suspend_process =
        (nt_suspend_process_t)GetProcAddress(ntdll, "NtSuspendProcess");
    nt_resume_process_t resume_process =
        (nt_resume_process_t)GetProcAddress(ntdll, "NtResumeProcess");
    HANDLE process;
    unsigned char before[32];
    unsigned char after[32];
    SIZE_T transferred = 0;
    DWORD old_protection = 0;
    int suspended = 0;
    int ok = 0;

    if (stub == NULL || suspend_process == NULL || resume_process == NULL)
        return 0;
    process = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION |
        PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_SUSPEND_RESUME,
        FALSE, pid);
    if (process == NULL) return 0;
    if (suspend_process(process) < 0) goto done;
    suspended = 1;
    if (!ReadProcessMemory(process, stub, before, sizeof(before), &transferred) ||
        transferred != sizeof(before) ||
        memcmp(before, expected_stub, sizeof(before)) != 0) goto done;
    if (!VirtualProtectEx(process, stub, sizeof(filtered_stub),
        PAGE_EXECUTE_READWRITE, &old_protection)) goto done;
    if (!WriteProcessMemory(process, stub, filtered_stub,
        sizeof(filtered_stub), &transferred) ||
        transferred != sizeof(filtered_stub)) goto restore;
    FlushInstructionCache(process, stub, sizeof(filtered_stub));
    if (!ReadProcessMemory(process, stub, after, sizeof(after), &transferred) ||
        transferred != sizeof(after) ||
        memcmp(after, filtered_stub, sizeof(after)) != 0) goto restore;
    ok = 1;
restore:
    VirtualProtectEx(process, stub, sizeof(filtered_stub),
        old_protection, &old_protection);
done:
    if (suspended) resume_process(process);
    CloseHandle(process);
    return ok;
}

int wmain(void)
{
    wchar_t target_path[MAX_PATH];
    ULONGLONG deadline = GetTickCount64() + 45000;
    DWORD pid = 0;
    sibling_path(target_path, MAX_PATH, L"DNF.exe");
    do {
        pid = find_target(target_path);
        if (pid != 0) break;
        Sleep(1);
    } while (GetTickCount64() < deadline);
    if (pid == 0) {
        log_result(L"target-not-found path=%ls", target_path);
        return 2;
    }
    if (!patch_target(pid)) {
        log_result(L"patch-failed pid=%lu error=%lu", pid, GetLastError());
        return 3;
    }
    log_result(L"patch-ok pid=%lu stub=%p", pid,
        GetProcAddress(GetModuleHandleW(L"ntdll.dll"),
            "NtSetInformationThread"));
    return 0;
}
