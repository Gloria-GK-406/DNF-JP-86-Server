#include "host_internal.h"

#include "clienthack/sha256.h"

int ch_hash_file_sha256(const wchar_t* path, uint8_t digest[32])
{
    HANDLE file;
    uint8_t buffer[64 * 1024];
    DWORD amount;
    ch_sha256_context context;

    if (path == NULL || digest == NULL) return 0;
    file = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN, NULL);
    if (file == INVALID_HANDLE_VALUE) return 0;

    ch_sha256_init(&context);
    for (;;) {
        if (!ReadFile(file, buffer, sizeof(buffer), &amount, NULL)) {
            CloseHandle(file);
            return 0;
        }
        if (amount == 0) break;
        ch_sha256_update(&context, buffer, amount);
    }
    CloseHandle(file);
    ch_sha256_final(&context, digest);
    return 1;
}

int ch_get_module_directory(wchar_t path[MAX_PATH])
{
    DWORD length;
    wchar_t* slash;
    if (path == NULL || g_ch_module == NULL) return 0;
    length = GetModuleFileNameW(g_ch_module, path, MAX_PATH);
    if (length == 0 || length >= MAX_PATH) return 0;
    slash = wcsrchr(path, L'\\');
    if (slash == NULL) return 0;
    *slash = L'\0';
    return 1;
}
