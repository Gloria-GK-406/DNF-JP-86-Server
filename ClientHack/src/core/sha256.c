#include "clienthack/sha256.h"

#include <string.h>

static const uint32_t ch_sha256_round_constants[64] = {
    0x428A2F98u, 0x71374491u, 0xB5C0FBCFu, 0xE9B5DBA5u,
    0x3956C25Bu, 0x59F111F1u, 0x923F82A4u, 0xAB1C5ED5u,
    0xD807AA98u, 0x12835B01u, 0x243185BEu, 0x550C7DC3u,
    0x72BE5D74u, 0x80DEB1FEu, 0x9BDC06A7u, 0xC19BF174u,
    0xE49B69C1u, 0xEFBE4786u, 0x0FC19DC6u, 0x240CA1CCu,
    0x2DE92C6Fu, 0x4A7484AAu, 0x5CB0A9DCu, 0x76F988DAu,
    0x983E5152u, 0xA831C66Du, 0xB00327C8u, 0xBF597FC7u,
    0xC6E00BF3u, 0xD5A79147u, 0x06CA6351u, 0x14292967u,
    0x27B70A85u, 0x2E1B2138u, 0x4D2C6DFCu, 0x53380D13u,
    0x650A7354u, 0x766A0ABBu, 0x81C2C92Eu, 0x92722C85u,
    0xA2BFE8A1u, 0xA81A664Bu, 0xC24B8B70u, 0xC76C51A3u,
    0xD192E819u, 0xD6990624u, 0xF40E3585u, 0x106AA070u,
    0x19A4C116u, 0x1E376C08u, 0x2748774Cu, 0x34B0BCB5u,
    0x391C0CB3u, 0x4ED8AA4Au, 0x5B9CCA4Fu, 0x682E6FF3u,
    0x748F82EEu, 0x78A5636Fu, 0x84C87814u, 0x8CC70208u,
    0x90BEFFFAu, 0xA4506CEBu, 0xBEF9A3F7u, 0xC67178F2u
};

static uint32_t ch_rotr32(uint32_t value, unsigned count)
{
    return (value >> count) | (value << (32u - count));
}

static void ch_sha256_transform(ch_sha256_context* context, const uint8_t block[64])
{
    uint32_t words[64];
    uint32_t a, b, c, d, e, f, g, h;
    unsigned i;

    for (i = 0; i < 16; ++i) {
        words[i] = ((uint32_t)block[i * 4] << 24) |
            ((uint32_t)block[i * 4 + 1] << 16) |
            ((uint32_t)block[i * 4 + 2] << 8) |
            (uint32_t)block[i * 4 + 3];
    }
    for (i = 16; i < 64; ++i) {
        const uint32_t s0 = ch_rotr32(words[i - 15], 7) ^
            ch_rotr32(words[i - 15], 18) ^ (words[i - 15] >> 3);
        const uint32_t s1 = ch_rotr32(words[i - 2], 17) ^
            ch_rotr32(words[i - 2], 19) ^ (words[i - 2] >> 10);
        words[i] = words[i - 16] + s0 + words[i - 7] + s1;
    }

    a = context->state[0]; b = context->state[1];
    c = context->state[2]; d = context->state[3];
    e = context->state[4]; f = context->state[5];
    g = context->state[6]; h = context->state[7];
    for (i = 0; i < 64; ++i) {
        const uint32_t sum1 = ch_rotr32(e, 6) ^ ch_rotr32(e, 11) ^ ch_rotr32(e, 25);
        const uint32_t choice = (e & f) ^ ((~e) & g);
        const uint32_t temp1 = h + sum1 + choice + ch_sha256_round_constants[i] + words[i];
        const uint32_t sum0 = ch_rotr32(a, 2) ^ ch_rotr32(a, 13) ^ ch_rotr32(a, 22);
        const uint32_t majority = (a & b) ^ (a & c) ^ (b & c);
        const uint32_t temp2 = sum0 + majority;
        h = g; g = f; f = e; e = d + temp1;
        d = c; c = b; b = a; a = temp1 + temp2;
    }

    context->state[0] += a; context->state[1] += b;
    context->state[2] += c; context->state[3] += d;
    context->state[4] += e; context->state[5] += f;
    context->state[6] += g; context->state[7] += h;
}

void ch_sha256_init(ch_sha256_context* context)
{
    memset(context, 0, sizeof(*context));
    context->state[0] = 0x6A09E667u;
    context->state[1] = 0xBB67AE85u;
    context->state[2] = 0x3C6EF372u;
    context->state[3] = 0xA54FF53Au;
    context->state[4] = 0x510E527Fu;
    context->state[5] = 0x9B05688Cu;
    context->state[6] = 0x1F83D9ABu;
    context->state[7] = 0x5BE0CD19u;
}

void ch_sha256_update(ch_sha256_context* context, const void* data, size_t length)
{
    const uint8_t* input = (const uint8_t*)data;
    size_t copied;
    if (context == NULL || (input == NULL && length != 0)) return;
    context->bit_count += (uint64_t)length * 8u;
    while (length != 0) {
        copied = 64 - context->buffer_size;
        if (copied > length) copied = length;
        memcpy(context->buffer + context->buffer_size, input, copied);
        context->buffer_size += copied;
        input += copied;
        length -= copied;
        if (context->buffer_size == 64) {
            ch_sha256_transform(context, context->buffer);
            context->buffer_size = 0;
        }
    }
}

void ch_sha256_final(ch_sha256_context* context, uint8_t digest[32])
{
    uint64_t bit_count;
    unsigned i;
    if (context == NULL || digest == NULL) return;
    bit_count = context->bit_count;
    context->buffer[context->buffer_size++] = 0x80;
    if (context->buffer_size > 56) {
        memset(context->buffer + context->buffer_size, 0, 64 - context->buffer_size);
        ch_sha256_transform(context, context->buffer);
        context->buffer_size = 0;
    }
    memset(context->buffer + context->buffer_size, 0, 56 - context->buffer_size);
    for (i = 0; i < 8; ++i) {
        context->buffer[63 - i] = (uint8_t)(bit_count >> (i * 8));
    }
    ch_sha256_transform(context, context->buffer);
    for (i = 0; i < 8; ++i) {
        digest[i * 4] = (uint8_t)(context->state[i] >> 24);
        digest[i * 4 + 1] = (uint8_t)(context->state[i] >> 16);
        digest[i * 4 + 2] = (uint8_t)(context->state[i] >> 8);
        digest[i * 4 + 3] = (uint8_t)context->state[i];
    }
    memset(context, 0, sizeof(*context));
}

void ch_sha256(const void* data, size_t length, uint8_t digest[32])
{
    ch_sha256_context context;
    ch_sha256_init(&context);
    ch_sha256_update(&context, data, length);
    ch_sha256_final(&context, digest);
}

int ch_sha256_from_hex(const char* text, uint8_t digest[32])
{
    size_t i;
    if (text == NULL || digest == NULL || strlen(text) != 64) return 0;
    for (i = 0; i < 32; ++i) {
        unsigned value = 0;
        unsigned nibble;
        size_t part;
        for (part = 0; part < 2; ++part) {
            const char c = text[i * 2 + part];
            if (c >= '0' && c <= '9') nibble = (unsigned)(c - '0');
            else if (c >= 'a' && c <= 'f') nibble = (unsigned)(c - 'a' + 10);
            else if (c >= 'A' && c <= 'F') nibble = (unsigned)(c - 'A' + 10);
            else return 0;
            value = value * 16u + nibble;
        }
        digest[i] = (uint8_t)value;
    }
    return 1;
}
