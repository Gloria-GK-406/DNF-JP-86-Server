#include "clienthack/plugin_api.h"
#include "multi_instance_logic.h"

#include <windows.h>

#include <stdint.h>
#include <wchar.h>

typedef HANDLE (WINAPI *create_mutex_w_fn)(LPSECURITY_ATTRIBUTES, BOOL, LPCWSTR);
typedef HWND (WINAPI *find_window_w_fn)(LPCWSTR, LPCWSTR);
typedef HWND (WINAPI *find_window_a_fn)(LPCSTR, LPCSTR);

static create_mutex_w_fn g_original_create_mutex_w;
static find_window_w_fn g_original_find_window_w;
static find_window_a_fn g_original_find_window_a;
static void (CH_CALL *g_log)(uint32_t level, const char* component, const char* message);
static LONG g_logged_mutex_rename;

static int configuration_path(wchar_t path[MAX_PATH])
{
    HMODULE az = GetModuleHandleW(L"Az.dll");
    wchar_t* separator;
    DWORD length;
    if (az == NULL) return 0;
    length = GetModuleFileNameW(az, path, MAX_PATH);
    if (length == 0 || length >= MAX_PATH) return 0;
    separator = wcsrchr(path, L'\\');
    if (separator == NULL) return 0;
    return _snwprintf_s(
        separator + 1,
        MAX_PATH - (size_t)(separator + 1 - path),
        _TRUNCATE,
        L"ClientHack.ini") > 0;
}

static int is_foreign_dnf_window(HWND window)
{
    DWORD process_id = 0;
    HANDLE process;
    wchar_t path[MAX_PATH];
    wchar_t current_path[MAX_PATH];
    DWORD path_length = MAX_PATH;
    DWORD current_path_length;
    BOOL resolved;
    if (window == NULL) return 0;
    GetWindowThreadProcessId(window, &process_id);
    if (process_id == 0 || process_id == GetCurrentProcessId()) return 0;
    process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, process_id);
    if (process == NULL) return 0;
    resolved = QueryFullProcessImageNameW(process, 0, path, &path_length);
    CloseHandle(process);
    if (!resolved) return 0;
    current_path_length = GetModuleFileNameW(NULL, current_path, MAX_PATH);
    if (current_path_length == 0 || current_path_length >= MAX_PATH) return 0;
    return ch_multi_instance_has_same_executable_name(path, current_path);
}

static HANDLE WINAPI proxy_create_mutex_w(
    LPSECURITY_ATTRIBUTES attributes,
    BOOL initial_owner,
    LPCWSTR name)
{
    wchar_t unique_name[128];
    if (g_original_create_mutex_w == NULL) return NULL;
    if (!ch_multi_instance_is_target_mutex(name)) {
        return g_original_create_mutex_w(attributes, initial_owner, name);
    }
    if (!ch_multi_instance_format_mutex_name(
            name,
            (unsigned long)GetCurrentProcessId(),
            unique_name,
            sizeof(unique_name) / sizeof(unique_name[0]))) {
        return g_original_create_mutex_w(attributes, initial_owner, name);
    }
    if (g_log != NULL && InterlockedCompareExchange(&g_logged_mutex_rename, 1, 0) == 0) {
        g_log(3, "MultiInstance", "renamed the DNF single-instance mutex");
    }
    return g_original_create_mutex_w(attributes, initial_owner, unique_name);
}

static HWND WINAPI proxy_find_window_w(LPCWSTR class_name, LPCWSTR window_name)
{
    HWND window = g_original_find_window_w != NULL
        ? g_original_find_window_w(class_name, window_name)
        : NULL;
    return is_foreign_dnf_window(window) ? NULL : window;
}

static HWND WINAPI proxy_find_window_a(LPCSTR class_name, LPCSTR window_name)
{
    HWND window = g_original_find_window_a != NULL
        ? g_original_find_window_a(class_name, window_name)
        : NULL;
    return is_foreign_dnf_window(window) ? NULL : window;
}

static FARPROC resolve_export(const wchar_t* module_name, const char* export_name)
{
    HMODULE module = GetModuleHandleW(module_name);
    if (module == NULL) {
        module = LoadLibraryExW(module_name, NULL, LOAD_LIBRARY_SEARCH_SYSTEM32);
    }
    return module != NULL ? GetProcAddress(module, export_name) : NULL;
}

static ch_status install_hooks(const ch_host_api* host)
{
    FARPROC create_mutex_w = resolve_export(L"kernel32.dll", "CreateMutexW");
    FARPROC find_window_w = resolve_export(L"user32.dll", "FindWindowW");
    FARPROC find_window_a = resolve_export(L"user32.dll", "FindWindowA");
    ch_status status;
    if (create_mutex_w == NULL || find_window_w == NULL || find_window_a == NULL) {
        return CH_STATUS_NOT_FOUND;
    }
#if defined(_MSC_VER)
#pragma warning(push)
#pragma warning(disable: 4054 4055)
#endif
    g_original_create_mutex_w = (create_mutex_w_fn)(void*)create_mutex_w;
    g_original_find_window_w = (find_window_w_fn)(void*)find_window_w;
    g_original_find_window_a = (find_window_a_fn)(void*)find_window_a;
#if defined(_MSC_VER)
#pragma warning(pop)
#endif
    status = host->hook_create_absolute(
        (void*)create_mutex_w,
        (void*)proxy_create_mutex_w,
        (void**)&g_original_create_mutex_w);
    if (status != CH_STATUS_OK) return status;
    status = host->hook_create_absolute(
        (void*)find_window_w,
        (void*)proxy_find_window_w,
        (void**)&g_original_find_window_w);
    if (status != CH_STATUS_OK) return status;
    return host->hook_create_absolute(
        (void*)find_window_a,
        (void*)proxy_find_window_a,
        (void**)&g_original_find_window_a);
}

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)
{
    wchar_t path[MAX_PATH];
    int enabled;
    if (host == NULL || host->struct_size < sizeof(*host)
        || host->abi_version != CLIENTHACK_ABI_VERSION
        || host->hook_create_absolute == NULL) {
        return CH_STATUS_UNSUPPORTED_ABI;
    }
    if (!configuration_path(path)) return CH_STATUS_SYSTEM_ERROR;
    g_log = host->log;
    enabled = GetPrivateProfileIntW(L"MultiInstance", L"Enabled", 1, path) != 0;
    if (!enabled) {
        if (g_log != NULL) g_log(3, "MultiInstance", "disabled by configuration");
        return CH_STATUS_OK;
    }
    if (g_log != NULL) g_log(3, "MultiInstance", "installing DNF multi-instance hooks");
    return install_hooks(host);
}
