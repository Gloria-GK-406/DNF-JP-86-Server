/* S4A21 runtime repairs: routines the protector broke, plus plaintext cipher.
 *
 * The byte patches in 0010/0020 are not enough to reach the game. Three of the
 * client's own routines have to be REPLACED, because on this client they no
 * longer do what their callers expect:
 *
 *   CThread::Create   virtualised on S4A21 (the call site jumps to 0x04F69122,
 *                     a VM entry). In this runtime it neither resumes the new
 *                     thread nor preserves the object vtable, so MTUPD's
 *                     StartListening returns having started nothing and the
 *                     client waits forever on a listener that does not exist.
 *                     This is the freeze, not a crash: nothing faults.
 *   Cipher::Encrypt   applies a fixed-key XOR. This server speaks plaintext.
 *   Cipher::Decrypt   likewise.
 *   ISocket::Write    the packed original does not stage correctly; replaced by
 *                     the official ring-buffer push.
 *
 * Ported from S4A21ClientPatch/GameGaurd/GameGaurd.cpp (PatchS4A21). Upstream
 * installs these blind; here every target is byte-verified first -- and the
 * cipher/write signatures turn out to be byte-identical to the S4A12 ones in
 * runtime_signatures.h, because they are the same functions from the same
 * compiler at different addresses. Only the addresses are per-client.
 *
 * Why this is its own plugin rather than an S4A21 NetworkHook: the cipher and
 * write hooks belong in NetworkHook, and should move there once it is ported.
 * NetworkHook also carries frame capture and the read-primitive traces, all of
 * which are S4A12 addresses; porting the whole of it just to reach the login
 * screen would mean carrying six unported, unverified hook sites. When
 * NetworkHook lands for S4A21, the three hooks below move into it and this
 * plugin keeps only the CThread::Create repair.
 */
#include "clienthack/plugin_api.h"
#include "clienthack/runtime_signatures.h"

#include <windows.h>
#include <string.h>

/* --- S4A21 addresses (RVA; upstream states them as VA = RVA + 0x00400000) - */
#define A21_CIPHER_ENCRYPT_RVA   0x022B7D90u
#define A21_CIPHER_DECRYPT_RVA   0x022B7E50u
#define A21_ISOCKET_WRITE_RVA    0x05390FE9u
/* The CALL to CThread::Create, not the function: the function is a VM entry,
 * so the call site is what gets redirected -- exactly as upstream does. */
#define A21_CTHREAD_CALLSITE_RVA 0x024A06A2u
#define A21_THREAD_THUNK_RVA     0x024A4DC0u   /* shared start routine */
#define A21_LISTENER_VTABLE_RVA  0x0321C50Cu   /* listener class vtable */

/* Outbound header is 14 bytes on this client (13 on S4A12); the total length
 * field sits at offset 3. */
#define A21_HEADER_SIZE 14
#define A21_HEADER_TOTALLENGTH_OFFSET 3

/* Send staging ring inside the socket object, from the official layout:
 *   ring+0x000000  byte buffer[0xAF000]
 *   ring+0x0AF000  wpos (producer)
 *   ring+0x0AF004  rpos (consumer, advanced by the untouched flush side)
 *   ring+0x0AF008  CRITICAL_SECTION
 * wpos == rpos means EMPTY, so a push must never let wpos catch rpos; the ring
 * therefore holds at most STAGE_SIZE-1 bytes. */
#define STAGE_OFFSET 0x15E218u
#define STAGE_SIZE   0xAF000u

static const ch_host_api* g_host;
static void* g_original_encrypt;
static void* g_original_decrypt;
static void* g_original_socket_write;
static uint8_t g_cthread_call_bytes[5];
static const uint8_t g_cthread_call_expected[5] = { 0xE8, 0x7B, 0x8A, 0x6C, 0x02 };

static void log_line(uint32_t level, const char* message)
{
    if (g_host != NULL && g_host->log != NULL)
        g_host->log(level, "A21RuntimeFix", message);
}

/* --- CThread::Create ------------------------------------------------------ */

/* Creates the thread and resumes it, the way the official client does:
 *   handle at this+0x1C, tid at this+0x20, started flag at this+0x24. */
static DWORD __fastcall proxy_cthread_create(void* self_ptr, void* edx_value)
{
    DWORD* self = (DWORD*)self_ptr;
    HANDLE thread;
    const uintptr_t base = (g_host != NULL) ? g_host->dnf_base : 0;
    const DWORD vtable = (DWORD)(base + A21_LISTENER_VTABLE_RVA);
    (void)edx_value;
    if (self == NULL) return 0;
    /* A previous trip through the VM path can leave 0x103 in [this+0]; restore
     * the vtable so the shared thunk can dispatch Run(). */
    if (*self != vtable) *self = vtable;
    thread = CreateThread(NULL, 0,
        (LPTHREAD_START_ROUTINE)(base + A21_THREAD_THUNK_RVA), self,
        CREATE_SUSPENDED, (LPDWORD)(self + 8));
    self[7] = (DWORD)(uintptr_t)thread;
    if (thread == NULL) return 0;      /* the official throws here */
    SetThreadPriority(thread, THREAD_PRIORITY_HIGHEST);
    *((BYTE*)self + 36) = 1;
    return ResumeThread(thread);
}

/* --- Cipher --------------------------------------------------------------- */

/* The header sits immediately before the body, and MakePacket leaves a stale
 * totalLength in it, so the field is finalized unconditionally -- including for
 * empty bodies. A stale length misframes the packet on the peer. */
static int __fastcall cipher_encrypt_passthrough(
    void* self, void* edx_value, int packet_type, const char* input,
    int input_size, char* output, int* output_size)
{
    (void)self;
    (void)edx_value;
    (void)packet_type;
    if (input == NULL || output == NULL || output_size == NULL || input_size < 0)
        return 0;
    *(int*)(input - A21_HEADER_SIZE + A21_HEADER_TOTALLENGTH_OFFSET) =
        input_size + A21_HEADER_SIZE;
    if (input_size != 0) memcpy(output, input, (size_t)input_size);
    *output_size = input_size;
    return 1;
}

static int __fastcall cipher_decrypt_passthrough(
    void* self, void* edx_value, int packet_type, const char* input,
    int input_size, char* output, int* output_size)
{
    (void)self;
    (void)edx_value;
    (void)packet_type;
    if (input == NULL || output == NULL || output_size == NULL || input_size < 0)
        return 0;
    if (input_size != 0) memcpy(output, input, (size_t)input_size);
    *output_size = input_size;
    return 1;
}

/* --- ISocket::Write ------------------------------------------------------- */

/* Error codes and the wrapper's return convention (1 / 0x800000xx) match the
 * original, because callers on this client branch on them. */
static int stage_ring_push(char* ring, const char* src, int size)
{
    CRITICAL_SECTION* cs = (CRITICAL_SECTION*)(ring + STAGE_SIZE + 8);
    DWORD* wpos = (DWORD*)(ring + STAGE_SIZE);
    DWORD* rpos = (DWORD*)(ring + STAGE_SIZE + 4);
    DWORD wp;
    DWORD rp;
    DWORD tail;

    EnterCriticalSection(cs);
    if ((unsigned)size >= STAGE_SIZE) {
        LeaveCriticalSection(cs);
        return -1;
    }
    wp = *wpos;
    rp = *rpos;
    if (wp < rp) {
        if (size >= (int)(rp - wp)) {
            LeaveCriticalSection(cs);
            return -4;
        }
        memcpy(ring + wp, src, (size_t)size);
        *wpos = wp + (DWORD)size;
        LeaveCriticalSection(cs);
        return 0;
    }
    tail = STAGE_SIZE - wp;                      /* room from wpos to the end */
    if (size < (int)tail) {
        memcpy(ring + wp, src, (size_t)size);
        *wpos = wp + (DWORD)size;
        LeaveCriticalSection(cs);
        return 0;
    }
    if (size == (int)tail) {
        if (rp == 0) {
            /* filling exactly to the end would force wpos == rpos == 0, which
             * the consumer would read as EMPTY */
            LeaveCriticalSection(cs);
            return -2;
        }
        memcpy(ring + wp, src, (size_t)size);
        wp += (DWORD)size;
        if (wp == STAGE_SIZE) wp = 0;
        *wpos = wp;
        LeaveCriticalSection(cs);
        return 0;
    }
    if (size >= (int)(tail + rp)) {
        LeaveCriticalSection(cs);
        return -3;
    }
    memcpy(ring + wp, src, (size_t)tail);                    /* fill to the end */
    memcpy(ring, src + tail, (size_t)size - (size_t)tail);   /* wrap the rest */
    *wpos = (DWORD)size - tail;
    LeaveCriticalSection(cs);
    return 0;
}

static int __fastcall proxy_isocket_write(
    void* self_ptr, void* edx_value, char* buffer, int size)
{
    char* base = (char*)self_ptr;
    (void)edx_value;
    if (base == NULL) return (int)0x80000000;
    if (!*(base + 4)) return (int)0x80000001;    /* not initialized */
    if (!*(base + 8)) return (int)0x80000054;    /* socket closed */
    if (size == 0 || buffer == NULL) return (int)0x80000000;
    return stage_ring_push(base + STAGE_OFFSET, buffer, size) >= 0
        ? 1 : (int)0x80000059;
}

/* --- install -------------------------------------------------------------- */

static ch_status create_hook(uintptr_t rva, const uint8_t* expected,
    uint32_t expected_length, void* detour, void** original, const char* name)
{
    ch_hook_request request = {
        sizeof(request), rva, expected, expected_length,
        detour, original, name
    };
    return g_host->hook_create(&request);
}

/* The call site is redirected by rewriting its rel32, which can only be
 * computed once this DLL is loaded -- hence a runtime-filled replacement
 * buffer rather than a constant one. The expected bytes still pin the site: if
 * this is not the `call 0x04F69122` we measured, nothing is written. */
static ch_status redirect_cthread_callsite(void)
{
    const uintptr_t site = g_host->dnf_base + A21_CTHREAD_CALLSITE_RVA;
    const int32_t rel = (int32_t)((uintptr_t)(void*)proxy_cthread_create
        - (site + 5));
    ch_patch_request request;
    g_cthread_call_bytes[0] = 0xE8;
    memcpy(&g_cthread_call_bytes[1], &rel, sizeof(rel));
    request.struct_size = sizeof(request);
    request.target_rva = A21_CTHREAD_CALLSITE_RVA;
    request.expected = g_cthread_call_expected;
    request.replacement = g_cthread_call_bytes;
    request.length = (uint32_t)sizeof(g_cthread_call_bytes);
    request.name = "cthread-create-callsite";
    return g_host->patch_apply(&request);
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
#if defined(_MSC_VER)
#pragma warning(push)
#pragma warning(disable: 4055)
#endif
    status = create_hook(A21_CIPHER_ENCRYPT_RVA, CH_SIG_CIPHER_ENCRYPT,
        sizeof(CH_SIG_CIPHER_ENCRYPT), (void*)cipher_encrypt_passthrough,
        &g_original_encrypt, "cipher-encrypt-passthrough");
    if (status == CH_STATUS_OK) {
        status = create_hook(A21_CIPHER_DECRYPT_RVA, CH_SIG_CIPHER_DECRYPT,
            sizeof(CH_SIG_CIPHER_DECRYPT), (void*)cipher_decrypt_passthrough,
            &g_original_decrypt, "cipher-decrypt-passthrough");
    }
    if (status == CH_STATUS_OK) {
        status = create_hook(A21_ISOCKET_WRITE_RVA, CH_SIG_SOCKET_WRITE,
            sizeof(CH_SIG_SOCKET_WRITE), (void*)proxy_isocket_write,
            &g_original_socket_write, "socket-write-staging");
    }
#if defined(_MSC_VER)
#pragma warning(pop)
#endif
    if (status != CH_STATUS_OK) return status;
    status = redirect_cthread_callsite();
    if (status != CH_STATUS_OK) return status;
    log_line(3, "cipher passthrough, socket staging and CThread::Create installed");
    return CH_STATUS_OK;
}
