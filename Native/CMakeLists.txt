cmake_minimum_required(VERSION 3.20)
project(GhostShellNative CXX C)

# Нативные (C++) приложения Ghost Shell. Без .NET: один маленький exe, статическая сборка, рисование на видеокарте (Direct2D + DirectComposition).
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

# словари перевода вшиваются в exe как строки: gs_embed_text(<куда> <имя> <файл.loc>...)
function(gs_embed_text out_cpp symbol)
  set(content "")
  foreach(f ${ARGN})
    file(READ ${f} part)
    string(APPEND content "${part}\n")
  endforeach()
  file(WRITE ${out_cpp} "namespace gs { extern const char* ${symbol}; const char* ${symbol} = R\"GSLOC(${content})GSLOC\"; }\n")
endfunction()
gs_embed_text(${CMAKE_BINARY_DIR}/common_loc.cpp kCommonLoc ${CMAKE_SOURCE_DIR}/../Shared/Loc/common.loc)

add_library(gscommon STATIC common/win.cpp common/loc.cpp ${CMAKE_BINARY_DIR}/common_loc.cpp)
target_include_directories(gscommon PUBLIC common)

# ядро: рисование, слои поверх экрана, оболочка с вкладками
add_library(gscore STATIC core/gfx.cpp core/scanner.cpp core/surface.cpp core/overlay_host.cpp core/ui_common.cpp core/app.cpp core/snapshots.cpp core/gpu_canvas.cpp)
target_include_directories(gscore PUBLIC core)
target_link_libraries(gscore PUBLIC gscommon imgui d3d11 d3dcompiler dxgi d2d1 dwrite dcomp windowscodecs dwmapi ole32 uuid winmm shell32 user32 gdi32 advapi32)

# модули
add_library(mod_season STATIC modules/season/model.cpp modules/season/engine.cpp modules/season/settings_io.cpp modules/season/draw.cpp
  modules/season/scenes.cpp modules/season/preview.cpp modules/season/season_module.cpp)
target_include_directories(mod_season PUBLIC modules/season)
target_link_libraries(mod_season PUBLIC gscore)

add_library(mod_cursor STATIC modules/cursor/cursor_settings.cpp modules/cursor/cursor_shapes.cpp modules/cursor/cursor_files.cpp modules/cursor/cursor_apply.cpp
  modules/cursor/trail_layer.cpp modules/cursor/cursor_module.cpp)
target_include_directories(mod_cursor PUBLIC modules/cursor)
target_link_libraries(mod_cursor PUBLIC gscore)

# проверка логики Season без экрана
add_executable(sp_logic_test modules/season/logic_test.cpp)
target_link_libraries(sp_logic_test PRIVATE mod_season)

# приложение
gs_embed_text(${CMAKE_BINARY_DIR}/style_loc.cpp kStyleLoc ${CMAKE_SOURCE_DIR}/apps/style/Loc/app.loc ${CMAKE_SOURCE_DIR}/modules/season/Loc/app.loc ${CMAKE_SOURCE_DIR}/modules/cursor/Loc/app.loc)
add_executable(GhostStyle WIN32 apps/style/main.cpp apps/style/app.rc ${CMAKE_BINARY_DIR}/style_loc.cpp)
target_link_libraries(GhostStyle PRIVATE mod_season mod_cursor gscore)
target_link_options(GhostStyle PRIVATE -mwindows -municode)
