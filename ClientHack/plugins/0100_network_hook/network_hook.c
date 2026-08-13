#include "clienthack/plugin_api.h"
#include "clienthack/runtime_signatures.h"

#include <windows.h>
#include <stdio.h>
#include <string.h>

enum {
    STAGE_OFFSET = 0x15E218u,
    STAGE_SIZE = 0x0AF000u,
    STAGE_WPOS_OFFSET = STAGE_OFFSET + STAGE_SIZE,
    STAGE_CS_OFFSET = STAGE_OFFSET + STAGE_SIZE + 8u,
    /* The 13-byte packet header sits immediately before the cipher body.
     * totalLength (body + 13) is stored at header offset 3 as a little-endian
     * DWORD; MakePacket/TCP_SEND leave a stale value here and expect
     * Cipher::Encrypt to finalize it once the body length is known. */
    HEADER_SIZE = 13u,
    HEADER_TOTALLENGTH_OFFSET = 3u
};

static void* g_original_encrypt;
static void* g_original_decrypt;
static void* g_original_socket_write;

/* Targeted frame capture: every Game packet whose type (or cmd, as a fallback)
 * equals TARGET_PACKET_TYPE is hex-dumped to clienthack-00ab-capture.log next
 * to the game exe. This is the cheapest way to obtain a real frame sample
 * before touching a debugger -- Cipher::Encrypt/Decrypt already receive the
 * int opcode and the contiguous [13-byte header][body] memory, so no new RE is
 * needed to read the whole frame. */
enum {
    TARGET_PACKET_TYPE = 0x00ABu,
    /* How many caller frames to record. Frames 0..1 are inside this plugin
     * (RtlCaptureStackBackTrace + dump_target_frame); the cipher detour is
     * ~frame 2; the game's send/builder path starts ~frame 3, so we keep a
     * comfortable window of 16 to reach the original builder. */
    CAPTURE_STACK_FRAMES = 16,
    /* Skip our own plugin frames so the recorded RVAs are all game code. */
    CAPTURE_STACK_SKIP = 2,
    /* ISocket::Write trace: how many leading body bytes to log per call. This
     * is enough to identify which message a staged chunk belongs to (header
     * bytes for the first chunk of a frame, body prefix for later chunks). */
    WRITE_TRACE_PEEK = 8,
    /* 00AB body prefix observed in the capture (80 00 04 00 ...). Used to flag
     * the specific Write call that stages the 00AB body. */
    WRITE_00AB_PREFIX_LO = 0x00040080u
};

static const ch_host_api* g_host;

/* Capture the current call stack and write one JSON line of the form
 * {"stack":[rva0,rva1,...]} where each rva is relative to DNF.exe base, i.e.
 * directly usable as a disassembly address. Frames that fall outside the DNF
 * module are emitted as their raw absolute address (no minus-zero noise). */
static void dump_call_stack(FILE* f)
{
    void* frames[CAPTURE_STACK_FRAMES];
    USHORT n = 0;
    uintptr_t base = 0;

    if (g_host) base = g_host->dnf_base;
    n = RtlCaptureStackBackTrace((ULONG)CAPTURE_STACK_SKIP,
        (ULONG)CAPTURE_STACK_FRAMES, frames, NULL);
    fputs(",\"stack\":[", f);
    for (USHORT i = 0; i < n; ++i) {
        uintptr_t a = (uintptr_t)frames[i];
        uintptr_t rva = (base && a >= base) ? (a - base) : a;
        fprintf(f, "%s%lu", (i == 0 ? "" : ","), (unsigned long)rva);
    }
    fputs("]}", f);
}

/* Build "<game dir>\clienthack-write-trace.log". */
static void build_trace_path(wchar_t* out, size_t cap)
{
    DWORD n = GetModuleFileNameW(NULL, out, (DWORD)cap);
    wchar_t* slash = NULL;
    if (n == 0 || n >= cap) { wcscpy_s(out, cap, L"clienthack-write-trace.log"); return; }
    for (size_t i = 0; out[i]; ++i)
        if (out[i] == L'\\' || out[i] == L'/') slash = out + i;
    if (slash) slash[1] = L'\0';
    else out[0] = L'\0';
    wcscat_s(out, cap, L"clienthack-write-trace.log");
}

/* Record every ISocket::Write call: its leading bytes (to identify which frame
 * the chunk belongs to), the size, and a short call stack. This turns the
 * single send chokepoint into a map of (message -> builder). Only the first
 * chunk of each frame carries the 13-byte header, so callers that push a
 * header reveal their opcode directly. */
static void trace_socket_write(const uint8_t* buffer, int size)
{
    wchar_t path[MAX_PATH];
    FILE* f;
    int peek, hdr = 0;
    uint16_t type = 0;

    if (buffer == NULL || size <= 0) return;
    build_trace_path(path, MAX_PATH);
    if (_wfopen_s(&f, path, L"a") != 0 || f == NULL) return;

    peek = size < WRITE_TRACE_PEEK ? size : WRITE_TRACE_PEEK;
    /* If this chunk starts with cmd byte 0x00 or 0x01 followed by a plausible
     * type and length, treat it as a frame header so we can log the opcode. */
    if (size >= 7 && (buffer[0] == 0x00 || buffer[0] == 0x01)) {
        uint32_t L = (uint32_t)(buffer[3] | (buffer[4] << 8) |
            (buffer[5] << 16) | (buffer[6] << 24));
        if (L >= 13u && L <= 0x100000u) {
            hdr = 1;
            type = (uint16_t)(buffer[1] | (buffer[2] << 8));
        }
    }
    fprintf(f, "{\"size\":%d,\"hdr\":%d,\"type\":%u,\"peek\":\"",
        size, hdr, type);
    for (int i = 0; i < peek; ++i) fprintf(f, "%02X", buffer[i]);
    fputs("\"", f);
    dump_call_stack(f);
    fputs("\n", f);
    fclose(f);
}

/* Build "<game dir>\clienthack-00ab-capture.log". Falls back to the file name
 * alone (current dir) if the path does not fit. */
static void build_capture_path(wchar_t* out, size_t cap)
{
    DWORD n = GetModuleFileNameW(NULL, out, (DWORD)cap);
    wchar_t* slash = NULL;
    if (n == 0 || n >= cap) { wcscpy_s(out, cap, L"clienthack-00ab-capture.log"); return; }
    for (size_t i = 0; out[i]; ++i)
        if (out[i] == L'\\' || out[i] == L'/') slash = out + i;
    if (slash) slash[1] = L'\0';
    else out[0] = L'\0';
    wcscat_s(out, cap, L"clienthack-00ab-capture.log");
}

/* Hex-dump one captured frame as a single structured line. `frame` points at
 * the 13-byte header, `total` is header+body length. `packet_type` is the int
 * argument the cipher received (the message type), kept for cross-checking
 * against the u16 type field inside the header. */
static void dump_target_frame(const char* dir, int packet_type,
    const uint8_t* frame, int total)
{
    wchar_t path[MAX_PATH];
    FILE* f;
    uint16_t hdr_type = 0;
    uint8_t cmd = 0;
    int body_len;

    build_capture_path(path, MAX_PATH);
    if (frame == NULL || total < (int)HEADER_SIZE) return;
    cmd = frame[0];
    hdr_type = (uint16_t)(frame[1] | (frame[2] << 8));
    body_len = total - (int)HEADER_SIZE;

    if (_wfopen_s(&f, path, L"a") != 0 || f == NULL) {
        if (g_host && g_host->log)
            g_host->log(1, "NetworkHook", "00ab-capture: failed to open log");
        return;
    }
    fprintf(f, "{\"dir\":\"%s\",\"arg_type\":%d,\"cmd\":%u,\"type\":%u,"
        "\"length\":%d,\"seq\":%u,\"body_len\":%d,\"hex\":\"",
        dir, packet_type, cmd, hdr_type, total,
        (unsigned)(frame[11] | (frame[12] << 8)), body_len);
    for (int i = 0; i < total; ++i) fprintf(f, "%02X", frame[i]);
    fputs("\"", f);
    dump_call_stack(f);
    fputs("\n", f);
    fclose(f);

    if (g_host && g_host->log) {
        char msg[96];
        snprintf(msg, sizeof(msg),
            "00ab-capture: %s type=0x%04X len=%d body=%d", dir, hdr_type,
            total, body_len);
        g_host->log(3, "NetworkHook", msg);
    }
}

/* Returns nonzero if this packet should be captured. We deliberately check
 * ONLY the int `packet_type` argument that the cipher received, and never read
 * around `input`: a previous version also probed `input - HEADER_SIZE` for the
 * on-wire type field, but on the decrypt path the input does not sit behind a
 * 13-byte header (the server frame header is 15 bytes), so that probe read out
 * of bounds on every packet and corrupted behavior. `packet_type` is the
 * opcode the engine already decoded, which is the safe single source of truth. */
static int should_capture(int packet_type)
{
    return packet_type == (int)TARGET_PACKET_TYPE;
}

/* Cipher::Encrypt passthrough. The original applies a fixed-key XOR; the
 * server speaks plaintext, so the body is memcpy'd unchanged. We also
 * finalize the packet header totalLength = body + 13 unconditionally. The
 * write must not be skipped for short bodies: MakePacket leaves a stale
 * totalLength from a previous packet, and a wrong value misframes the packet
 * on the peer (the connection drops after the first short login follow-up).
 * Matches the reference S4A12 Proxy_CipherEncrypt, which has no size guard. */
static int __fastcall cipher_encrypt_passthrough(
    void* self, void* edx_value, int packet_type, const char* input,
    int input_size, char* output, int* output_size)
{
    (void)self;
    (void)edx_value;
    if (input == NULL || output == NULL || output_size == NULL || input_size < 0)
        return 0;
    if (should_capture(packet_type)) {
        dump_target_frame("send", packet_type,
            (const uint8_t*)input - HEADER_SIZE, input_size + (int)HEADER_SIZE);
    }
    *(int*)(input - HEADER_SIZE + HEADER_TOTALLENGTH_OFFSET) =
        input_size + HEADER_SIZE;
    if (input_size != 0) memcpy(output, input, (size_t)input_size);
    *output_size = input_size;
    return 1;
}

/* Cipher::Decrypt passthrough. Server sends plaintext, so decrypt is a
 * memcpy. Kept separate from encrypt so the two hook targets stay
 * independently identifiable. */
static int __fastcall cipher_decrypt_passthrough(
    void* self, void* edx_value, int packet_type, const char* input,
    int input_size, char* output, int* output_size)
{
    (void)self;
    (void)edx_value;
    if (input == NULL || output == NULL || output_size == NULL || input_size < 0)
        return 0;
    if (should_capture(packet_type)) {
        dump_target_frame("recv", packet_type,
            (const uint8_t*)input - HEADER_SIZE, input_size + (int)HEADER_SIZE);
    }
    if (input_size != 0) memcpy(output, input, (size_t)input_size);
    *output_size = input_size;
    return 1;
}

/* ISocket::Write appends to the per-socket packet staging buffer; it does not
 * touch the send ring or the socket. MakePacket writes the 13-byte header,
 * wrappers append body bytes, then TCP_SEND pops the staged packet, calls
 * Cipher::Encrypt, and hands the result to the flush ring/thread that send()s.
 * Reversed from the reference S4A12 GameGaurd implementation. */
static int __fastcall socket_write(
    void* self, void* edx_value, const char* buffer, int size)
{
    uint8_t* base = (uint8_t*)self;
    CRITICAL_SECTION* critical_section;
    DWORD* write_position;
    DWORD position;
    (void)edx_value;

    if (base == NULL || base[8] == 0) return (int)0x80000054u;
    if (buffer == NULL || size <= 0) return -1;
    trace_socket_write((const uint8_t*)buffer, size);
    critical_section = (CRITICAL_SECTION*)(base + STAGE_CS_OFFSET);
    write_position = (DWORD*)(base + STAGE_WPOS_OFFSET);
    EnterCriticalSection(critical_section);
    position = *write_position;
    if (position >= STAGE_SIZE || (DWORD)size >= STAGE_SIZE - position) {
        LeaveCriticalSection(critical_section);
        return -2;
    }
    memcpy(base + STAGE_OFFSET + position, buffer, (size_t)size);
    *write_position = position + (DWORD)size;
    LeaveCriticalSection(critical_section);
    return 0;
}

static ch_status apply_network_xor_patch(const ch_host_api* host)
{
    static const uint8_t nops[5] = { 0x90, 0x90, 0x90, 0x90, 0x90 };
    ch_patch_request request = {
        sizeof(request), 0x00D8999Bu, CH_ORIG_NETWORK_XOR_00D8999B,
        nops, sizeof(nops), "disable-fixed-key-xor"
    };
    return host->patch_apply(&request);
}

static ch_status create_hook(const ch_host_api* host, uintptr_t rva,
    const uint8_t* expected, uint32_t expected_length, void* detour,
    void** original, const char* name)
{
    ch_hook_request request = {
        sizeof(request), rva, expected, expected_length,
        detour, original, name
    };
    return host->hook_create(&request);
}

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)
{
    ch_status status;
    g_host = host;
    if (host == NULL || host->struct_size < sizeof(*host) ||
        host->abi_version != CLIENTHACK_ABI_VERSION ||
        host->patch_apply == NULL || host->hook_create == NULL) {
        return CH_STATUS_UNSUPPORTED_ABI;
    }
    status = apply_network_xor_patch(host);
    if (status != CH_STATUS_OK) return status;
#if defined(_MSC_VER)
#pragma warning(push)
#pragma warning(disable: 4055)
#endif
    status = create_hook(host, 0x01C11360u, CH_SIG_CIPHER_ENCRYPT,
        sizeof(CH_SIG_CIPHER_ENCRYPT), (void*)cipher_encrypt_passthrough,
        &g_original_encrypt, "cipher-encrypt-passthrough");
    if (status == CH_STATUS_OK) {
        status = create_hook(host, 0x01C11420u, CH_SIG_CIPHER_DECRYPT,
            sizeof(CH_SIG_CIPHER_DECRYPT), (void*)cipher_decrypt_passthrough,
            &g_original_decrypt, "cipher-decrypt-passthrough");
    }
    if (status == CH_STATUS_OK) {
        status = create_hook(host, 0x048FF9ABu, CH_SIG_SOCKET_WRITE,
            sizeof(CH_SIG_SOCKET_WRITE), (void*)socket_write,
            &g_original_socket_write, "socket-write-staging");
    }
#if defined(_MSC_VER)
#pragma warning(pop)
#endif
    return status;
}
