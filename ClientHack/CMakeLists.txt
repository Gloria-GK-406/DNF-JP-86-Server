cmake_minimum_required(VERSION 3.25)

project(ClientHack VERSION 0.1.0 LANGUAGES C)

option(CLIENTHACK_PROTECT_DRX "Hide hardware debug registers from the client" ON)

if(NOT WIN32)
    message(FATAL_ERROR "ClientHack supports Windows only")
endif()

if(NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
    message(FATAL_ERROR "ClientHack must be configured for Win32/x86")
endif()

set(CMAKE_C_STANDARD 17)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

set(CLIENTHACK_STAGE_DIR "${CMAKE_BINARY_DIR}/stage")

function(clienthack_warnings target)
    target_compile_definitions(${target} PRIVATE WIN32_LEAN_AND_MEAN)
    if(MSVC)
        target_compile_options(${target} PRIVATE /W4 /utf-8)
        target_compile_definitions(${target} PRIVATE _CRT_SECURE_NO_WARNINGS)
    elseif(CMAKE_C_COMPILER_ID MATCHES "GNU|Clang")
        target_compile_options(${target} PRIVATE -Wall -Wextra -Werror)
    endif()
endfunction()

add_library(clienthack_core STATIC
    src/core/abi.c
    src/core/endpoint_redirect.c
    src/core/gate.c
    src/core/iat_state.c
    src/core/patch_state.c
    src/core/plugin_name.c
    src/core/required_plugins.c
    src/core/sha256.c
    src/core/startup.c)
target_include_directories(clienthack_core PUBLIC include)
clienthack_warnings(clienthack_core)

# The S4A21 core. Identical sources; the client profile changes compile-time
# data inside them (most visibly the required-plugin roster), so an S4A21 host
# must not link the S4A12 core object files.
add_library(clienthack_core_a21 STATIC
    src/core/abi.c
    src/core/endpoint_redirect.c
    src/core/gate.c
    src/core/iat_state.c
    src/core/patch_state.c
    src/core/plugin_name.c
    src/core/required_plugins.c
    src/core/sha256.c
    src/core/startup.c)
target_include_directories(clienthack_core_a21 PUBLIC include)
target_compile_definitions(clienthack_core_a21 PUBLIC CLIENTHACK_CLIENT_A21=1)
clienthack_warnings(clienthack_core_a21)

enable_testing()
find_program(CLIENTHACK_POWERSHELL_EXECUTABLE NAMES powershell pwsh REQUIRED)
find_package(Python3 COMPONENTS Interpreter REQUIRED)
add_test(NAME clienthack_capture_evidence
    COMMAND "${CLIENTHACK_POWERSHELL_EXECUTABLE}"
        -NoProfile -ExecutionPolicy Bypass
        -File "${CMAKE_CURRENT_SOURCE_DIR}/tools/compare-captures.ps1"
        -First "${CMAKE_CURRENT_SOURCE_DIR}/runtime/captures/capture-1.jsonl"
        -Second "${CMAKE_CURRENT_SOURCE_DIR}/runtime/captures/capture-2.jsonl")
add_executable(clienthack_tests tests/test_main.c)
target_link_libraries(clienthack_tests PRIVATE clienthack_core)
target_include_directories(clienthack_tests PRIVATE src/az)
clienthack_warnings(clienthack_tests)
add_test(NAME clienthack_tests COMMAND clienthack_tests)

add_executable(clienthack_endpoint_redirect_tests tests/test_endpoint_redirect.c)
target_link_libraries(clienthack_endpoint_redirect_tests PRIVATE clienthack_core)
clienthack_warnings(clienthack_endpoint_redirect_tests)
add_test(NAME clienthack_endpoint_redirect_tests COMMAND clienthack_endpoint_redirect_tests)

add_executable(clienthack_anti_debug_tests tests/test_anti_debug.c)
target_include_directories(clienthack_anti_debug_tests PRIVATE include plugins)
clienthack_warnings(clienthack_anti_debug_tests)
add_test(NAME clienthack_anti_debug_tests COMMAND clienthack_anti_debug_tests)
add_test(NAME clienthack_rebuild_analysis_pe_tests
    COMMAND "${Python3_EXECUTABLE}"
        "${CMAKE_CURRENT_SOURCE_DIR}/tests/test_rebuild_analysis_pe.py" -v)

add_library(minhook STATIC
    third_party/minhook/src/buffer.c
    third_party/minhook/src/hook.c
    third_party/minhook/src/trampoline.c
    third_party/minhook/src/hde/hde32.c)
target_include_directories(minhook PUBLIC third_party/minhook/include)
target_include_directories(minhook PRIVATE third_party/minhook/src third_party/minhook/src/hde)
target_compile_definitions(minhook PRIVATE WIN32_LEAN_AND_MEAN)

add_library(Az SHARED
    src/az/az_exports.c
    src/az/iat_repair.c
    src/az/Az.def
    src/host/file_hash.c
    src/host/early_antidebug.c
    src/host/host_main.c
    src/host/host_services.c
    src/host/plugin_loader.c
    src/host/readiness.c
    src/host/startup_trace.c)
target_include_directories(Az PRIVATE include src/host)
target_link_libraries(Az PRIVATE clienthack_core minhook user32)
clienthack_warnings(Az)
set_target_properties(Az PROPERTIES
    PREFIX ""
    OUTPUT_NAME "Az"
    RUNTIME_OUTPUT_DIRECTORY "${CLIENTHACK_STAGE_DIR}"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CLIENTHACK_STAGE_DIR}"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CLIENTHACK_STAGE_DIR}")

# The S4A21 host. Same sources, different client profile -- see
# clienthack/client_profile.h for why the client is a compile-time choice and
# not a runtime probe. Staged separately so the two Az.dll files can never be
# confused for one another.
set(CLIENTHACK_STAGE_DIR_A21 "${CMAKE_BINARY_DIR}/stage-a21")
add_library(Az_a21 SHARED
    src/az/az_exports.c
    src/az/iat_repair.c
    src/az/Az.def
    src/host/file_hash.c
    src/host/early_antidebug.c
    src/host/host_main.c
    src/host/host_services.c
    src/host/plugin_loader.c
    src/host/readiness.c
    src/host/startup_trace.c)
target_include_directories(Az_a21 PRIVATE include src/host)
target_compile_definitions(Az_a21 PRIVATE CLIENTHACK_CLIENT_A21=1)
target_link_libraries(Az_a21 PRIVATE clienthack_core_a21 minhook user32)
clienthack_warnings(Az_a21)
set_target_properties(Az_a21 PROPERTIES
    PREFIX ""
    OUTPUT_NAME "Az"
    RUNTIME_OUTPUT_DIRECTORY "${CLIENTHACK_STAGE_DIR_A21}"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CLIENTHACK_STAGE_DIR_A21}"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CLIENTHACK_STAGE_DIR_A21}")

add_library(AzCapture SHARED
    src/capture/capture.c
    src/az/Az.def)
target_include_directories(AzCapture PRIVATE include src/az)
target_link_libraries(AzCapture PRIVATE clienthack_core user32)
clienthack_warnings(AzCapture)
set_target_properties(AzCapture PROPERTIES
    PREFIX ""
    OUTPUT_NAME "Az"
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/capture"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_BINARY_DIR}/capture"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_BINARY_DIR}/capture")

add_library(ijl15 SHARED src/ijl/ijl_stub.c src/ijl/ijl15.def)
clienthack_warnings(ijl15)
set_target_properties(ijl15 PROPERTIES
    PREFIX ""
    OUTPUT_NAME "ijl15"
    RUNTIME_OUTPUT_DIRECTORY "${CLIENTHACK_STAGE_DIR}"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CLIENTHACK_STAGE_DIR}"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CLIENTHACK_STAGE_DIR}")

function(clienthack_plugin target source output_name descriptor_name)
    add_library(${target} SHARED
        ${source}
        plugins/plugin_entry.c
        plugins/plugin.def)
    target_include_directories(${target} PRIVATE include plugins)
    target_compile_definitions(${target} PRIVATE
        CLIENTHACK_PLUGIN_NAME="${descriptor_name}"
        CLIENTHACK_PLUGIN_FLAGS=CH_PLUGIN_REQUIRED)
    clienthack_warnings(${target})
    set_target_properties(${target} PROPERTIES
        PREFIX ""
        OUTPUT_NAME "${output_name}"
        RUNTIME_OUTPUT_DIRECTORY "${CLIENTHACK_STAGE_DIR}/plugins"
        RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CLIENTHACK_STAGE_DIR}/plugins"
        RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CLIENTHACK_STAGE_DIR}/plugins")
endfunction()

function(clienthack_plugin_a21 target source output_name descriptor_name)
    add_library(${target} SHARED
        ${source}
        plugins/plugin_entry.c
        plugins/plugin.def)
    target_include_directories(${target} PRIVATE include plugins)
    target_compile_definitions(${target} PRIVATE
        CLIENTHACK_CLIENT_A21=1
        CLIENTHACK_PLUGIN_NAME="${descriptor_name}"
        CLIENTHACK_PLUGIN_FLAGS=CH_PLUGIN_REQUIRED)
    clienthack_warnings(${target})
    set_target_properties(${target} PROPERTIES
        PREFIX ""
        OUTPUT_NAME "${output_name}"
        RUNTIME_OUTPUT_DIRECTORY "${CLIENTHACK_STAGE_DIR_A21}/plugins"
        RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CLIENTHACK_STAGE_DIR_A21}/plugins"
        RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CLIENTHACK_STAGE_DIR_A21}/plugins")
endfunction()

add_library(plugin_memory_dump SHARED EXCLUDE_FROM_ALL
    plugins/0000_memory_dump/memory_dump.c
    plugins/plugin_entry.c
    plugins/plugin.def)
target_include_directories(plugin_memory_dump PRIVATE include plugins src/az)
target_compile_definitions(plugin_memory_dump PRIVATE
    CLIENTHACK_PLUGIN_NAME="MemoryDump"
    CLIENTHACK_PLUGIN_FLAGS=0u)
clienthack_warnings(plugin_memory_dump)
set_target_properties(plugin_memory_dump PROPERTIES
    PREFIX ""
    OUTPUT_NAME "0000_MemoryDump"
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/diagnostics/plugins"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_BINARY_DIR}/diagnostics/plugins"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_BINARY_DIR}/diagnostics/plugins")

add_custom_target(clienthack_memory_dump DEPENDS plugin_memory_dump)

# The S4A21 dump plugin. Same source, different client profile: the identity
# addresses and the IAT table both come from clienthack/client_profile.h, so
# this target differs from the S4A12 one only by CLIENTHACK_CLIENT_A21.
#
# It is a SEPARATE binary rather than a runtime switch on purpose -- the startup
# gate patches five bytes at a hardcoded address, and getting that address wrong
# corrupts the client before a single log line exists. A build error is the right
# failure mode for that; a runtime misdetection is not.
add_library(plugin_memory_dump_a21 SHARED EXCLUDE_FROM_ALL
    plugins/0000_memory_dump/memory_dump.c
    plugins/plugin_entry.c
    plugins/plugin.def)
target_include_directories(plugin_memory_dump_a21 PRIVATE include plugins src/az)
target_compile_definitions(plugin_memory_dump_a21 PRIVATE
    CLIENTHACK_PLUGIN_NAME="MemoryDump"
    CLIENTHACK_PLUGIN_FLAGS=0u
    CLIENTHACK_CLIENT_A21=1)
clienthack_warnings(plugin_memory_dump_a21)
set_target_properties(plugin_memory_dump_a21 PROPERTIES
    PREFIX ""
    OUTPUT_NAME "0000_MemoryDump"
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/diagnostics-a21/plugins"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_BINARY_DIR}/diagnostics-a21/plugins"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_BINARY_DIR}/diagnostics-a21/plugins")

add_custom_target(clienthack_memory_dump_a21 DEPENDS plugin_memory_dump_a21)

# Diagnostic, debug-branch only, and deliberately NOT registered in
# required_plugins.c: it is a measurement instrument, so a failure to arm must
# leave the client startable rather than abort the host. Off unless
# [PageGuardTrace] Enabled=1, which needs CLIENTHACK_PLUGIN_FLAGS=0u.
add_library(plugin_page_guard_trace SHARED
    plugins/0001_page_guard_trace/page_guard_trace.c
    plugins/plugin_entry.c
    plugins/plugin.def)
target_include_directories(plugin_page_guard_trace PRIVATE include plugins)
target_compile_definitions(plugin_page_guard_trace PRIVATE
    CLIENTHACK_PLUGIN_NAME="PageGuardTrace"
    CLIENTHACK_PLUGIN_FLAGS=0u
    CLIENTHACK_PLUGIN_HAS_SHUTDOWN=1)
clienthack_warnings(plugin_page_guard_trace)
set_target_properties(plugin_page_guard_trace PROPERTIES
    PREFIX ""
    OUTPUT_NAME "0001_PageGuardTrace"
    RUNTIME_OUTPUT_DIRECTORY "${CLIENTHACK_STAGE_DIR}/plugins"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CLIENTHACK_STAGE_DIR}/plugins"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CLIENTHACK_STAGE_DIR}/plugins")

clienthack_plugin(plugin_anti_anti_debug
    plugins/0005_anti_anti_debug/anti_anti_debug.c
    "0005_AntiAntiDebug" "AntiAntiDebug")
if(CLIENTHACK_PROTECT_DRX)
    target_compile_definitions(plugin_anti_anti_debug PRIVATE CLIENTHACK_PROTECT_DRX=1)
else()
    target_compile_definitions(plugin_anti_anti_debug PRIVATE CLIENTHACK_PROTECT_DRX=0)
endif()
# Debug-branch only, and deliberately NOT registered in required_plugins.c: the
# guard is a mitigation for the B1 replay's startup ordering hole, so a signature
# mismatch must leave the client startable (unguarded) instead of aborting the
# host. That needs CLIENTHACK_PLUGIN_FLAGS=0u, which clienthack_plugin() hardcodes
# to CH_PLUGIN_REQUIRED -- hence the explicit target.
add_library(plugin_b1_startup_guard SHARED
    plugins/0006_b1_startup_guard/b1_startup_guard.c
    plugins/plugin_entry.c
    plugins/plugin.def)
target_include_directories(plugin_b1_startup_guard PRIVATE include plugins)
target_compile_definitions(plugin_b1_startup_guard PRIVATE
    CLIENTHACK_PLUGIN_NAME="B1StartupGuard"
    CLIENTHACK_PLUGIN_FLAGS=0u
    CLIENTHACK_PLUGIN_HAS_SHUTDOWN=1)
clienthack_warnings(plugin_b1_startup_guard)
set_target_properties(plugin_b1_startup_guard PROPERTIES
    PREFIX ""
    OUTPUT_NAME "0006_B1StartupGuard"
    RUNTIME_OUTPUT_DIRECTORY "${CLIENTHACK_STAGE_DIR}/plugins"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CLIENTHACK_STAGE_DIR}/plugins"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CLIENTHACK_STAGE_DIR}/plugins")

clienthack_plugin(plugin_disable_gameguard
    plugins/0010_disable_gameguard/disable_gameguard.c
    "0010_DisableGameGuard" "DisableGameGuard")
clienthack_plugin(plugin_client_compatibility
    plugins/0020_client_compatibility/client_compatibility.c
    "0020_ClientCompatibility" "ClientCompatibility")
# S4A21 variants: the same source, compiled against the other client's patch
# table. Two DLLs rather than one that probes at run time, for the reason the
# client profile exists at all -- a patch table is a list of absolute addresses,
# and applying the wrong one writes over unrelated code.
clienthack_plugin_a21(plugin_disable_gameguard_a21
    plugins/0010_disable_gameguard/disable_gameguard.c
    "0010_DisableGameGuard" "DisableGameGuard")
clienthack_plugin_a21(plugin_client_compatibility_a21
    plugins/0020_client_compatibility/client_compatibility.c
    "0020_ClientCompatibility" "ClientCompatibility")
clienthack_plugin_a21(plugin_a21_runtime_fix
    plugins/0050_a21_runtime_fix/a21_runtime_fix.c
    "0050_A21RuntimeFix" "A21RuntimeFix")
# Diagnostic, and deliberately NOT required: the recorder exists to explain a
# failure, so it must never become one. That needs FLAGS=0, which
# clienthack_plugin_a21() hardcodes to CH_PLUGIN_REQUIRED -- hence the explicit
# target. Client-agnostic source; only the S4A21 build is produced because
# S4A12 already carries the same recorder inside 0006_B1StartupGuard.
add_library(plugin_fault_recorder_a21 SHARED
    plugins/0007_fault_recorder/fault_recorder.c
    plugins/plugin_entry.c
    plugins/plugin.def)
target_include_directories(plugin_fault_recorder_a21 PRIVATE include plugins)
target_compile_definitions(plugin_fault_recorder_a21 PRIVATE
    CLIENTHACK_CLIENT_A21=1
    CLIENTHACK_PLUGIN_NAME="FaultRecorder"
    CLIENTHACK_PLUGIN_FLAGS=0u
    CLIENTHACK_PLUGIN_HAS_SHUTDOWN=1)
clienthack_warnings(plugin_fault_recorder_a21)
set_target_properties(plugin_fault_recorder_a21 PROPERTIES
    PREFIX ""
    OUTPUT_NAME "0007_FaultRecorder"
    RUNTIME_OUTPUT_DIRECTORY "${CLIENTHACK_STAGE_DIR_A21}/plugins"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CLIENTHACK_STAGE_DIR_A21}/plugins"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CLIENTHACK_STAGE_DIR_A21}/plugins")
clienthack_plugin_a21(plugin_network_hook_a21
    plugins/0100_network_hook/network_hook.c
    "0100_NetworkHook" "NetworkHook")
clienthack_plugin_a21(plugin_server_redirect_a21
    plugins/0110_server_redirect/server_redirect.c
    "0110_ServerRedirect" "ServerRedirect")
target_link_libraries(plugin_server_redirect_a21 PRIVATE clienthack_core_a21 ws2_32)

add_custom_target(clienthack_plugins_a21 DEPENDS
    plugin_network_hook_a21
    plugin_server_redirect_a21
    plugin_disable_gameguard_a21
    plugin_client_compatibility_a21
    plugin_a21_runtime_fix)

clienthack_plugin(plugin_network_hook
    plugins/0100_network_hook/network_hook.c
    "0100_NetworkHook" "NetworkHook")
clienthack_plugin(plugin_server_redirect
    plugins/0110_server_redirect/server_redirect.c
    "0110_ServerRedirect" "ServerRedirect")
target_link_libraries(plugin_server_redirect PRIVATE clienthack_core ws2_32)
clienthack_plugin(plugin_prevent_minimize
    plugins/0900_prevent_minimize/prevent_minimize.c
    "0900_PreventMinimize" "PreventMinimize")

add_custom_target(clienthack_plugins DEPENDS
    plugin_page_guard_trace
    plugin_anti_anti_debug
    plugin_b1_startup_guard
    plugin_disable_gameguard
    plugin_client_compatibility
    plugin_network_hook
    plugin_server_redirect
    plugin_prevent_minimize)

add_custom_target(clienthack_stage ALL DEPENDS Az ijl15 clienthack_plugins)

add_custom_command(TARGET Az POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        "${CMAKE_CURRENT_SOURCE_DIR}/ClientHack.ini"
        "${CLIENTHACK_STAGE_DIR}/ClientHack.ini")

add_dependencies(clienthack_tests clienthack_stage)
target_compile_definitions(clienthack_tests PRIVATE
    CLIENTHACK_STAGE_DIR=L"${CLIENTHACK_STAGE_DIR}")

add_executable(clienthack_host_tests
    tests/test_host_services.c
    src/host/host_services.c
    src/host/readiness.c
    src/host/startup_trace.c)
target_include_directories(clienthack_host_tests PRIVATE include src/host)
target_link_libraries(clienthack_host_tests PRIVATE clienthack_core minhook)
clienthack_warnings(clienthack_host_tests)
add_test(NAME clienthack_host_tests COMMAND clienthack_host_tests)

set(CLIENTHACK_LOADER_FIXTURE_DIR "${CMAKE_BINARY_DIR}/loader-fixture")
set(CLIENTHACK_SERVICE_FIXTURE_DIR "${CMAKE_BINARY_DIR}/service-fixture")
add_library(fixture_failing_plugin SHARED
    tests/fixture_failing_plugin.c
    plugins/plugin.def)
target_include_directories(fixture_failing_plugin PRIVATE include)
clienthack_warnings(fixture_failing_plugin)
set_target_properties(fixture_failing_plugin PROPERTIES
    PREFIX ""
    OUTPUT_NAME "0010_DisableGameGuard"
    RUNTIME_OUTPUT_DIRECTORY "${CLIENTHACK_LOADER_FIXTURE_DIR}/plugins"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CLIENTHACK_LOADER_FIXTURE_DIR}/plugins"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CLIENTHACK_LOADER_FIXTURE_DIR}/plugins")

add_library(fixture_service_failure SHARED
    tests/fixture_service_failure.c
    plugins/plugin.def)
target_include_directories(fixture_service_failure PRIVATE include)
clienthack_warnings(fixture_service_failure)
set_target_properties(fixture_service_failure PROPERTIES
    PREFIX ""
    OUTPUT_NAME "1000_ServiceFailure"
    RUNTIME_OUTPUT_DIRECTORY "${CLIENTHACK_SERVICE_FIXTURE_DIR}/plugins"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CLIENTHACK_SERVICE_FIXTURE_DIR}/plugins"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CLIENTHACK_SERVICE_FIXTURE_DIR}/plugins")

add_executable(clienthack_loader_tests
    tests/test_plugin_loader.c
    src/host/host_services.c
    src/host/plugin_loader.c)
target_include_directories(clienthack_loader_tests PRIVATE include src/host)
target_link_libraries(clienthack_loader_tests PRIVATE clienthack_core minhook)
target_compile_definitions(clienthack_loader_tests PRIVATE
    CLIENTHACK_LOADER_FIXTURE_DIR=L"${CLIENTHACK_LOADER_FIXTURE_DIR}"
    CLIENTHACK_SERVICE_FIXTURE_DIR=L"${CLIENTHACK_SERVICE_FIXTURE_DIR}")
add_dependencies(clienthack_loader_tests fixture_failing_plugin fixture_service_failure)
clienthack_warnings(clienthack_loader_tests)
add_test(NAME clienthack_loader_tests COMMAND clienthack_loader_tests)

set(CLIENTHACK_ROLLBACK_FAILURE_FIXTURE_DIR "${CMAKE_BINARY_DIR}/rollback-failure-fixture")
add_library(fixture_rollback_failure SHARED
    tests/fixture_rollback_failure.c
    plugins/plugin.def)
target_include_directories(fixture_rollback_failure PRIVATE include)
clienthack_warnings(fixture_rollback_failure)
set_target_properties(fixture_rollback_failure PROPERTIES
    PREFIX ""
    OUTPUT_NAME "0010_DisableGameGuard"
    RUNTIME_OUTPUT_DIRECTORY "${CLIENTHACK_ROLLBACK_FAILURE_FIXTURE_DIR}/plugins"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CLIENTHACK_ROLLBACK_FAILURE_FIXTURE_DIR}/plugins"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CLIENTHACK_ROLLBACK_FAILURE_FIXTURE_DIR}/plugins")

add_executable(clienthack_rollback_failure_tests
    tests/test_rollback_failure.c
    src/host/host_services.c
    src/host/plugin_loader.c)
target_include_directories(clienthack_rollback_failure_tests PRIVATE include src/host)
target_link_libraries(clienthack_rollback_failure_tests PRIVATE clienthack_core minhook)
target_compile_definitions(clienthack_rollback_failure_tests PRIVATE
    CLIENTHACK_ROLLBACK_FAILURE_FIXTURE_DIR=L"${CLIENTHACK_ROLLBACK_FAILURE_FIXTURE_DIR}")
add_dependencies(clienthack_rollback_failure_tests fixture_rollback_failure)
clienthack_warnings(clienthack_rollback_failure_tests)
add_test(NAME clienthack_rollback_failure_tests COMMAND clienthack_rollback_failure_tests)
