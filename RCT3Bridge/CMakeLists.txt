cmake_minimum_required(VERSION 3.25)
project(RCT3Bridge LANGUAGES CXX)

if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "RCT3Bridge must be built as 32-bit Windows binaries")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(RCT3Bridge SHARED
  src/BridgeProtocol.cpp
  src/Rct3Bridge.cpp
)
target_include_directories(RCT3Bridge PRIVATE src)
target_compile_definitions(RCT3Bridge PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(RCT3Bridge PRIVATE /W4 /WX /permissive- /guard:cf)
target_link_libraries(RCT3Bridge PRIVATE d3d9 windowscodecs advapi32 ole32)
set_target_properties(RCT3Bridge PROPERTIES OUTPUT_NAME "RCT3Bridge")

add_executable(RCT3Launcher
  src/Rct3Launcher.cpp
)
target_compile_definitions(RCT3Launcher PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(RCT3Launcher PRIVATE /W4 /WX /permissive- /guard:cf)
target_link_libraries(RCT3Launcher PRIVATE advapi32)

include(CTest)
if(BUILD_TESTING)
  add_executable(RCT3Bridge.ProtocolTests
    tests/ProtocolTests.cpp
    src/BridgeProtocol.cpp
  )
  target_include_directories(RCT3Bridge.ProtocolTests PRIVATE src)
  target_compile_definitions(RCT3Bridge.ProtocolTests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
  target_compile_options(RCT3Bridge.ProtocolTests PRIVATE /W4 /WX /permissive-)
  add_test(NAME protocol COMMAND RCT3Bridge.ProtocolTests)
endif()
