#include "multi_instance_logic.h"

#include <stdio.h>
#include <wchar.h>

#define CHECK(expression) do { \
    if (!(expression)) { \
        fprintf(stderr, "FAIL %s:%d: %s\n", __FILE__, __LINE__, #expression); \
        return 1; \
    } \
} while (0)

int main(void)
{
    wchar_t name[128];

    CHECK(ch_multi_instance_is_target_mutex(L"dbefeuate_ccen_khxfor_lcar_blr"));
    CHECK(ch_multi_instance_is_target_mutex(L"NEOPLEDNFCLIENT"));
    CHECK(!ch_multi_instance_is_target_mutex(L"unrelated"));
    CHECK(!ch_multi_instance_is_target_mutex(NULL));

    CHECK(ch_multi_instance_format_mutex_name(
        L"NeopleDNFClient", 42ul, name, sizeof(name) / sizeof(name[0])));
    CHECK(wcscmp(name, L"NeopleDNFClient_42") == 0);
    CHECK(!ch_multi_instance_format_mutex_name(L"NeopleDNFClient", 42ul, name, 4));

    CHECK(ch_multi_instance_has_same_executable_name(
        L"C:\\Games\\DNF.exe", L"D:\\ClientA\\dnf.EXE"));
    CHECK(ch_multi_instance_has_same_executable_name(
        L"C:\\Games\\DNF.unpacked-b1-debug.exe",
        L"D:/ClientB/DNF.unpacked-b1-debug.exe"));
    CHECK(!ch_multi_instance_has_same_executable_name(
        L"C:\\Games\\DNF.exe", L"D:\\ClientA\\Launcher.exe"));
    CHECK(!ch_multi_instance_has_same_executable_name(
        NULL, L"D:\\ClientA\\DNF.exe"));
    CHECK(!ch_multi_instance_has_same_executable_name(
        L"C:\\Games\\DNF.exe", NULL));

    puts("multi-instance tests passed");
    return 0;
}
