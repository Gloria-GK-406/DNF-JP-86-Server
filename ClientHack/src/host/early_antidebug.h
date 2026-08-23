#ifndef CLIENTHACK_EARLY_ANTIDEBUG_H
#define CLIENTHACK_EARLY_ANTIDEBUG_H

#include <windows.h>

typedef struct ch_early_antidebug_state {
    LONG installed;
    LONG blocked_hide_calls;
    DWORD service_id;
    void* gateway;
    void* fs_transition;
    LONG fs_transition_hooked;
    DWORD install_thread_id;
    LONG hidden_at_install;
    DWORD last_thread_handle;
    DWORD last_service_return;
    DWORD last_caller;
    DWORD monitor_stack[16];
    DWORD hidden_filetime_low;
    DWORD hidden_filetime_high;
    DWORD query_caller;
    BYTE query_code[128];
    LONG instrumentation_status;
    DWORD syscall_return_pcs[64];
    LONG trace_armed;
    LONG trace_captured;
    DWORD transition_eip;
    DWORD transition_esp;
    DWORD transition_stack[8];
    DWORD instruction_pcs[128];
    LONG set_process_returns;
    DWORD set_process_stack[8];
    DWORD pre_native_call_target;
    BYTE pre_native_target_code[64];
} ch_early_antidebug_state;

int ch_early_antidebug_install(void);
int ch_early_antidebug_start_monitor(void);
void ch_early_antidebug_set_debug_handshake(volatile LONG* state);
void ch_early_antidebug_set_native_trace_state(volatile LONG* state);
int ch_early_antidebug_arm_pre_native_call(void);
void ch_early_antidebug_snapshot(ch_early_antidebug_state* state);

#endif
