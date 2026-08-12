#ifndef CLIENTHACK_SHA256_H
#define CLIENTHACK_SHA256_H

#include <stddef.h>
#include <stdint.h>

typedef struct ch_sha256_context {
    uint32_t state[8];
    uint64_t bit_count;
    uint8_t buffer[64];
    size_t buffer_size;
} ch_sha256_context;

void ch_sha256_init(ch_sha256_context* context);
void ch_sha256_update(ch_sha256_context* context, const void* data, size_t length);
void ch_sha256_final(ch_sha256_context* context, uint8_t digest[32]);
void ch_sha256(const void* data, size_t length, uint8_t digest[32]);
int ch_sha256_from_hex(const char* text, uint8_t digest[32]);

#endif
