cmake_minimum_required(VERSION 3.20)
project(CUEToolsMusepackEncoder C)

if(NOT DEFINED MUSEPACK_SOURCE_DIR)
    message(FATAL_ERROR "MUSEPACK_SOURCE_DIR must name the patched r495 source tree")
endif()

add_compile_definitions(FAST_MATH CVD_FASTLOG CUETOOLS_NO_TAGS)

if(MSVC)
    # r495 uses the portable CRT names that current MSVC marks deprecated and
    # several POSIX spellings that it keeps as compatibility aliases. Retain
    # those source-level APIs without masking unrelated diagnostics.
    add_compile_definitions(_CRT_SECURE_NO_WARNINGS _CRT_NONSTDC_NO_WARNINGS)
    # Embed debug information in objects so the final PE does not acquire a
    # build-directory-specific PDB record. /Brepro plus the fixed upstream
    # version string makes independently staged builds byte-identical.
    set(CMAKE_MSVC_RUNTIME_LIBRARY MultiThreaded)
    set(CMAKE_MSVC_DEBUG_INFORMATION_FORMAT Embedded)
    # r495 intentionally performs float-domain psychoacoustic arithmetic with
    # unsuffixed constants and stores bounded sizes in its 32-bit bitstream
    # fields. Preserve those historical conversions while keeping every other
    # level-3 diagnostic fatal.
    add_compile_options(/Brepro /W3 /WX /wd4018 /wd4244 /wd4267)
endif()

add_library(mpcpsy_static STATIC
    "${MUSEPACK_SOURCE_DIR}/libmpcpsy/ans.c"
    "${MUSEPACK_SOURCE_DIR}/libmpcpsy/cvd.c"
    "${MUSEPACK_SOURCE_DIR}/libmpcpsy/fft4g.c"
    "${MUSEPACK_SOURCE_DIR}/libmpcpsy/fft_routines.c"
    "${MUSEPACK_SOURCE_DIR}/libmpcpsy/profile.c"
    "${MUSEPACK_SOURCE_DIR}/libmpcpsy/psy.c"
    "${MUSEPACK_SOURCE_DIR}/libmpcpsy/psy_tab.c"
    "${MUSEPACK_SOURCE_DIR}/common/fastmath.c")
target_include_directories(mpcpsy_static PRIVATE
    "${MUSEPACK_SOURCE_DIR}/include"
    "${MUSEPACK_SOURCE_DIR}/libmpcpsy")

add_library(mpcenc_static STATIC
    "${MUSEPACK_SOURCE_DIR}/libmpcenc/analy_filter.c"
    "${MUSEPACK_SOURCE_DIR}/libmpcenc/bitstream.c"
    "${MUSEPACK_SOURCE_DIR}/libmpcenc/encode_sv7.c"
    "${MUSEPACK_SOURCE_DIR}/libmpcenc/huffsv7.c"
    "${MUSEPACK_SOURCE_DIR}/libmpcenc/quant.c"
    "${MUSEPACK_SOURCE_DIR}/common/crc32.c")
target_include_directories(mpcenc_static PRIVATE
    "${MUSEPACK_SOURCE_DIR}/include"
    "${MUSEPACK_SOURCE_DIR}/libmpcenc")

add_executable(mpcenc
    "${MUSEPACK_SOURCE_DIR}/mpcenc/keyboard.c"
    "${MUSEPACK_SOURCE_DIR}/mpcenc/mpcenc.c"
    "${MUSEPACK_SOURCE_DIR}/mpcenc/pipeopen.c"
    "${MUSEPACK_SOURCE_DIR}/mpcenc/stderr.c"
    "${MUSEPACK_SOURCE_DIR}/mpcenc/wave_in.c"
    "${MUSEPACK_SOURCE_DIR}/mpcenc/winmsg.c")
target_include_directories(mpcenc PRIVATE
    "${MUSEPACK_SOURCE_DIR}/include"
    "${MUSEPACK_SOURCE_DIR}/libmpcenc"
    "${MUSEPACK_SOURCE_DIR}/libmpcpsy"
    "${MUSEPACK_SOURCE_DIR}/mpcenc")
target_link_libraries(mpcenc PRIVATE mpcpsy_static mpcenc_static winmm)

if(MSVC)
    target_link_options(mpcenc PRIVATE /Brepro /DEBUG:NONE /INCREMENTAL:NO)
endif()
