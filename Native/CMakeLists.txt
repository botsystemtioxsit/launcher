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

# файлы, вшитые в exe: gs_embed_tree(<куда> <имя> <папка>) — массив {путь, данные, размер}
function(gs_embed_tree out_cpp symbol root)
  file(GLOB_RECURSE files RELATIVE ${root} ${root}/*)
  list(SORT files)
  set(decl "")
  set(table "")
  set(i 0)
  foreach(f ${files})
    file(READ ${root}/${f} hex HEX)
    string(REGEX REPLACE "([0-9a-f][0-9a-f])" "0x\\1," bytes "${hex}")
    file(SIZE ${root}/${f} sz)
    string(APPEND decl "static const unsigned char d${i}[] = {${bytes}0};\n")
    string(APPEND table "{\"${f}\", d${i}, ${sz}},\n")
    math(EXPR i "${i}+1")
  endforeach()
  file(WRITE ${out_cpp} "#include <cstddef>\nnamespace gs { struct EmbFile { const char* path; const unsigned char* data; size_t size; };\n${decl}\nextern const EmbFile ${symbol}[];\nconst EmbFile ${symbol}[] = {\n${table}{nullptr, nullptr, 0}};\n}\n")
endfunction()

# Microsoft Edge WebView2 SDK (пакет NuGet: заголовки и загрузчик WebView2Loader.dll; сам движок — часть Windows 11)
set(WV2_VER 1.0.2792.45)
set(WV2_DIR ${CMAKE_BINARY_DIR}/wv2)
if(NOT EXISTS ${WV2_DIR}/build/native/include/WebView2.h)
  file(DOWNLOAD https://www.nuget.org/api/v2/package/Microsoft.Web.WebView2/${WV2_VER} ${CMAKE_BINARY_DIR}/wv2.zip STATUS wv2_status TLS_VERIFY ON)
  list(GET wv2_status 0 wv2_code)
  if(NOT wv2_code EQUAL 0)
    message(FATAL_ERROR "Не скачался WebView2 SDK: ${wv2_status}")
  endif()
  file(ARCHIVE_EXTRACT INPUT ${CMAKE_BINARY_DIR}/wv2.zip DESTINATION ${WV2_DIR})
endif()
file(WRITE ${CMAKE_BINARY_DIR}/wv2.rc "WV2LOADER RCDATA \"${WV2_DIR}/build/native/x64/WebView2Loader.dll\"\n")

add_library(gscommon STATIC common/win.cpp common/loc.cpp ${CMAKE_BINARY_DIR}/common_loc.cpp)
target_include_directories(gscommon PUBLIC common)

# ядро: рисование, слои поверх экрана, оболочка с вкладками
add_library(gscore STATIC core/gfx.cpp core/scanner.cpp core/surface.cpp core/overlay_host.cpp core/ui_common.cpp core/app.cpp core/snapshots.cpp core/gpu_canvas.cpp core/webview.cpp)
target_include_directories(gscore PRIVATE third_party/shim ${WV2_DIR}/build/native/include)
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

add_library(mod_color STATIC modules/color/color_module.cpp)
target_include_directories(mod_color PUBLIC modules/color)
target_link_libraries(mod_color PUBLIC gscore mod_cursor dwmapi psapi)

gs_embed_tree(${CMAKE_BINARY_DIR}/wp_assets.cpp kWpAssets ${CMAKE_SOURCE_DIR}/modules/wallpaper/assets)
add_library(mod_wallpaper STATIC modules/wallpaper/wp_model.cpp modules/wallpaper/wp_telemetry.cpp modules/wallpaper/wp_controller.cpp
  modules/wallpaper/wallpaper_module.cpp modules/wallpaper/wallpaper_test.cpp ${CMAKE_BINARY_DIR}/wp_assets.cpp)
target_include_directories(mod_wallpaper PUBLIC modules/wallpaper)
target_link_libraries(mod_wallpaper PUBLIC gscore winhttp iphlpapi dwmapi shell32 ole32 uuid)

# проверка логики Season без экрана
add_executable(sp_logic_test modules/season/logic_test.cpp)
target_link_libraries(sp_logic_test PRIVATE mod_season)

# приложение
gs_embed_text(${CMAKE_BINARY_DIR}/style_loc.cpp kStyleLoc ${CMAKE_SOURCE_DIR}/apps/style/Loc/app.loc ${CMAKE_SOURCE_DIR}/modules/season/Loc/app.loc ${CMAKE_SOURCE_DIR}/modules/wallpaper/Loc/app.loc ${CMAKE_SOURCE_DIR}/modules/cursor/Loc/app.loc ${CMAKE_SOURCE_DIR}/modules/color/Loc/app.loc)
add_executable(GhostStyle WIN32 apps/style/main.cpp apps/style/app.rc ${CMAKE_BINARY_DIR}/wv2.rc ${CMAKE_BINARY_DIR}/style_loc.cpp)
target_link_libraries(GhostStyle PRIVATE mod_season mod_wallpaper mod_cursor mod_color gscore)
target_link_options(GhostStyle PRIVATE -mwindows -municode)
