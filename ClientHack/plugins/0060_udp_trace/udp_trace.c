/* Optional S4A21 network diagnostic.
 *
 * Records the process' UDP send/receive endpoints and TCP connect attempts
 * without changing their arguments or results.  It deliberately hooks the
 * Winsock exports rather than the game's cipher/socket staging functions: the
 * required 0050_A21RuntimeFix plugin already owns those game routines.
 */
#include "clienthack/plugin_api.h"

#include <winsock2.h>
#include <windows.h>

#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <wchar.h>

#define UDP_TRACE_HEX_BYTES 48

typedef int (WSAAPI *sendto_fn)(
    SOCKET socket_value,
    const char* buffer,
    int length,
    int flags,
    const struct sockaddr* destination,
    int destination_length);
typedef int (WSAAPI *recvfrom_fn)(
    SOCKET socket_value,
    char* buffer,
    int length,
    int flags,
    struct sockaddr* source,
    int* source_length);
typedef int (WSAAPI *connect_fn)(
    SOCKET socket_value,
    const struct sockaddr* destination,
    int destination_length);

static const ch_host_api* g_host;
static sendto_fn g_original_sendto;
static recvfrom_fn g_original_recvfrom;
static connect_fn g_original_connect;
static wchar_t g_log_path[MAX_PATH];
static HANDLE g_log = INVALID_HANDLE_VALUE;
static SRWLOCK g_log_lock = SRWLOCK_INIT;

static void build_log_path(wchar_t* output, size_t capacity)
{
    DWORD length = GetModuleFileNameW(NULL, output, (DWORD)capacity);
    wchar_t* separator;
    if (length == 0 || length >= capacity) {
        wcscpy_s(output, capacity, L"clienthack-udp-trace.log");
        return;
    }
    separator = wcsrchr(output, L'\\');
    if (separator == NULL) {
        wcscpy_s(output, capacity, L"clienthack-udp-trace.log");
        return;
    }
    separator[1] = L'\0';
    wcscat_s(output, capacity, L"clienthack-udp-trace.log");
}

static void write_line(const char* line)
{
    DWORD written;
    size_t length;
    if (line == NULL || g_log == INVALID_HANDLE_VALUE) return;
    length = strlen(line);
    AcquireSRWLockExclusive(&g_log_lock);
    WriteFile(g_log, line, (DWORD)length, &written, NULL);
    ReleaseSRWLockExclusive(&g_log_lock);
}

static void format_endpoint(
    const struct sockaddr* address,
    int address_length,
    char* output,
    size_t capacity)
{
    const struct sockaddr_in* ipv4;
    const unsigned char* bytes;
    if (address == NULL || address_length < (int)sizeof(*ipv4)
        || address->sa_family != AF_INET) {
        strcpy_s(output, capacity, "unknown");
        return;
    }
    ipv4 = (const struct sockaddr_in*)address;
    bytes = (const unsigned char*)&ipv4->sin_addr.s_addr;
    _snprintf_s(output, capacity, _TRUNCATE, "%u.%u.%u.%u:%u",
        (unsigned)bytes[0], (unsigned)bytes[1],
        (unsigned)bytes[2], (unsigned)bytes[3],
        (unsigned)ntohs(ipv4->sin_port));
}

static void format_local_endpoint(SOCKET socket_value, char* output, size_t capacity)
{
    struct sockaddr_storage storage;
    int length = (int)sizeof(storage);
    memset(&storage, 0, sizeof(storage));
    if (getsockname(socket_value, (struct sockaddr*)&storage, &length) == SOCKET_ERROR) {
        strcpy_s(output, capacity, "unknown");
        return;
    }
    format_endpoint((const struct sockaddr*)&storage, length, output, capacity);
}

static void format_hex(const char* buffer, int length, char* output, size_t capacity)
{
    static const char digits[] = "0123456789ABCDEF";
    int count;
    int index;
    if (buffer == NULL || length <= 0 || capacity == 0) {
        if (capacity != 0) output[0] = '\0';
        return;
    }
    count = length < UDP_TRACE_HEX_BYTES ? length : UDP_TRACE_HEX_BYTES;
    if (capacity < (size_t)(count * 2 + 1)) count = (int)((capacity - 1) / 2);
    for (index = 0; index < count; ++index) {
        unsigned char value = (unsigned char)buffer[index];
        output[index * 2] = digits[value >> 4];
        output[index * 2 + 1] = digits[value & 0x0F];
    }
    output[count * 2] = '\0';
}

static void record_network_event(
    const char* operation,
    SOCKET socket_value,
    const struct sockaddr* peer,
    int peer_length,
    const char* buffer,
    int requested_length,
    int result,
    int error)
{
    char local[64];
    char remote[64];
    char hex[(UDP_TRACE_HEX_BYTES * 2) + 1];
    char line[512];
    format_local_endpoint(socket_value, local, sizeof(local));
    format_endpoint(peer, peer_length, remote, sizeof(remote));
    format_hex(buffer, result >= 0 ? result : requested_length, hex, sizeof(hex));
    _snprintf_s(line, sizeof(line), _TRUNCATE,
        "{\"tick\":%llu,\"thread\":%lu,\"op\":\"%s\","
        "\"socket\":%llu,\"local\":\"%s\",\"peer\":\"%s\","
        "\"requested\":%d,\"result\":%d,\"error\":%d,\"hex\":\"%s\"}\r\n",
        (unsigned long long)GetTickCount64(),
        (unsigned long)GetCurrentThreadId(), operation,
        (unsigned long long)(uintptr_t)socket_value,
        local, remote, requested_length, result, error, hex);
    write_line(line);
}

static int WSAAPI trace_sendto(
    SOCKET socket_value,
    const char* buffer,
    int length,
    int flags,
    const struct sockaddr* destination,
    int destination_length)
{
    int result = g_original_sendto(
        socket_value, buffer, length, flags, destination, destination_length);
    int error = result == SOCKET_ERROR ? WSAGetLastError() : 0;
    record_network_event("sendto", socket_value, destination, destination_length,
        buffer, length, result, error);
    if (result == SOCKET_ERROR) WSASetLastError(error);
    return result;
}

static int WSAAPI trace_recvfrom(
    SOCKET socket_value,
    char* buffer,
    int length,
    int flags,
    struct sockaddr* source,
    int* source_length)
{
    int result = g_original_recvfrom(
        socket_value, buffer, length, flags, source, source_length);
    int error = result == SOCKET_ERROR ? WSAGetLastError() : 0;
    /* Non-blocking polling failures are deliberately omitted. */
    if (result >= 0) {
        record_network_event("recvfrom", socket_value, source,
            source_length != NULL ? *source_length : 0,
            buffer, length, result, 0);
    }
    if (result == SOCKET_ERROR) WSASetLastError(error);
    return result;
}

static int WSAAPI trace_connect(
    SOCKET socket_value,
    const struct sockaddr* destination,
    int destination_length)
{
    int result = g_original_connect(socket_value, destination, destination_length);
    int error = result == SOCKET_ERROR ? WSAGetLastError() : 0;
    record_network_event("connect", socket_value, destination, destination_length,
        NULL, 0, result, error);
    if (result == SOCKET_ERROR) WSASetLastError(error);
    return result;
}

static ch_status hook_export(
    HMODULE module,
    const char* name,
    void* detour,
    void** original)
{
    FARPROC target = GetProcAddress(module, name);
    if (target == NULL) return CH_STATUS_NOT_FOUND;
#if defined(_MSC_VER)
#pragma warning(push)
#pragma warning(disable: 4055)
#endif
    {
        ch_status status = g_host->hook_create_absolute(
            (void*)target, detour, original);
#if defined(_MSC_VER)
#pragma warning(pop)
#endif
        return status;
    }
}

void CH_CALL ch_plugin_shutdown(void)
{
    AcquireSRWLockExclusive(&g_log_lock);
    if (g_log != INVALID_HANDLE_VALUE) {
        CloseHandle(g_log);
        g_log = INVALID_HANDLE_VALUE;
    }
    ReleaseSRWLockExclusive(&g_log_lock);
}

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)
{
    HMODULE winsock;
    ch_status status;
    char line[128];
    g_host = host;
    if (host == NULL || host->struct_size < sizeof(*host)
        || host->abi_version != CLIENTHACK_ABI_VERSION
        || host->hook_create_absolute == NULL) {
        return CH_STATUS_UNSUPPORTED_ABI;
    }
    build_log_path(g_log_path, MAX_PATH);
    g_log = CreateFileW(g_log_path, FILE_APPEND_DATA,
        FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL, NULL);
    if (g_log == INVALID_HANDLE_VALUE) return CH_STATUS_SYSTEM_ERROR;
    _snprintf_s(line, sizeof(line), _TRUNCATE,
        "{\"event\":\"session-start\",\"pid\":%lu}\r\n",
        (unsigned long)GetCurrentProcessId());
    write_line(line);

    winsock = GetModuleHandleW(L"ws2_32.dll");
    if (winsock == NULL)
        winsock = LoadLibraryExW(L"ws2_32.dll", NULL, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (winsock == NULL) return CH_STATUS_SYSTEM_ERROR;

#if defined(_MSC_VER)
#pragma warning(push)
#pragma warning(disable: 4055)
#endif
    status = hook_export(winsock, "sendto", (void*)trace_sendto,
        (void**)&g_original_sendto);
    if (status == CH_STATUS_OK) {
        status = hook_export(winsock, "recvfrom", (void*)trace_recvfrom,
            (void**)&g_original_recvfrom);
    }
    if (status == CH_STATUS_OK) {
        status = hook_export(winsock, "connect", (void*)trace_connect,
            (void**)&g_original_connect);
    }
#if defined(_MSC_VER)
#pragma warning(pop)
#endif
    if (status == CH_STATUS_OK && host->log != NULL)
        host->log(3, "UdpTrace", "Winsock sendto/recvfrom/connect tracing armed");
    return status;
}
