#include "clienthack/plugin_api.h"
#include "clienthack/runtime_signatures.h"

#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>
#if defined(_MSC_VER)
#include <intrin.h>
/* MSVC has no __builtin_return_address; GCC has no _ReturnAddress. Both compile
 * this file (see CMakePresets: win32-* use MSVC, mingw32-release uses GCC). */
#define CH_RETURN_ADDRESS() _ReturnAddress()
#else
#define CH_RETURN_ADDRESS() __builtin_return_address(0)
#endif

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


/* Frame capture: every Game packet that passes the [FrameCapture] filter is
 * hex-dumped as one JSON line to clienthack-frame-capture.log next to the game
 * exe. This is the cheapest way to obtain real frame samples before touching a
 * debugger -- Cipher::Encrypt/Decrypt already receive the int opcode and the
 * plaintext body, so no new RE is needed to read the traffic.
 *
 * The two directions are NOT symmetric and must not share dump code:
 *   send  `input` points at the body of a contiguous [13-byte header][body]
 *         staging buffer, so the whole frame is dumpable (the encrypt detour
 *         already writes totalLength back into that same header).
 *   recv  `input` is a receive buffer that is NOT preceded by the frame header
 *         (the server header is 15 bytes and lives elsewhere). Reading behind
 *         `input` here corrupted client behavior in an earlier version, so the
 *         recv path dumps the body only and takes the message identity from
 *         the `packet_type` argument. */
enum {
    /* How many caller frames to record. Frames 0..1 are inside this plugin
     * (RtlCaptureStackBackTrace + the dump helper); the cipher detour is
     * ~frame 2; the game's send/builder path starts ~frame 3, so we keep a
     * comfortable window of 16 to reach the original builder. */
    CAPTURE_STACK_FRAMES = 16,
    /* Skip our own plugin frames so the recorded RVAs are all game code. */
    CAPTURE_STACK_SKIP = 2,
    /* ISocket::Write trace: how many leading body bytes to log per call. This
     * is enough to identify which message a staged chunk belongs to (header
     * bytes for the first chunk of a frame, body prefix for later chunks). */
    WRITE_TRACE_PEEK = 8,
    /* Filter capacity and hex-dump cap for the [FrameCapture] ini section. */
    CAPTURE_MAX_TYPES = 32,
    CAPTURE_DEFAULT_MAX_BODY = 4096,
    /* parse_type_list wildcard marker: "capture every type". */
    CAPTURE_TYPES_ALL = -1
};

static const ch_host_api* g_host;

/* [FrameCapture] configuration, resolved once in ch_plugin_initialize. The
 * type filters default to the wildcard so a session yields the complete
 * bidirectional transcript; a narrow list is only needed to cut noise once the
 * message of interest is known. */
static int g_capture_enabled = 1;
static uint16_t g_capture_types[CAPTURE_MAX_TYPES];
static int g_capture_type_count = CAPTURE_TYPES_ALL;
static uint16_t g_stack_types[CAPTURE_MAX_TYPES];
static int g_stack_type_count = CAPTURE_TYPES_ALL;
static int g_write_trace_enabled;
static int g_capture_max_body = CAPTURE_DEFAULT_MAX_BODY;
static wchar_t g_capture_log_path[MAX_PATH];
static wchar_t g_trace_log_path[MAX_PATH];

/* Read trace: field-level decomposition of inbound frames.
 *
 * The frame capture above answers "what bytes arrived". It cannot answer "where
 * are the field boundaries", and for S->C that is the blocking question: the
 * static walk from a handler entry dies wherever the call graph enters the
 * packed region, which is most of the notification handlers.
 *
 * Every S->C field extraction goes through exactly four functions that pull
 * from one global stream (see protocol/patterns/game-receive.md). Packed code
 * is no obstacle -- it still has to call them to obtain bytes. Hooking the four
 * therefore yields the exact field sequence of any frame at runtime: width,
 * order, value, and the call site that asked for it.
 *
 * Offsets need a frame origin, and the stream cursor is a raw pointer rather
 * than an offset. SetRemaining is called in the receive function immediately
 * before the command byte is read and dispatched, so the cursor at that moment
 * is the body origin. That model validates itself: a CMD frame must then report
 * its result envelope at offset 0, and a known layout must line up field for
 * field. If the offsets come out shifted, the origin is wrong -- not the reads.
 *
 * There are two SetRemaining call sites inside the receive function, so a frame
 * boundary is taken only when reads have accumulated; back-to-back calls just
 * move the origin. Every call is logged with its argument either way, so the
 * trace shows what actually happened rather than what this comment assumes. */
enum {
    /* VA 0x031C7E00 in the disassembly; stored as an RVA so a relocated image
     * still resolves. +0x00 is the read cursor, +0x04 the remaining count. */
    INBOUND_STREAM_RVA = 0x02DC7E00u,
    /* Records buffered per frame. The entry batch's largest body is ~30 KB of
     * quest flags, which is read in bulk rather than field by field; 512 covers
     * every frame observed so far and truncation is reported in the line. */
    READ_TRACE_MAX_RECORDS = 512,
    /* Leading bytes kept for a ReadBytes call, enough to recognise the content
     * without turning the trace into a second hex dump. */
    READ_TRACE_PEEK = 8
};

/* One primitive call. `width` is 1/2/4 for the scalar reads and the requested
 * length for ReadBytes, which is distinguished by `is_bytes`. */
typedef struct {
    int32_t offset;
    int32_t width;
    uint32_t value;
    uint32_t ret_rva;
    uint8_t peek[READ_TRACE_PEEK];
    uint8_t peek_length;
    uint8_t is_bytes;
} ch_read_record;

static int g_read_trace_enabled;
static uint16_t g_read_trace_types[CAPTURE_MAX_TYPES];
static int g_read_trace_type_count = CAPTURE_TYPES_ALL;
static wchar_t g_read_trace_log_path[MAX_PATH];

static void* g_original_read_u8;
static void* g_original_read_u16;
static void* g_original_read_u32;
static void* g_original_read_bytes;
static void* g_original_set_remaining;

/* Current frame under trace. `origin` is the cursor snapshot taken at the last
 * SetRemaining; `active` gates the read detours so an untraced frame costs one
 * comparison. */
static uintptr_t g_read_frame_origin;
static int32_t g_read_frame_remaining;
/* Snapshot of the frame's type, taken when the frame opens. The live
 * g_last_decrypt_type cannot be used at flush time: a frame is written out when
 * the NEXT one begins, and by then that next frame has already been decrypted.
 * The first trace mislabelled every line by one frame for exactly this reason
 * -- the filter was right, only the recorded name was stale. */
static long g_read_frame_type = -1;
static int g_read_frame_active;
static int g_read_frame_records;
static int g_read_frame_dropped;
static int g_read_frame_resets;
static ch_read_record g_read_frame[READ_TRACE_MAX_RECORDS];
/* Type of the most recent body handed to Cipher::Decrypt. The receive function
 * decrypts before it dispatches, so at SetRemaining time this is the frame
 * about to be parsed. It is the only identity available on this path: the read
 * primitives see nothing but the stream. */
static volatile long g_last_decrypt_type = -1;

/* Build "<game dir>\<file_name>". Falls back to the bare file name (current
 * directory) when the game directory does not fit. */
static void build_log_path(wchar_t* out, size_t cap, const wchar_t* file_name)
{
    DWORD n = GetModuleFileNameW(NULL, out, (DWORD)cap);
    wchar_t* slash = NULL;
    if (n == 0 || n >= cap) { wcscpy_s(out, cap, file_name); return; }
    for (size_t i = 0; out[i]; ++i)
        if (out[i] == L'\\' || out[i] == L'/') slash = out + i;
    if (slash) slash[1] = L'\0';
    else out[0] = L'\0';
    wcscat_s(out, cap, file_name);
}

/* Returns nonzero if `type` passes a filter list. A negative count is the
 * wildcard (every type); zero means the filter selects nothing. */
static int type_selected(const uint16_t* list, int count, uint16_t type)
{
    if (count < 0) return 1;
    for (int i = 0; i < count; ++i)
        if (list[i] == type) return 1;
    return 0;
}

/* Parse a separated list of hex message types ("00AB,1DD" or "0x00ab 0x01dd")
 * into `out`. Returns the number of entries parsed, CAPTURE_TYPES_ALL for the
 * wildcard "*"/"all", or 0 for an empty string. Unparsable tokens are skipped
 * rather than failing initialization: a malformed filter must never keep the
 * client from starting. */
static int parse_type_list(const wchar_t* text, uint16_t* out, int cap)
{
    int count = 0;
    const wchar_t* p = text;

    if (text == NULL) return 0;
    if (_wcsicmp(text, L"*") == 0 || _wcsicmp(text, L"all") == 0)
        return CAPTURE_TYPES_ALL;

    while (*p != L'\0' && count < cap) {
        unsigned long value;
        wchar_t* end = NULL;
        if (*p == L',' || *p == L';' || *p == L' ' || *p == L'\t') { ++p; continue; }
        value = wcstoul(p, &end, 16);
        if (end == p) { ++p; continue; }
        p = end;
        if (value <= 0xFFFFu) out[count++] = (uint16_t)value;
    }
    return count;
}

/* Resolve [FrameCapture] from ClientHack.ini next to the game exe. Absent keys
 * keep the defaults declared above, so a client with no [FrameCapture] section
 * records the full transcript with call stacks and no write trace. */
static void load_capture_config(void)
{
    wchar_t ini[MAX_PATH];
    wchar_t buffer[512];
    wchar_t name[MAX_PATH];
    int parsed;

    build_log_path(ini, MAX_PATH, L"ClientHack.ini");
    g_capture_enabled = GetPrivateProfileIntW(
        L"FrameCapture", L"Enabled", 1, ini) != 0;
    g_write_trace_enabled = GetPrivateProfileIntW(
        L"FrameCapture", L"WriteTrace", 0, ini) != 0;
    g_capture_max_body = (int)GetPrivateProfileIntW(
        L"FrameCapture", L"MaxBodyBytes", CAPTURE_DEFAULT_MAX_BODY, ini);
    if (g_capture_max_body < 0) g_capture_max_body = 0;

    /* An absent key yields the "*" default, so both lookups are parsed
     * unconditionally: the return value cannot distinguish "key absent" from
     * "key present but empty", and `Stack=` with an empty value must mean "no
     * stacks" rather than falling back to the wildcard. `Types=` empty stays
     * fail-open (every type), because a filter that silently records nothing
     * is indistinguishable from a broken plugin. */
    GetPrivateProfileStringW(L"FrameCapture", L"Types", L"*",
        buffer, (DWORD)(sizeof(buffer) / sizeof(buffer[0])), ini);
    parsed = parse_type_list(buffer, g_capture_types, CAPTURE_MAX_TYPES);
    g_capture_type_count = (parsed == 0) ? CAPTURE_TYPES_ALL : parsed;

    GetPrivateProfileStringW(L"FrameCapture", L"Stack", L"*",
        buffer, (DWORD)(sizeof(buffer) / sizeof(buffer[0])), ini);
    g_stack_type_count =
        parse_type_list(buffer, g_stack_types, CAPTURE_MAX_TYPES);

    if (GetPrivateProfileStringW(L"FrameCapture", L"LogFile",
            L"clienthack-frame-capture.log", name,
            (DWORD)(sizeof(name) / sizeof(name[0])), ini) == 0) {
        wcscpy_s(name, MAX_PATH, L"clienthack-frame-capture.log");
    }
    build_log_path(g_capture_log_path, MAX_PATH, name);
    build_log_path(g_trace_log_path, MAX_PATH, L"clienthack-write-trace.log");

    if (g_host && g_host->log) {
        char msg[160];
        snprintf(msg, sizeof(msg),
            "frame-capture: enabled=%d types=%d stack=%d write_trace=%d "
            "max_body=%d", g_capture_enabled, g_capture_type_count,
            g_stack_type_count, g_write_trace_enabled, g_capture_max_body);
        g_host->log(3, "NetworkHook", msg);
    }
}

/* Resolve [ReadTrace]. Disabled by default, and deliberately so: the scalar
 * reads have well over a thousand call sites between them, so the hooks are not
 * even installed unless this is on. A normal capture session therefore carries
 * none of this cost and none of its risk. */
static void load_read_trace_config(void)
{
    wchar_t ini[MAX_PATH];
    wchar_t buffer[512];
    wchar_t name[MAX_PATH];

    build_log_path(ini, MAX_PATH, L"ClientHack.ini");
    g_read_trace_enabled = GetPrivateProfileIntW(
        L"ReadTrace", L"Enabled", 0, ini) != 0;

    /* Same fail-open rule as [FrameCapture].Types: an empty filter records
     * everything rather than silently recording nothing. */
    GetPrivateProfileStringW(L"ReadTrace", L"Types", L"*",
        buffer, (DWORD)(sizeof(buffer) / sizeof(buffer[0])), ini);
    g_read_trace_type_count =
        parse_type_list(buffer, g_read_trace_types, CAPTURE_MAX_TYPES);
    if (g_read_trace_type_count == 0)
        g_read_trace_type_count = CAPTURE_TYPES_ALL;

    if (GetPrivateProfileStringW(L"ReadTrace", L"LogFile",
            L"clienthack-read-trace.log", name,
            (DWORD)(sizeof(name) / sizeof(name[0])), ini) == 0) {
        wcscpy_s(name, MAX_PATH, L"clienthack-read-trace.log");
    }
    build_log_path(g_read_trace_log_path, MAX_PATH, name);

    if (g_host && g_host->log) {
        char msg[128];
        snprintf(msg, sizeof(msg), "read-trace: enabled=%d types=%d",
            g_read_trace_enabled, g_read_trace_type_count);
        g_host->log(3, "NetworkHook", msg);
    }
}

/* Base of the inbound stream structure, or 0 before the host reports the module
 * base. Returning 0 makes every read detour fall straight through. */
static uint8_t* inbound_stream(void)
{
    if (g_host == NULL || g_host->dnf_base == 0) return NULL;
    return (uint8_t*)(g_host->dnf_base + INBOUND_STREAM_RVA);
}

static uintptr_t inbound_cursor(void)
{
    uint8_t* stream = inbound_stream();
    return stream ? *(uintptr_t*)stream : 0;
}

/* Emit the buffered frame as one JSON line and clear the buffer. Called from
 * the SetRemaining detour, so a frame is written when the next one starts; the
 * final frame of a session stays buffered, which costs one trailing frame and
 * avoids guessing where a frame ends. */
static void flush_read_frame(void)
{
    FILE* f;

    if (g_read_frame_records <= 0) { g_read_frame_dropped = 0; return; }
    if (_wfopen_s(&f, g_read_trace_log_path, L"a") == 0 && f != NULL) {
        fprintf(f, "{\"type\":%ld,\"body_len\":%ld,\"reads\":%d,"
            "\"dropped\":%d,\"resets\":%d,\"fields\":[",
            g_read_frame_type, (long)g_read_frame_remaining,
            g_read_frame_records, g_read_frame_dropped, g_read_frame_resets);
        for (int i = 0; i < g_read_frame_records; ++i) {
            const ch_read_record* r = &g_read_frame[i];
            fprintf(f, "%s{\"off\":%ld,\"w\":%ld,\"ret\":%lu",
                (i == 0 ? "" : ","), (long)r->offset, (long)r->width,
                (unsigned long)r->ret_rva);
            if (r->is_bytes) {
                fputs(",\"peek\":\"", f);
                for (int j = 0; j < r->peek_length; ++j)
                    fprintf(f, "%02X", r->peek[j]);
                fputs("\"}", f);
            } else {
                fprintf(f, ",\"v\":%lu}", (unsigned long)r->value);
            }
        }
        fputs("]}\n", f);
        fclose(f);
    }
    g_read_frame_records = 0;
    g_read_frame_dropped = 0;
    g_read_frame_resets = 0;
}

/* Return address of the game code that called the detour, as an RVA. Under
 * MinHook the detour is entered directly from the original call site, so this
 * identifies the reading handler -- the one thing the static walk loses when it
 * reaches the packed region. */
static uint32_t caller_rva(void* return_address)
{
    uintptr_t a = (uintptr_t)return_address;
    uintptr_t base = g_host ? g_host->dnf_base : 0;
    return (uint32_t)((base && a >= base) ? (a - base) : a);
}

/* Append one primitive call. Offsets are relative to the frame origin, so a
 * negative value means the origin model is wrong for this frame; it is recorded
 * rather than clamped so that shows up in the trace instead of hiding. */
static void record_read(uintptr_t cursor, int32_t width, uint32_t value,
    const uint8_t* bytes, void* return_address)
{
    ch_read_record* r;

    if (!g_read_frame_active) return;
    if (g_read_frame_records >= READ_TRACE_MAX_RECORDS) {
        ++g_read_frame_dropped;
        return;
    }
    r = &g_read_frame[g_read_frame_records++];
    r->offset = (int32_t)(cursor - g_read_frame_origin);
    r->width = width;
    r->value = value;
    r->ret_rva = caller_rva(return_address);
    r->peek_length = 0;
    r->is_bytes = (bytes != NULL) ? 1u : 0u;
    if (bytes != NULL) {
        int n = width < READ_TRACE_PEEK ? width : READ_TRACE_PEEK;
        if (n < 0) n = 0;
        for (int i = 0; i < n; ++i) r->peek[i] = bytes[i];
        r->peek_length = (uint8_t)n;
    }
}

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

/* Record every ISocket::Write call: its leading bytes (to identify which frame
 * the chunk belongs to), the size, and a short call stack. This turns the
 * single send chokepoint into a map of (message -> builder). Only the first
 * chunk of each frame carries the 13-byte header, so callers that push a
 * header reveal their opcode directly. */
static void trace_socket_write(const uint8_t* buffer, int size)
{
    FILE* f;
    int peek, hdr = 0;
    uint16_t type = 0;

    if (!g_write_trace_enabled) return;
    if (buffer == NULL || size <= 0) return;
    if (_wfopen_s(&f, g_trace_log_path, L"a") != 0 || f == NULL) return;

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

/* Open the capture log for append, or return NULL after one host log line. */
static FILE* open_capture_log(void)
{
    FILE* f;
    if (_wfopen_s(&f, g_capture_log_path, L"a") != 0 || f == NULL) {
        if (g_host && g_host->log)
            g_host->log(1, "NetworkHook", "frame-capture: failed to open log");
        return NULL;
    }
    return f;
}

/* Hex-dump `length` bytes, capped by MaxBodyBytes. Returns the number of bytes
 * actually written so the caller can flag truncation. */
static int dump_hex(FILE* f, const uint8_t* bytes, int length)
{
    int limit = length;
    if (g_capture_max_body > 0 && limit > g_capture_max_body)
        limit = g_capture_max_body;
    for (int i = 0; i < limit; ++i) fprintf(f, "%02X", bytes[i]);
    return limit;
}

static void log_capture_line(const char* dir, uint16_t type, int total,
    int body_len)
{
    char msg[96];
    if (g_host == NULL || g_host->log == NULL) return;
    snprintf(msg, sizeof(msg), "frame-capture: %s type=0x%04X len=%d body=%d",
        dir, type, total, body_len);
    g_host->log(3, "NetworkHook", msg);
}

/* Outbound dump. `frame` points at the 13-byte header and `total` is
 * header+body, so the on-wire bytes are recorded verbatim. `packet_type` is
 * the int argument the cipher received; it is kept as `arg_type` for
 * cross-checking against the u16 type field inside the header. */
static void dump_sent_frame(int packet_type, const uint8_t* frame, int total)
{
    FILE* f;
    uint16_t hdr_type;
    int body_len, dumped;

    if (frame == NULL || total < (int)HEADER_SIZE) return;
    hdr_type = (uint16_t)(frame[1] | (frame[2] << 8));
    body_len = total - (int)HEADER_SIZE;
    f = open_capture_log();
    if (f == NULL) return;

    fprintf(f, "{\"dir\":\"send\",\"hdr\":1,\"arg_type\":%d,\"cmd\":%u,"
        "\"type\":%u,\"length\":%d,\"seq\":%u,\"body_len\":%d,\"hex\":\"",
        packet_type, frame[0], hdr_type, total,
        (unsigned)(frame[11] | (frame[12] << 8)), body_len);
    dumped = dump_hex(f, frame, total);
    fprintf(f, "\",\"dumped\":%d", dumped);
    if (type_selected(g_stack_types, g_stack_type_count, hdr_type))
        dump_call_stack(f);
    else
        fputs("}", f);
    fputs("\n", f);
    fclose(f);
    log_capture_line("send", hdr_type, total, body_len);
}

/* Inbound dump. Only `body[0..body_len)` may be read: the decrypt input is not
 * preceded by the frame header, so `cmd`/`length`/`seq` are unavailable here
 * and the identity comes from the `packet_type` argument alone (recorded as
 * both `arg_type` and `type`, with `hdr:0` marking the weaker provenance). */
static void dump_received_body(int packet_type, const uint8_t* body,
    int body_len)
{
    FILE* f;
    int dumped;

    if (body == NULL || body_len < 0) return;
    f = open_capture_log();
    if (f == NULL) return;

    fprintf(f, "{\"dir\":\"recv\",\"hdr\":0,\"arg_type\":%d,\"type\":%d,"
        "\"body_len\":%d,\"hex\":\"", packet_type, packet_type, body_len);
    dumped = dump_hex(f, body, body_len);
    fprintf(f, "\",\"dumped\":%d", dumped);
    if (packet_type >= 0 && packet_type <= 0xFFFF
        && type_selected(g_stack_types, g_stack_type_count,
            (uint16_t)packet_type))
        dump_call_stack(f);
    else
        fputs("}", f);
    fputs("\n", f);
    fclose(f);
    log_capture_line("recv", (uint16_t)packet_type, body_len, body_len);
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
    if (!g_capture_enabled) return 0;
    if (packet_type < 0 || packet_type > 0xFFFF) return 0;
    return type_selected(g_capture_types, g_capture_type_count,
        (uint16_t)packet_type);
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
    /* Finalize totalLength BEFORE capturing: MakePacket leaves a stale value
     * in the header, so dumping first would record a length field that never
     * reaches the wire (the 2026-08-13 00AB samples show the stale 0x3D). */
    *(int*)(input - HEADER_SIZE + HEADER_TOTALLENGTH_OFFSET) =
        input_size + HEADER_SIZE;
    if (should_capture(packet_type)) {
        dump_sent_frame(packet_type, (const uint8_t*)input - HEADER_SIZE,
            input_size + (int)HEADER_SIZE);
    }
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
    /* The read trace has no other way to name the frame it is about to see:
     * the primitives are handed a stream, not a message. Recording the opcode
     * here works because the receive function decrypts before it dispatches. */
    g_last_decrypt_type = (long)packet_type;
    if (should_capture(packet_type)) {
        dump_received_body(packet_type, (const uint8_t*)input, input_size);
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

/* The four read primitives, all __cdecl with a bare `ret`. The cursor is
 * sampled before the original runs -- that is the field's offset -- and the
 * value after it, because the original is what writes through `dest`. */
typedef void(__cdecl* ch_read_scalar_fn)(void* dest);
typedef void(__cdecl* ch_read_bytes_fn)(void* dest, int length);
typedef void(__cdecl* ch_set_remaining_fn)(int remaining);

static void __cdecl read_u8_trace(void* dest)
{
    uintptr_t cursor = inbound_cursor();
    void* ret = CH_RETURN_ADDRESS();
    ((ch_read_scalar_fn)g_original_read_u8)(dest);
    if (dest) record_read(cursor, 1, *(uint8_t*)dest, NULL, ret);
}

static void __cdecl read_u16_trace(void* dest)
{
    uintptr_t cursor = inbound_cursor();
    void* ret = CH_RETURN_ADDRESS();
    ((ch_read_scalar_fn)g_original_read_u16)(dest);
    if (dest) record_read(cursor, 2, *(uint16_t*)dest, NULL, ret);
}

static void __cdecl read_u32_trace(void* dest)
{
    uintptr_t cursor = inbound_cursor();
    void* ret = CH_RETURN_ADDRESS();
    ((ch_read_scalar_fn)g_original_read_u32)(dest);
    if (dest) record_read(cursor, 4, *(uint32_t*)dest, NULL, ret);
}

static void __cdecl read_bytes_trace(void* dest, int length)
{
    uintptr_t cursor = inbound_cursor();
    void* ret = CH_RETURN_ADDRESS();
    ((ch_read_bytes_fn)g_original_read_bytes)(dest, length);
    if (dest && length > 0)
        record_read(cursor, length, 0, (const uint8_t*)dest, ret);
}

/* Frame boundary. Two call sites inside the receive function share this
 * function, so a boundary is only taken once reads have accumulated; otherwise
 * the origin is simply moved to the later of the two, which is the one that
 * immediately precedes dispatch. */
static void __cdecl set_remaining_trace(int remaining)
{
    if (g_read_frame_records > 0) flush_read_frame();
    else if (g_read_frame_active) ++g_read_frame_resets;

    ((ch_set_remaining_fn)g_original_set_remaining)(remaining);

    g_read_frame_origin = inbound_cursor();
    g_read_frame_remaining = (int32_t)remaining;
    g_read_frame_type = g_last_decrypt_type;
    g_read_frame_active = (g_last_decrypt_type >= 0
        && g_last_decrypt_type <= 0xFFFF
        && type_selected(g_read_trace_types, g_read_trace_type_count,
            (uint16_t)g_last_decrypt_type)) ? 1 : 0;
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
    load_capture_config();
    load_read_trace_config();
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
    /* Installed only when asked for. These sit on the hottest path in the
     * receive layer, and an always-present trampoline on it would make every
     * session pay for a diagnostic that most sessions do not want.
     * SetRemaining goes on last: it is what arms the read detours, so the four
     * reads must already be in place when the first frame boundary arrives. */
    if (status == CH_STATUS_OK && g_read_trace_enabled) {
        status = create_hook(host, 0x01D00420u, CH_SIG_READ_U8,
            sizeof(CH_SIG_READ_U8), (void*)read_u8_trace,
            &g_original_read_u8, "inbound-read-u8");
        if (status == CH_STATUS_OK) {
            status = create_hook(host, 0x01D00490u, CH_SIG_READ_U16,
                sizeof(CH_SIG_READ_U16), (void*)read_u16_trace,
                &g_original_read_u16, "inbound-read-u16");
        }
        if (status == CH_STATUS_OK) {
            status = create_hook(host, 0x01D00500u, CH_SIG_READ_U32,
                sizeof(CH_SIG_READ_U32), (void*)read_u32_trace,
                &g_original_read_u32, "inbound-read-u32");
        }
        if (status == CH_STATUS_OK) {
            status = create_hook(host, 0x01D00570u, CH_SIG_READ_BYTES,
                sizeof(CH_SIG_READ_BYTES), (void*)read_bytes_trace,
                &g_original_read_bytes, "inbound-read-bytes");
        }
        if (status == CH_STATUS_OK) {
            status = create_hook(host, 0x01CFF6A0u, CH_SIG_SET_REMAINING,
                sizeof(CH_SIG_SET_REMAINING), (void*)set_remaining_trace,
                &g_original_set_remaining, "inbound-set-remaining");
        }
    }
#if defined(_MSC_VER)
#pragma warning(pop)
#endif
    return status;
}
