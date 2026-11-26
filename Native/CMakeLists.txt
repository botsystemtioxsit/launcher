cmake_minimum_required(VERSION 3.20)
project(GhostShellNative CXX C)

# Нативные (C++) версии плагинов Ghost Shell. Без .NET: один маленький exe, статическая сборка, рисование на видеокарте (Direct2D + DirectComposition).
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/out)
add_compile_definitions(UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX _WIN32_WINNT=0x0A00 WINVER=0x0A00)
add_compile_options(-Wall -Wextra -Wno-unused-parameter -Wno-missing-field-initializers -O2 -g1)
add_link_options(-static -static-libgcc -static-libstdc++)

include(FetchContent)
set(IMGUI_TAG v1.91.5)
FetchContent_Declare(imgui GIT_REPOSITORY https://github.com/ocornut/imgui.git GIT_TAG ${IMGUI_TAG} GIT_SHALLOW TRUE)
FetchContent_GetProperties(imgui)
if(NOT imgui_POPULATED)
  FetchContent_Populate(imgui)
endif()
add_library(imgui STATIC
  ${imgui_SOURCE_DIR}/imgui.cpp ${imgui_SOURCE_DIR}/imgui_draw.cpp ${imgui_SOURCE_DIR}/imgui_tables.cpp ${imgui_SOURCE_DIR}/imgui_widgets.cpp
  ${imgui_SOURCE_DIR}/backends/imgui_impl_win32.cpp ${imgui_SOURCE_DIR}/backends/imgui_impl_dx11.cpp)
target_include_directories(imgui PUBLIC ${imgui_SOURCE_DIR} ${imgui_SOURCE_DIR}/backends)

# словари перевода вшиваются в exe как строки
function(gs_embed_text out_cpp symbol file)
  file(READ ${file} content)
  file(WRITE ${out_cpp} "namespace gs { extern const char* ${symbol}; const char* ${symbol} = R\"GSLOC(${content})GSLOC\"; }\n")
endfunction()
gs_embed_text(${CMAKE_BINARY_DIR}/common_loc.cpp kCommonLoc ${CMAKE_SOURCE_DIR}/../Shared/Loc/common.loc)

add_library(gscommon STATIC common/win.cpp common/loc.cpp ${CMAKE_BINARY_DIR}/common_loc.cpp)
target_include_directories(gscommon PUBLIC common)

add_subdirectory(seasonplus)
