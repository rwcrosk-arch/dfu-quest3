cmake_minimum_required(VERSION 3.10)
project(dfutrigger_layer CXX)

set(CMAKE_CXX_STANDARD 17)

add_library(dfutrigger_layer SHARED src/dfutrigger_layer.cpp)

target_include_directories(dfutrigger_layer PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/include)

target_compile_options(dfutrigger_layer PRIVATE
    -fvisibility=hidden
    -fPIC
    -Wall
)

target_link_libraries(dfutrigger_layer PRIVATE log android)
