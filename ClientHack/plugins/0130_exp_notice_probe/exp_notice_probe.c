/* Where does a kill's experience notice stop?
 *
 * THE QUESTION
 *
 * A kill raises the character's experience and the client draws the new total,
 * but no line about it appears in any chat tab. Two explanations survive a
 * static read of the handler at 0x01178CD0, and they call for opposite repairs:
 *
 *   (a) the client computes the gain as zero and never builds a line at all --
 *       the notice is never produced, and the fault is in what the server sends
 *       or in what the client already holds;
 *   (b) the line is built and handed to the chat object, which drops it -- the
 *       fault is in chat filtering, downstream of everything the server does.
 *
 * Reading the disassembly cannot separate them: it shows the branches, not
 * which one a live session takes. This plugin is the measurement. It changes no
 * behaviour and reads no state the client does not already have in registers --
 * it only records which of three points a real kill reaches.
 *
 * THE THREE POINTS
 *
 *   ENTERED   0x011793AA, where the handler applies the level it was sent.
 *             On the straight-line path from the frame's fields, so it fires
 *             once per NOTI 0x0025 and proves the handler ran at all.
 *   GAIN      0x01179503, the first call after `gain > 0` is decided
 *             (0x011794F8 jumps past it when the gain is zero or less).
 *             Fires only when there is something to announce.
 *   HANDED    0x01179C63, where the assembled line goes to the chat object
 *             with its colour and type. The text is recorded with it.
 *
 * HOW TO READ THE RESULT
 *
 *   ENTERED, no GAIN         -> (a). The client saw no gain. Chat filtering is
 *                               innocent and the fault is upstream of it.
 *   ENTERED, GAIN, HANDED    -> (b). The line existed and was handed over. If
 *                               the player saw nothing, it died at or after
 *                               0x01A60620, which is where filtering lives.
 *   GAIN but no HANDED       -> neither: it is lost between the two, and the
 *                               eleven hundred bytes in between are where to
 *                               look next.
 *   no ENTERED               -> the handler never ran, and the frame itself is
 *                               the subject.
 *
 * WHAT IT IS NOT
 *
 * Not a fix, and deliberately not installed alongside one: a run with a repair
 * in place cannot say what the unrepaired client did. Each probe is a five-byte
 * `call rel32` replacing a `call rel32`, validated against the bytes the site
 * holds, and each detour forwards to the routine it replaced. Nothing else is
 * written.
 */

#include "clienthack/plugin_api.h"

#include <windows.h>
#include <stdio.h>

#define ARRAY_COUNT(values) (sizeof(values) / sizeof((values)[0]))

static const ch_host_api* g_host;

/* Upstream and this project both number the image from 0x00400000. */
#define RVA(va) ((uintptr_t)((va) - 0x00400000u))

#define SITE_ENTERED    RVA(0x011793AAu)    /* -> 0x0183EA10 apply level      */
#define SITE_GAIN       RVA(0x01179503u)    /* -> 0x027BACC0 header literal   */
#define SITE_HANDED     RVA(0x01179C63u)    /* -> 0x01A60620 add a chat line  */

#define RVA_APPLY_LEVEL     RVA(0x0183EA10u)
#define RVA_DECRYPT_LITERAL RVA(0x027BACC0u)
#define RVA_ADD_CHAT_LINE   RVA(0x01A60620u)

/* The five bytes each site holds: `call rel32` reaching the routine above.
 * Validated, so an image whose call sites moved refuses instead of having five
 * bytes of some other instruction overwritten. */
static const uint8_t bytes_entered[5] = { 0xE8, 0x61, 0x56, 0x6C, 0x00 };
static const uint8_t bytes_gain[5] = { 0xE8, 0xB8, 0x17, 0x64, 0x01 };
static const uint8_t bytes_handed[5] = { 0xE8, 0xB8, 0x69, 0x8E, 0x00 };

static void* at(uintptr_t rva)
{
    return (void*)(g_host->dnf_base + rva);
}

static void say(const char* message)
{
    if (g_host != NULL && g_host->log != NULL)
        g_host->log(2, "ExpNoticeProbe", message);
}

/* The routines being forwarded to. Two are __thiscall, which MSVC will not
 * accept on a function-pointer typedef in C; both are spelled __fastcall with a
 * dead second argument, which is the same thing on the wire -- object in ECX,
 * the rest on the stack, callee cleaning, EDX scratch. */
typedef void (__fastcall *fn_apply_level)(void*, int, int);
typedef void* (__cdecl *fn_decrypt_literal)(void*);
typedef void (__fastcall *fn_add_chat_line)(
    void*, int, const char*, unsigned int, int, int, int, int, int);

#define EDX_UNUSED 0

/* The handler ran, and this is the level the frame carried. */
static void __fastcall probe_entered(void* character, void* edx, int level)
{
    char line[128];
    (void)edx;
    _snprintf_s(line, sizeof(line), _TRUNCATE, "ENTERED level=%d", level);
    say(line);
    ((fn_apply_level)at(RVA_APPLY_LEVEL))(character, EDX_UNUSED, level);
}

/* Reached only when the client decided the gain is positive. */
static void* __cdecl probe_gain(void* literal)
{
    say("GAIN positive, building the notice");
    return ((fn_decrypt_literal)at(RVA_DECRYPT_LITERAL))(literal);
}

/* The assembled line, on its way to the chat object. */
static void __fastcall probe_handed(
    void* chat, void* edx, const char* text, unsigned int colour, int type,
    int a, int b, int c, int d)
{
    char line[400];
    (void)edx;
    /* The text is the client's own buffer and always NUL-terminated by the
     * formatting that filled it; %.200s bounds what reaches the log anyway, so
     * a truncated or oddly encoded line cannot run off the end of it. */
    _snprintf_s(line, sizeof(line), _TRUNCATE,
        "HANDED type=%d colour=%08X text=%.200s", type, colour,
        text != NULL ? text : "(null)");
    say(line);
    ((fn_add_chat_line)at(RVA_ADD_CHAT_LINE))(
        chat, EDX_UNUSED, text, colour, type, a, b, c, d);
}

/* ---- where the chat filter is actually stored --------------------------- */

/* The store behind the chat filter reaches kernel32's PrivateProfile family,
 * which is a plain INI on disk. Which INI, though, is a string this client
 * keeps encrypted, and no file appeared anywhere obvious after a save -- so
 * these two probes ask the API itself rather than guessing at a path.
 *
 * The read probe also settles the claim the whole chat-filter theory rests on:
 * that a never-configured client hands back a 154-byte blob of zeros and
 * reports success. Every read logs its size, its result, and the sum of the
 * bytes it produced, which is exactly the quantity upstream's repair tests.
 *
 * Hooked at the export rather than at a call site, so a write from anywhere in
 * the client is seen -- including one that happens at shutdown, which is where
 * an INI write can hide. */

typedef BOOL (WINAPI *fn_write_struct)(LPCWSTR, LPCWSTR, LPVOID, UINT, LPCWSTR);
typedef BOOL (WINAPI *fn_read_struct)(LPCWSTR, LPCWSTR, LPVOID, UINT, LPCWSTR);

static fn_write_struct g_real_write_struct;
static fn_read_struct g_real_read_struct;

static BOOL WINAPI probe_write_struct(
    LPCWSTR section, LPCWSTR key, LPVOID data, UINT size, LPCWSTR file)
{
    char line[400];
    BOOL ok = g_real_write_struct(section, key, data, size, file);
    _snprintf_s(line, sizeof(line), _TRUNCATE,
        "INI WRITE ok=%d size=%u file=%.120S section=%.60S key=%.40S",
        ok, size,
        file != NULL ? file : L"(null)",
        section != NULL ? section : L"(null)",
        key != NULL ? key : L"(null)");
    say(line);
    return ok;
}

static BOOL WINAPI probe_read_struct(
    LPCWSTR section, LPCWSTR key, LPVOID data, UINT size, LPCWSTR file)
{
    char line[400];
    unsigned long sum = 0;
    UINT i;
    BOOL ok = g_real_read_struct(section, key, data, size, file);
    if (ok && data != NULL) {
        for (i = 0; i < size; ++i)
            sum += ((const unsigned char*)data)[i];
    }
    _snprintf_s(line, sizeof(line), _TRUNCATE,
        "INI READ ok=%d size=%u sum=%lu file=%.120S section=%.60S key=%.40S",
        ok, size, sum,
        file != NULL ? file : L"(null)",
        section != NULL ? section : L"(null)",
        key != NULL ? key : L"(null)");
    say(line);
    return ok;
}

/* Absent or unhookable exports are reported and skipped: they are a second
 * question, and losing the three probes above over them would cost the run. */
static void arm_profile_probes(const ch_host_api* host)
{
    HMODULE kernel32;
    void* target;

    if (host->hook_create_absolute == NULL) {
        say("no absolute-hook support; INI probes not armed");
        return;
    }

    kernel32 = GetModuleHandleW(L"kernel32.dll");
    if (kernel32 == NULL) {
        say("kernel32 not found; INI probes not armed");
        return;
    }

    target = (void*)GetProcAddress(kernel32, "WritePrivateProfileStructW");
    if (target != NULL && host->hook_create_absolute(
            target, (void*)probe_write_struct,
            (void**)&g_real_write_struct) == CH_STATUS_OK) {
        say("INI write probe armed");
    } else {
        say("INI write probe NOT armed");
    }

    target = (void*)GetProcAddress(kernel32, "GetPrivateProfileStructW");
    if (target != NULL && host->hook_create_absolute(
            target, (void*)probe_read_struct,
            (void**)&g_real_read_struct) == CH_STATUS_OK) {
        say("INI read probe armed");
    } else {
        say("INI read probe NOT armed");
    }
}

/* ---- installation ------------------------------------------------------ */

/* `call rel32` reaching the detour from this site, built at load time: the
 * detour is in this DLL and the site in the client, and neither address is
 * known until both are mapped. */
static ch_status redirect(uintptr_t rva, const uint8_t* expected, void* detour,
    const char* name)
{
    uint8_t replacement[5];
    uintptr_t from = g_host->dnf_base + rva;
    uintptr_t rel = (uintptr_t)detour - (from + 5);
    ch_patch_request request;

    replacement[0] = 0xE8;
    replacement[1] = (uint8_t)(rel & 0xFFu);
    replacement[2] = (uint8_t)((rel >> 8) & 0xFFu);
    replacement[3] = (uint8_t)((rel >> 16) & 0xFFu);
    replacement[4] = (uint8_t)((rel >> 24) & 0xFFu);

    request.struct_size = sizeof(request);
    request.target_rva = rva;
    request.expected = expected;
    request.replacement = replacement;
    request.length = sizeof(replacement);
    request.name = name;
    return g_host->patch_apply(&request);
}

typedef struct probe_site {
    uintptr_t rva;
    const uint8_t* expected;
    void* detour;
    const char* name;
} probe_site;

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)
{
    probe_site sites[3];
    size_t i;

    g_host = host;
    if (host == NULL || host->struct_size < sizeof(*host) ||
        host->abi_version != CLIENTHACK_ABI_VERSION ||
        host->patch_apply == NULL || host->log == NULL) {
        return CH_STATUS_UNSUPPORTED_ABI;
    }

    sites[0].rva = SITE_ENTERED;
    sites[0].expected = bytes_entered;
    sites[0].detour = (void*)probe_entered;
    sites[0].name = "exp-probe-entered";
    sites[1].rva = SITE_GAIN;
    sites[1].expected = bytes_gain;
    sites[1].detour = (void*)probe_gain;
    sites[1].name = "exp-probe-gain";
    sites[2].rva = SITE_HANDED;
    sites[2].expected = bytes_handed;
    sites[2].detour = (void*)probe_handed;
    sites[2].name = "exp-probe-handed";

    for (i = 0; i < ARRAY_COUNT(sites); ++i) {
        ch_status status = redirect(
            sites[i].rva, sites[i].expected, sites[i].detour, sites[i].name);
        if (status != CH_STATUS_OK) {
            host->log(3, "ExpNoticeProbe", sites[i].name);
            return status;
        }
    }

    say("three probes armed on the experience notice path");
    arm_profile_probes(host);
    return CH_STATUS_OK;
}
