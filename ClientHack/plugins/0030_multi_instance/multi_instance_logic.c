#include "multi_instance_logic.h"

#include <wchar.h>

int ch_multi_instance_is_target_mutex(const wchar_t* name)
{
    return name != NULL
        && (_wcsicmp(name, L"dbefeuate_ccen_khxfor_lcar_blr") == 0
            || _wcsicmp(name, L"NeopleDNFClient") == 0);
}

int ch_multi_instance_format_mutex_name(
    const wchar_t* name,
    unsigned long process_id,
    wchar_t* output,
    size_t output_count)
{
    int written;
    if (name == NULL || output == NULL || output_count == 0) return 0;
    written = _snwprintf_s(
        output,
        output_count,
        _TRUNCATE,
        L"%ls_%lu",
        name,
        process_id);
    if (written < 0) {
        output[0] = L'\0';
        return 0;
    }
    return 1;
}

static const wchar_t* executable_name(const wchar_t* path)
{
    const wchar_t* slash;
    const wchar_t* forward_slash;
    slash = wcsrchr(path, L'\\');
    forward_slash = wcsrchr(path, L'/');
    if (forward_slash != NULL && (slash == NULL || forward_slash > slash)) {
        slash = forward_slash;
    }
    return slash != NULL ? slash + 1 : path;
}

int ch_multi_instance_has_same_executable_name(
    const wchar_t* candidate_path,
    const wchar_t* current_path)
{
    if (candidate_path == NULL || current_path == NULL) return 0;
    return _wcsicmp(
        executable_name(candidate_path),
        executable_name(current_path)) == 0;
}
