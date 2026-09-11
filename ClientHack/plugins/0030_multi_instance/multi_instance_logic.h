#ifndef CLIENTHACK_MULTI_INSTANCE_LOGIC_H
#define CLIENTHACK_MULTI_INSTANCE_LOGIC_H

#include <stddef.h>
#include <wchar.h>

int ch_multi_instance_is_target_mutex(const wchar_t* name);

int ch_multi_instance_format_mutex_name(
    const wchar_t* name,
    unsigned long process_id,
    wchar_t* output,
    size_t output_count);

int ch_multi_instance_has_same_executable_name(
    const wchar_t* candidate_path,
    const wchar_t* current_path);

#endif
