/* The client's struct-shaped config reads are writes. This makes them reads.
 *
 * WHAT IS WRONG
 *
 * IAT slot 0x0302928C holds kernel32!WritePrivateProfileStructW, and the code
 * that calls through it wants GetPrivateProfileStructW. The two are 0x500 apart
 * in kernel32 and share a signature exactly -- (section, key, struct, size,
 * file) -- so the mistake does not crash. It silently inverts every read of a
 * struct-shaped setting into a write of whatever the caller had in its buffer,
 * and returns TRUE, so the caller believes it read what it in fact just
 * destroyed.
 *
 * HOW IT WAS FOUND, AND WHAT IT COST
 *
 * A kill's experience notice never appeared in any chat tab. Probes on the
 * notice path showed the line was built and handed to the chat object, so it
 * was being dropped downstream; the chat filter is a 154-byte blob, and the
 * blob in DNF.cfg was 154 bytes of zeros -- every message type switched off,
 * including the two the experience notice uses. The zeros were not a stale
 * setting. They were written by the client's own attempt to READ them.
 *
 * Under a debugger on 2026-08-28, before this fix, startup produced four
 * WritePrivateProfileStructW calls and no reads:
 *
 *     size=80  CONFIG_COMMON_IGA_FILEVERSION
 *     size=12  CONFIG_CHATTING_WINDOW_SIZE
 *     size=154 CONFIG_CHARACTER_CHATTING_FILTER
 *     size=12  CONFIG_LAST_JOIN_INFO_ON_CLIENT_CRASH
 *
 * With the slot corrected, the same four became reads and nothing was written.
 * So this is not only the chat filter: no struct-shaped setting in this client
 * has ever survived a restart.
 *
 * WHY IT IS PATCHED HERE AND NOT IN THE IAT TABLE
 *
 * Because the client really is like this. `DNF-iat.csv` records the slot read
 * out of the live packed original, and it holds the writer there too -- the
 * table in src/az/iat_table_a21.h is a faithful record, not a mistake, and
 * editing it would only make the dump's own verification lie about what the
 * client contains. The upstream client patch carries the same entry for the
 * same reason, and works around the consequences in the chat layer instead of
 * here. This is a deliberate deviation from the client as shipped, which is
 * why it is one named patch with its evidence attached rather than a quiet
 * correction to a record of facts.
 *
 * The slot is validated before it is changed: if it does not hold the writer,
 * nothing is written and the plugin reports it. This plugin is not required, so
 * a client it does not recognise starts without it.
 */

#include "clienthack/plugin_api.h"

#include <windows.h>
#include <stdio.h>

/* VA in the 0x00400000-based image; the slot is in the import directory. */
#define IAT_SLOT_RVA ((uintptr_t)(0x0302928Cu - 0x00400000u))

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)
{
    HMODULE kernel32;
    void* writer;
    void* reader;
    void** slot;
    DWORD previous;
    char line[256];

    if (host == NULL || host->struct_size < sizeof(*host) ||
        host->abi_version != CLIENTHACK_ABI_VERSION || host->log == NULL) {
        return CH_STATUS_UNSUPPORTED_ABI;
    }

    kernel32 = GetModuleHandleW(L"kernel32.dll");
    if (kernel32 == NULL) {
        host->log(3, "ProfileReadFix", "kernel32 not present");
        return CH_STATUS_NOT_FOUND;
    }

    /* Resolved rather than hard-coded: kernel32 moves with every boot, so a
     * literal address would be right on the machine it was written on and
     * silently wrong everywhere else -- which is the shape of the defect this
     * repairs. */
    writer = (void*)GetProcAddress(kernel32, "WritePrivateProfileStructW");
    reader = (void*)GetProcAddress(kernel32, "GetPrivateProfileStructW");
    if (writer == NULL || reader == NULL) {
        host->log(3, "ProfileReadFix", "profile struct exports not found");
        return CH_STATUS_NOT_FOUND;
    }

    slot = (void**)(host->dnf_base + IAT_SLOT_RVA);

    /* A client whose slot already reads correctly, or holds something else
     * entirely, is left alone and said so: the point of this plugin is one
     * known inversion, not a general belief about what belongs in that slot. */
    if (*slot != writer) {
        _snprintf_s(line, sizeof(line), _TRUNCATE,
            "slot holds %p, not the writer -- left alone", *slot);
        host->log(2, "ProfileReadFix", line);
        return CH_STATUS_OK;
    }

    if (!VirtualProtect(slot, sizeof(*slot), PAGE_READWRITE, &previous)) {
        host->log(3, "ProfileReadFix", "could not unprotect the slot");
        return CH_STATUS_SYSTEM_ERROR;
    }
    *slot = reader;
    VirtualProtect(slot, sizeof(*slot), previous, &previous);

    _snprintf_s(line, sizeof(line), _TRUNCATE,
        "struct config reads restored: %p -> %p", writer, reader);
    host->log(2, "ProfileReadFix", line);
    return CH_STATUS_OK;
}
