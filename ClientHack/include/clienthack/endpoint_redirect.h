#ifndef CLIENTHACK_ENDPOINT_REDIRECT_H
#define CLIENTHACK_ENDPOINT_REDIRECT_H

#include <stddef.h>

int ch_ipv4_address_is_valid(const char* value);
int ch_endpoint_redirect_select(
    int enabled,
    const char* configured_address,
    const char* requested_address,
    char* output,
    size_t output_size);

#endif
