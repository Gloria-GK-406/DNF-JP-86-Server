#include "clienthack/plugin_name.h"

#include <limits.h>
#include <stddef.h>
#include <wctype.h>

static int ch_wide_case_compare(const wchar_t* left, const wchar_t* right)
{
    while (*left != L'\0' && *right != L'\0') {
        const wchar_t a = towlower(*left++);
        const wchar_t b = towlower(*right++);
        if (a < b) return -1;
        if (a > b) return 1;
    }
    return *left == *right ? 0 : (*left == L'\0' ? -1 : 1);
}

int ch_plugin_filename_parse(const wchar_t* filename, ch_plugin_candidate* candidate)
{
    const wchar_t* cursor;
    const wchar_t* extension;
    uint32_t priority = 0;
    size_t length;

    if (filename == NULL || candidate == NULL || filename[0] == L'\0') {
        return 0;
    }
    cursor = filename;
    if (*cursor < L'0' || *cursor > L'9') {
        return 0;
    }
    while (*cursor >= L'0' && *cursor <= L'9') {
        const uint32_t digit = (uint32_t)(*cursor - L'0');
        if (priority > (UINT32_MAX - digit) / 10u) {
            return 0;
        }
        priority = priority * 10u + digit;
        ++cursor;
    }
    if (*cursor++ != L'_' || *cursor == L'\0') {
        return 0;
    }

    length = wcslen(filename);
    if (length >= (sizeof(candidate->filename) / sizeof(candidate->filename[0])) || length < 5) {
        return 0;
    }
    extension = filename + length - 4;
    if (ch_wide_case_compare(extension, L".dll") != 0 || extension == cursor) {
        return 0;
    }

    candidate->priority = priority;
    wmemcpy(candidate->filename, filename, length + 1);
    return 1;
}

int ch_plugin_candidate_compare(const void* left, const void* right)
{
    const ch_plugin_candidate* a = (const ch_plugin_candidate*)left;
    const ch_plugin_candidate* b = (const ch_plugin_candidate*)right;
    if (a->priority < b->priority) return -1;
    if (a->priority > b->priority) return 1;
    return ch_wide_case_compare(a->filename, b->filename);
}
