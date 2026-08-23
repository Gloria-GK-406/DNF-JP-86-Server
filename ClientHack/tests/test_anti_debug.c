#include <stdio.h>
#include <string.h>

/* Include the plugin implementation so this focused test exercises the actual
 * static detours without loading DNF or installing process-wide hooks. */
#include "../plugins/0005_anti_anti_debug/anti_anti_debug.c"

static DWORD g_flags_seen_by_real;
static int g_failures;

#define CHECK(expression) do { \
    if (!(expression)) { \
        fprintf(stderr, "FAIL %s:%d: %s\n", __FILE__, __LINE__, #expression); \
        ++g_failures; \
    } \
} while (0)

static NTSTATUS_ NTAPI fake_get_context(HANDLE thread, PCONTEXT ctx)
{
    (void)thread;
    g_flags_seen_by_real = ctx->ContextFlags;
    ctx->Dr0 = 1;
    ctx->Dr1 = 2;
    ctx->Dr2 = 3;
    ctx->Dr3 = 4;
    ctx->Dr6 = 6;
    ctx->Dr7 = 7;
    return CH_STATUS_SUCCESS_NT;
}

static NTSTATUS_ NTAPI fake_set_context(HANDLE thread, PCONTEXT ctx)
{
    (void)thread;
    g_flags_seen_by_real = ctx->ContextFlags;
    return CH_STATUS_SUCCESS_NT;
}

static void test_get_context_hides_debug_registers_without_mutating_flags(void)
{
    CONTEXT ctx;
    DWORD original_flags = CONTEXT_FULL | CONTEXT_DEBUG_REGISTERS;
    memset(&ctx, 0, sizeof(ctx));
    ctx.ContextFlags = original_flags;
    g_real_get_context = fake_get_context;
    g_flags_seen_by_real = 0;

    CHECK(ch_hook_get_context((HANDLE)(LONG_PTR)-2, &ctx) == CH_STATUS_SUCCESS_NT);
    CHECK(g_flags_seen_by_real == (original_flags & ~CONTEXT_DEBUG_REGISTERS));
    CHECK(ctx.ContextFlags == original_flags);
    CHECK(ctx.Dr0 == 0 && ctx.Dr1 == 0 && ctx.Dr2 == 0 && ctx.Dr3 == 0);
    CHECK(ctx.Dr6 == 0 && ctx.Dr7 == 0);
}

static void test_set_context_blocks_debug_register_write_without_mutating_caller(void)
{
    CONTEXT ctx;
    DWORD original_flags = CONTEXT_FULL | CONTEXT_DEBUG_REGISTERS;
    memset(&ctx, 0, sizeof(ctx));
    ctx.ContextFlags = original_flags;
    g_real_set_context = fake_set_context;
    g_flags_seen_by_real = 0;

    CHECK(ch_hook_set_context((HANDLE)(LONG_PTR)-2, &ctx) == CH_STATUS_SUCCESS_NT);
    CHECK(g_flags_seen_by_real == (original_flags & ~CONTEXT_DEBUG_REGISTERS));
    CHECK(ctx.ContextFlags == original_flags);
}

static void test_only_target_serializer_single_step_is_resumed(void)
{
    ch_host_api host;
    EXCEPTION_RECORD record;
    CONTEXT ctx;
    const uintptr_t dnf_base = 0x00400000u;
    const uintptr_t target = dnf_base + 0x01CD1120u;

    memset(&host, 0, sizeof(host));
    memset(&record, 0, sizeof(record));
    memset(&ctx, 0, sizeof(ctx));
    host.dnf_base = dnf_base;
    g_host = &host;
    record.ExceptionCode = STATUS_SINGLE_STEP;
    record.ExceptionAddress = (PVOID)target;
    ctx.Eip = (DWORD)target;
    ctx.EFlags = 0x00000202u;

    CHECK(ch_prepare_target_single_step_resume(&record, &ctx));
    CHECK(ctx.EFlags == 0x00010202u);

    ctx.EFlags = 0x00000202u;
    record.ExceptionAddress = (PVOID)(target + 1u);
    CHECK(!ch_prepare_target_single_step_resume(&record, &ctx));
    CHECK(ctx.EFlags == 0x00000202u);

    record.ExceptionAddress = (PVOID)target;
    record.ExceptionCode = STATUS_BREAKPOINT;
    CHECK(!ch_prepare_target_single_step_resume(&record, &ctx));
    CHECK(ctx.EFlags == 0x00000202u);

    g_host = NULL;
}

static void test_wow64_gateway_decoder_and_hide_filter_are_exact(void)
{
    const uint8_t stub[] = {
        0xB8, 0x0D, 0x00, 0x00, 0x00,
        0xBA, 0x78, 0x56, 0x34, 0x12,
        0xFF, 0xD2
    };
    DWORD service_id = 0;
    void* gateway = NULL;
    DWORD frame[6];

    memset(frame, 0, sizeof(frame));
    CHECK(ch_decode_wow64_syscall_stub(stub, &service_id, &gateway));
    CHECK(service_id == 0x0Du);
    CHECK((uintptr_t)gateway == 0x12345678u);

    g_nt_set_info_service = service_id;
    frame[1] = (DWORD)(uintptr_t)(HANDLE)(LONG_PTR)-2;
    frame[2] = CH_ThreadHideFromDebugger;
    frame[4] = 0;
    CHECK(ch_should_block_wow64_hide(service_id, frame));

    CHECK(!ch_should_block_wow64_hide(service_id + 1u, frame));
    frame[2] = CH_ThreadHideFromDebugger + 1u;
    CHECK(!ch_should_block_wow64_hide(service_id, frame));
    frame[2] = CH_ThreadHideFromDebugger;
    frame[4] = sizeof(DWORD);
    CHECK(!ch_should_block_wow64_hide(service_id, frame));

    CHECK(!ch_decode_wow64_syscall_stub(stub + 1, &service_id, &gateway));
}

int main(void)
{
    test_get_context_hides_debug_registers_without_mutating_flags();
    test_set_context_blocks_debug_register_write_without_mutating_caller();
    test_only_target_serializer_single_step_is_resumed();
    test_wow64_gateway_decoder_and_hide_filter_are_exact();
    if (g_failures != 0) {
        fprintf(stderr, "%d anti-debug assertion(s) failed\n", g_failures);
        return 1;
    }
    puts("anti-debug context tests passed");
    return 0;
}
