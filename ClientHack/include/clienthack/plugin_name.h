#ifndef CLIENTHACK_PLUGIN_NAME_H
#define CLIENTHACK_PLUGIN_NAME_H

#include <stdint.h>
#include <wchar.h>

typedef struct ch_plugin_candidate {
    uint32_t priority;
    wchar_t filename[260];
} ch_plugin_candidate;

int ch_plugin_filename_parse(const wchar_t* filename, ch_plugin_candidate* candidate);
int ch_plugin_candidate_compare(const void* left, const void* right);

#endif
