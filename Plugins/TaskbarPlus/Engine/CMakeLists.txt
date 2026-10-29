cmake_minimum_required(VERSION 3.15)
cmake_policy(SET CMP0091 NEW)
project(GhostShellTap LANGUAGES CXX)

# Движок панели задач Windows 11 для Taskbar Plus. Собирается на GitHub Actions (windows-latest, Visual Studio 2022).
# Статическая среда выполнения (/MT): DLL работает внутри Проводника, ставить Visual C++ Redistributable не нужно.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded")
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(GhostShellTap SHARED
    main.cpp styler.cpp config.cpp log.cpp GhostShellTap.def)

target_compile_definitions(GhostShellTap PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX _CRT_SECURE_NO_WARNINGS _SILENCE_EXPERIMENTAL_COROUTINE_DEPRECATION_WARNINGS)
target_compile_options(GhostShellTap PRIVATE /W3 /EHsc /permissive- /utf-8)
target_link_libraries(GhostShellTap PRIVATE windowsapp runtimeobject ole32 oleaut32 uuid user32 kernel32)

# C++/WinRT: готовые заголовки лежат в Windows SDK (папка cppwinrt рядом с um и winrt)
file(GLOB _kits "C:/Program Files (x86)/Windows Kits/10/Include/10.*/cppwinrt")
if(_kits)
    list(SORT _kits)
    list(REVERSE _kits)
    list(GET _kits 0 _cppwinrt)
    message(STATUS "C++/WinRT headers: ${_cppwinrt}")
    target_include_directories(GhostShellTap PRIVATE "${_cppwinrt}")
endif()

set_target_properties(GhostShellTap PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_CURRENT_SOURCE_DIR}/out"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_CURRENT_SOURCE_DIR}/out")
