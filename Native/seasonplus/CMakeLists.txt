# Season Plus — нативная версия плагина (C++)
add_library(spcore STATIC model.cpp engine.cpp settings_io.cpp)
target_include_directories(spcore PUBLIC .)
target_link_libraries(spcore PUBLIC gscommon)

# проверка логики без экрана
add_executable(sp_logic_test logic_test.cpp)
target_link_libraries(sp_logic_test PRIVATE spcore)

gs_embed_text(${CMAKE_BINARY_DIR}/season_loc.cpp kSeasonLoc ${CMAKE_CURRENT_SOURCE_DIR}/Loc/app.loc)

add_executable(SeasonPlus WIN32 main.cpp ui.cpp gfx.cpp draw.cpp overlay.cpp scanner.cpp scenes.cpp app.rc ${CMAKE_BINARY_DIR}/season_loc.cpp)
target_link_libraries(SeasonPlus PRIVATE spcore imgui d3d11 d3dcompiler dxgi d2d1 dwrite dcomp windowscodecs dwmapi ole32 uuid winmm shell32 user32 gdi32 advapi32)
target_link_options(SeasonPlus PRIVATE -mwindows -municode)
