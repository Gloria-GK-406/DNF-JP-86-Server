#include "clienthack/endpoint_redirect.h"

#include <stdio.h>
#include <string.h>

int ch_ipv4_address_is_valid(const char* value)
{
    unsigned int octet_count = 0;
    const char* cursor = value;
    if (value == NULL || *value == '\0') return 0;
    while (*cursor != '\0') {
        unsigned int number = 0;
        unsigned int digit_count = 0;
        if (*cursor < '0' || *cursor > '9') return 0;
        while (*cursor >= '0' && *cursor <= '9') {
            if (digit_count != 0u && number == 0u) return 0;
            number = number * 10u + (unsigned int)(*cursor - '0');
            if (++digit_count > 3u || number > 255u) return 0;
            ++cursor;
        }
        if (++octet_count == 4u) return *cursor == '\0';
        if (*cursor != '.') return 0;
        ++cursor;
    }
    return 0;
}

int ch_endpoint_redirect_select(
    int enabled,
    const char* configured_address,
    const char* requested_address,
    char* output,
    size_t output_size)
{
    const char* selected;
    int append_slash = 0;
    int written;
    if (output == NULL || output_size == 0 || requested_address == NULL) return 0;
    selected = requested_address;
    if (enabled && ch_ipv4_address_is_valid(configured_address)) {
        if (strcmp(requested_address, "127.0.0.1") == 0) {
            selected = configured_address;
        } else if (strcmp(requested_address, "127.0.0.1/") == 0) {
            selected = configured_address;
            append_slash = 1;
        }
    }
    written = snprintf(output, output_size, "%s%s", selected, append_slash ? "/" : "");
    return written >= 0 && (size_t)written < output_size && selected == configured_address;
}
