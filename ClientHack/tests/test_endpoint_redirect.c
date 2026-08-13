#include "clienthack/endpoint_redirect.h"

#include <stdio.h>
#include <string.h>

#define CHECK(expression) do { \
    if (!(expression)) { \
        fprintf(stderr, "FAIL %s:%d: %s\n", __FILE__, __LINE__, #expression); \
        return 1; \
    } \
} while (0)

int main(void)
{
    char output[32];
    CHECK(ch_ipv4_address_is_valid("127.0.0.1"));
    CHECK(ch_ipv4_address_is_valid("255.255.255.255"));
    CHECK(!ch_ipv4_address_is_valid("256.1.1.1"));
    CHECK(!ch_ipv4_address_is_valid("127.000.0.1"));
    CHECK(!ch_ipv4_address_is_valid("127.0.0"));
    CHECK(!ch_ipv4_address_is_valid("127.0.0.1/"));
    CHECK(ch_endpoint_redirect_select(1, "192.168.1.20", "127.0.0.1",
        output, sizeof(output)));
    CHECK(strcmp(output, "192.168.1.20") == 0);
    CHECK(ch_endpoint_redirect_select(1, "192.168.1.20", "127.0.0.1/",
        output, sizeof(output)));
    CHECK(strcmp(output, "192.168.1.20/") == 0);
    CHECK(!ch_endpoint_redirect_select(1, "192.168.1.20", "8.8.8.8",
        output, sizeof(output)));
    CHECK(strcmp(output, "8.8.8.8") == 0);
    CHECK(!ch_endpoint_redirect_select(0, "192.168.1.20", "127.0.0.1",
        output, sizeof(output)));
    CHECK(strcmp(output, "127.0.0.1") == 0);
    CHECK(!ch_endpoint_redirect_select(1, "invalid", "127.0.0.1",
        output, sizeof(output)));
    CHECK(strcmp(output, "127.0.0.1") == 0);
    puts("endpoint redirect tests passed");
    return 0;
}
