cmake_minimum_required(VERSION 3.20)
project(CUEToolsOpusEncoder C)

foreach(source_var
        OPUS_SOURCE_DIR
        LIBOPUSENC_SOURCE_DIR
        LIBOGG_SOURCE_DIR
        OPUS_TOOLS_SOURCE_DIR)
    if(NOT DEFINED ${source_var} OR NOT IS_DIRECTORY "${${source_var}}")
        message(FATAL_ERROR
            "${source_var} must name the corresponding extracted source tree")
    endif()
    file(TO_CMAKE_PATH "${${source_var}}" normalized_source_dir)
    set(${source_var} "${normalized_source_dir}")
endforeach()

set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(INSTALL_DOCS OFF CACHE BOOL "" FORCE)
set(INSTALL_PKG_CONFIG_MODULE OFF CACHE BOOL "" FORCE)
set(INSTALL_CMAKE_PACKAGE_MODULE OFF CACHE BOOL "" FORCE)
set(OPUS_BUILD_SHARED_LIBRARY OFF CACHE BOOL "" FORCE)
set(OPUS_BUILD_TESTING OFF CACHE BOOL "" FORCE)
set(OPUS_BUILD_PROGRAMS OFF CACHE BOOL "" FORCE)
set(OPUS_CUSTOM_MODES OFF CACHE BOOL "" FORCE)
set(OPUS_DRED OFF CACHE BOOL "" FORCE)
set(OPUS_OSCE OFF CACHE BOOL "" FORCE)
set(OPUS_INSTALL_PKG_CONFIG_MODULE OFF CACHE BOOL "" FORCE)
set(OPUS_INSTALL_CMAKE_CONFIG_MODULE OFF CACHE BOOL "" FORCE)
set(OPUS_STATIC_RUNTIME ON CACHE BOOL "" FORCE)

if(MSVC)
    set(CMAKE_MSVC_RUNTIME_LIBRARY MultiThreaded)
    set(CMAKE_MSVC_DEBUG_INFORMATION_FORMAT Embedded)
    add_compile_options(
        /Brepro
        /experimental:deterministic
        /W3
        /WX
        "/pathmap:${OPUS_SOURCE_DIR}=opus-1.6.1"
        "/pathmap:${LIBOPUSENC_SOURCE_DIR}=libopusenc-0.3"
        "/pathmap:${LIBOGG_SOURCE_DIR}=libogg-1.3.6"
        "/pathmap:${OPUS_TOOLS_SOURCE_DIR}=opus-tools-0.2"
        "/pathmap:${CMAKE_BINARY_DIR}=build")
endif()

add_subdirectory("${LIBOGG_SOURCE_DIR}" "${CMAKE_BINARY_DIR}/libogg")
add_subdirectory("${OPUS_SOURCE_DIR}" "${CMAKE_BINARY_DIR}/opus")

file(MAKE_DIRECTORY
    "${CMAKE_BINARY_DIR}/generated/libopusenc"
    "${CMAKE_BINARY_DIR}/generated/opus-tools")

file(WRITE "${CMAKE_BINARY_DIR}/generated/libopusenc/config.h" [=[
#ifndef CUETOOLS_LIBOPUSENC_CONFIG_H
#define CUETOOLS_LIBOPUSENC_CONFIG_H
#define RESAMPLE_FULL_SINC_TABLE 1
#define OPE_BUILD 1
#define PACKAGE_NAME "libopusenc"
#define PACKAGE_VERSION "0.3"
#endif
]=])

file(WRITE "${CMAKE_BINARY_DIR}/generated/opus-tools/config.h" [=[
#ifndef CUETOOLS_OPUS_TOOLS_CONFIG_H
#define CUETOOLS_OPUS_TOOLS_CONFIG_H
#define HAVE_INTTYPES_H 1
#define OUTSIDE_SPEEX 1
#define OPUSTOOLS 1
#define RESAMPLE_FULL_SINC_TABLE 1
#define RANDOM_PREFIX opustools
#define PACKAGE_NAME "opus-tools"
#define PACKAGE_VERSION "0.2-cuetools-libopus-1.6.1"
#if defined(_MSC_VER) && !defined(__cplusplus)
#define inline __inline
#endif
#endif
]=])

add_library(libopusenc_static STATIC
    "${LIBOPUSENC_SOURCE_DIR}/src/ogg_packer.c"
    "${LIBOPUSENC_SOURCE_DIR}/src/opus_header.c"
    "${LIBOPUSENC_SOURCE_DIR}/src/opusenc.c"
    "${LIBOPUSENC_SOURCE_DIR}/src/picture.c"
    "${LIBOPUSENC_SOURCE_DIR}/src/resample.c"
    "${LIBOPUSENC_SOURCE_DIR}/src/unicode_support.c")
if(MSVC)
    # The retained Speex-derived resampler intentionally evaluates its filter
    # design in double precision before storing float coefficients, and mixes
    # signed loop counters with the API's unsigned sample counts. Scope those
    # two historical diagnostic classes to that one numeric source file.
    set_source_files_properties(
        "${LIBOPUSENC_SOURCE_DIR}/src/resample.c"
        PROPERTIES COMPILE_OPTIONS "/wd4018;/wd4244")
endif()
target_compile_definitions(libopusenc_static PRIVATE
    HAVE_CONFIG_H
    OUTSIDE_SPEEX
    RANDOM_PREFIX=libopusenc
    _CRT_SECURE_NO_WARNINGS
    _CRT_NONSTDC_NO_WARNINGS)
target_include_directories(libopusenc_static PRIVATE
    "${CMAKE_BINARY_DIR}/generated/libopusenc"
    "${LIBOPUSENC_SOURCE_DIR}/include"
    "${LIBOPUSENC_SOURCE_DIR}/src")
target_link_libraries(libopusenc_static PUBLIC Opus::opus)

add_executable(opusenc
    "${OPUS_TOOLS_SOURCE_DIR}/share/getopt.c"
    "${OPUS_TOOLS_SOURCE_DIR}/share/getopt1.c"
    "${OPUS_TOOLS_SOURCE_DIR}/src/opus_header.c"
    "${OPUS_TOOLS_SOURCE_DIR}/src/opusenc.c"
    "${OPUS_TOOLS_SOURCE_DIR}/src/picture.c"
    "${OPUS_TOOLS_SOURCE_DIR}/src/audio-in.c"
    "${OPUS_TOOLS_SOURCE_DIR}/src/diag_range.c"
    "${OPUS_TOOLS_SOURCE_DIR}/src/flac.c"
    "${OPUS_TOOLS_SOURCE_DIR}/win32/unicode_support.c")
target_compile_definitions(opusenc PRIVATE
    HAVE_CONFIG_H
    WIN32
    _CRT_SECURE_NO_WARNINGS
    _CRT_NONSTDC_NO_WARNINGS)
target_include_directories(opusenc PRIVATE
    "${CMAKE_BINARY_DIR}/generated/opus-tools"
    "${OPUS_TOOLS_SOURCE_DIR}/include"
    "${OPUS_TOOLS_SOURCE_DIR}/src"
    "${OPUS_TOOLS_SOURCE_DIR}/win32"
    "${LIBOPUSENC_SOURCE_DIR}/include")
target_link_libraries(opusenc PRIVATE
    libopusenc_static
    Opus::opus
    Ogg::ogg
    winmm)

if(MSVC)
    target_link_options(opusenc PRIVATE /Brepro /DEBUG:NONE /INCREMENTAL:NO)
endif()
