#include "host_internal.h"

#include "clienthack/abi.h"
#include "clienthack/plugin_name.h"
#include "clienthack/required_plugins.h"

#include <stdlib.h>
#include <stdio.h>
#include <string.h>

static ch_loaded_plugin g_loaded_plugins[CH_MAX_PLUGINS];
static uint32_t g_loaded_plugin_count;

int ch_plugins_shutdown_all(void)
{
    if (g_ch_host_api.patch_rollback_all() != CH_STATUS_OK) return 0;
    while (g_loaded_plugin_count != 0) {
        ch_loaded_plugin* plugin = &g_loaded_plugins[--g_loaded_plugin_count];
        if (plugin->descriptor->shutdown != NULL) plugin->descriptor->shutdown();
        FreeLibrary(plugin->module);
    }
    return 1;
}

static int ch_join_path(wchar_t output[MAX_PATH], const wchar_t* left, const wchar_t* right)
{
    return _snwprintf_s(output, MAX_PATH, _TRUNCATE, L"%s\\%s", left, right) > 0;
}

static void ch_log_plugin_file(const wchar_t* filename, const char* action)
{
    char narrow[MAX_PATH];
    char message[MAX_PATH + 64];
    if (WideCharToMultiByte(CP_UTF8, 0, filename, -1,
        narrow, sizeof(narrow), NULL, NULL) <= 0) {
        ch_log(2, "loader", action);
        return;
    }
    _snprintf_s(message, sizeof(message), _TRUNCATE, "%s: %s", action, narrow);
    ch_log(3, "loader", message);
}

int ch_plugins_load_all(const wchar_t* root_directory)
{
    wchar_t plugin_directory[MAX_PATH];
    wchar_t pattern[MAX_PATH];
    WIN32_FIND_DATAW find_data;
    HANDLE search;
    ch_plugin_candidate candidates[CH_MAX_PLUGINS];
    uint32_t candidate_count = 0;
    uint32_t i;
    int overflow = 0;
    ch_required_plugin_state required_state;
    ch_required_plugin_state_init(&required_state);

    if (!ch_join_path(plugin_directory, root_directory, L"plugins") ||
        !ch_join_path(pattern, plugin_directory, L"*.dll")) return 0;

    search = FindFirstFileW(pattern, &find_data);
    if (search == INVALID_HANDLE_VALUE) {
        ch_log(2, "loader", "plugins directory is missing or empty");
        return 0;
    }
    do {
        if ((find_data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0) {
            ch_plugin_candidate candidate;
            if (ch_plugin_filename_parse(find_data.cFileName, &candidate)) {
                if (candidate_count == CH_MAX_PLUGINS) overflow = 1;
                else candidates[candidate_count++] = candidate;
            }
        }
    } while (FindNextFileW(search, &find_data));
    FindClose(search);
    if (candidate_count == 0 || overflow) return 0;
    qsort(candidates, candidate_count, sizeof(candidates[0]), ch_plugin_candidate_compare);

    for (i = 0; i < candidate_count; ++i) {
        wchar_t full_path[MAX_PATH];
        HMODULE module;
        ch_plugin_query_fn query;
        const ch_plugin_descriptor* descriptor;
        ch_status status;
        int required_identity;
        uint32_t required_index = 0;
        ch_host_checkpoint checkpoint;

        if (!ch_join_path(full_path, plugin_directory, candidates[i].filename)) return 0;
        ch_log_plugin_file(candidates[i].filename, "loading plugin");
        module = LoadLibraryExW(full_path, NULL,
            LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (module == NULL) {
            ch_log(1, "loader", "plugin DLL could not be loaded");
            return 0;
        }
        query = (ch_plugin_query_fn)(void*)GetProcAddress(module, CH_PLUGIN_QUERY_EXPORT);
        descriptor = query != NULL ? query() : NULL;
        if (!ch_plugin_descriptor_is_valid(descriptor)) {
            ch_log(2, "loader", "plugin descriptor rejected");
            FreeLibrary(module);
            if (ch_required_plugin_identify(candidates[i].filename, NULL, 0, NULL) != 0) return 0;
            continue;
        }
        required_identity = ch_required_plugin_identify(candidates[i].filename,
            descriptor->name, descriptor->flags, &required_index);
        if (required_identity < 0) {
            ch_log(1, "loader", "required plugin identity rejected");
            FreeLibrary(module);
            return 0;
        }
        ch_host_checkpoint_capture(&checkpoint);
        status = descriptor->initialize(&g_ch_host_api);
        if (status != CH_STATUS_OK) {
            const int must_succeed = required_identity > 0 ||
                (descriptor->flags & CH_PLUGIN_REQUIRED) != 0;
            const ch_status rollback = ch_host_rollback_to(&checkpoint);
            ch_log(must_succeed ? 0u : 2u, descriptor->name, "plugin initialization failed");
            if (rollback != CH_STATUS_OK) return 0;
            if (descriptor->shutdown != NULL) descriptor->shutdown();
            FreeLibrary(module);
            if (must_succeed) {
                ch_plugins_shutdown_all();
                return 0;
            }
            continue;
        }
        g_loaded_plugins[g_loaded_plugin_count].module = module;
        g_loaded_plugins[g_loaded_plugin_count].descriptor = descriptor;
        ++g_loaded_plugin_count;
        ch_log(3, descriptor->name, "plugin initialized");
        if (required_identity > 0) {
            ch_required_plugin_mark_initialized(&required_state, required_index);
        }
    }
    if (!ch_required_plugins_complete(&required_state)) {
        ch_plugins_shutdown_all();
        return 0;
    }
    return 1;
}
