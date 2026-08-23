#ifndef CLIENTHACK_CLIENT_PROFILE_H
#define CLIENTHACK_CLIENT_PROFILE_H

/* Which packed DNF.exe this build of Az targets.
 *
 * Every address here is a property of one specific client binary, so an Az.dll
 * built for one client must never be dropped next to another: the startup gate
 * would patch whatever happens to sit at the S4A12 cookie address, which on
 * S4A21 is unrelated code. Selecting the profile at COMPILE time rather than
 * probing at run time is deliberate -- a wrong guess here corrupts the client
 * before any log line is written, so it should be a build error, not a runtime
 * surprise. The two builds therefore produce two Az.dll files, one per client
 * folder.
 *
 * Default is S4A12; define CLIENTHACK_CLIENT_A21 to target S4A21.
 *
 * Provenance of the S4A21 values:
 *   PE header fields  -- read from DNFClient-A21-debug\DNF.exe
 *   cookie RVA        -- upstream S4A21ClientPatch/az/az.cpp, `#define XCookie
 *                        0x02C865DE` (a VA; RVA = VA - 0x00400000), together
 *                        with the same five-byte `8B FF 55 8B EC` prologue the
 *                        S4A12 gate matches on
 *   IAT table         -- upstream S4A21ClientPatch/az/iat_table.h, 420 slots
 */

#if defined(CLIENTHACK_CLIENT_A21)

#define CH_CLIENT_NAME "S4A21"
#define CH_CLIENT_NAME_W L"S4A21"
#define CH_CLIENT_PREFERRED_BASE 0x00400000u
#define CH_CLIENT_SIZE_OF_IMAGE 0x0746E000u
#define CH_CLIENT_ENTRYPOINT_RVA 0x07213FBCu
#define CH_CLIENT_TIMESTAMP 0x58458972u
#define CH_CLIENT_SECTION_COUNT 15u
/* ___security_init_cookie: the point at which the protector has finished
 * unpacking and the client's own code is about to run. */
#define CH_CLIENT_COOKIE_RVA 0x028865DEu
#define CH_CLIENT_SHA256_BYTES { \
    0x1F, 0x82, 0x44, 0xD3, 0xF7, 0x30, 0x9B, 0xBE, \
    0x2F, 0xDE, 0x42, 0x99, 0xE1, 0x86, 0x3A, 0x89, \
    0x4F, 0x6B, 0x28, 0xD5, 0x70, 0x7B, 0xF0, 0x17, \
    0x32, 0x98, 0x05, 0x85, 0xB2, 0x0B, 0x00, 0xDD }
#define CH_CLIENT_SHA256_TEXT \
    L"1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD"

/* Not yet measured on this client. The feature-ready wait and the dump's
 * protected-call anchor are extra identity checks layered on top of the SHA-256
 * gate, so a build without them is weaker but not wrong -- and inventing an
 * address would be worse than admitting the gap. */
/* #define CH_CLIENT_HAS_FEATURE_ANCHORS 1 */
/* #define CH_CLIENT_PROTECTED_CALL_RVA ... */

#else

#define CH_CLIENT_NAME "S4A12"
#define CH_CLIENT_NAME_W L"S4A12"
#define CH_CLIENT_PREFERRED_BASE 0x00400000u
#define CH_CLIENT_SIZE_OF_IMAGE 0x06693000u
#define CH_CLIENT_ENTRYPOINT_RVA 0x06176731u
#define CH_CLIENT_TIMESTAMP 0x5601674Eu
#define CH_CLIENT_SECTION_COUNT 15u
#define CH_CLIENT_COOKIE_RVA 0x0213BBBFu
#define CH_CLIENT_PROTECTED_CALL_RVA 0x04ECD542u
#define CH_CLIENT_HAS_FEATURE_ANCHORS 1
#define CH_CLIENT_SHA256_BYTES { \
    0xF7, 0x8F, 0x0A, 0xEA, 0x51, 0x37, 0xDF, 0x06, \
    0xB6, 0x3E, 0xD0, 0xF7, 0x47, 0x0B, 0x66, 0xF3, \
    0xA2, 0x4D, 0x06, 0x1B, 0x69, 0xD4, 0xDA, 0x13, \
    0x75, 0x8E, 0x65, 0x57, 0x4A, 0x7F, 0xC0, 0x4C }
#define CH_CLIENT_SHA256_TEXT \
    L"F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C"

#endif

/* The five bytes the cookie function starts with. Identical on both clients --
 * upstream patches the same sequence at the S4A21 address -- so the startup
 * gate's signature test needs no per-client form. */
#define CH_CLIENT_COOKIE_PROLOGUE { 0x8B, 0xFF, 0x55, 0x8B, 0xEC }

#endif
