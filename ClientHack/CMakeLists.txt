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

enable_testing()
find_program(CLIENTHACK_POWERSHELL_EXECUTABLE NAMES powershell pwsh REQUIRED)
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
    src/host/readiness.c)
target_include_directories(Az PRIVATE include src/host)
target_link_libraries(Az PRIVATE clienthack_core minhook user32)
clienthack_warnings(Az)
set_target_properties(Az PROPERTIES
    PREFIX ""
    OUTPUT_NAME "Az"
    RUNTIME_OUTPUT_DIRECTORY "${CLIENTHACK_STAGE_DIR}"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CLIENTHACK_STAGE_DIR}"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CLIENTHACK_STAGE_DIR}")

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

clienthack_plugin(plugin_anti_anti_debug
    plugins/0005_anti_anti_debug/anti_anti_debug.c
    "0005_AntiAntiDebug" "AntiAntiDebug")
if(CLIENTHACK_PROTECT_DRX)
    target_compile_definitions(plugin_anti_anti_debug PRIVATE CLIENTHACK_PROTECT_DRX=1)
else()
    target_compile_definitions(plugin_anti_anti_debug PRIVATE CLIENTHACK_PROTECT_DRX=0)
endif()
clienthack_plugin(plugin_disable_gameguard
    plugins/0010_disable_gameguard/disable_gameguard.c
    "0010_DisableGameGuard" "DisableGameGuard")
clienthack_plugin(plugin_client_compatibility
    plugins/0020_client_compatibility/client_compatibility.c
    "0020_ClientCompatibility" "ClientCompatibility")
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
    plugin_anti_anti_debug
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
    src/host/readiness.c)
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
