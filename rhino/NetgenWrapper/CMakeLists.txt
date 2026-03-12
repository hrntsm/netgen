cmake_minimum_required(VERSION 3.14)
project(netgen_wrapper CXX)

# ---------------------------------------------------------------------------
# Options
# ---------------------------------------------------------------------------
option(NGWRAPPER_BUILD_SHARED "Build netgen_wrapper as a shared library" ON)

# ---------------------------------------------------------------------------
# Find the installed netgen (nglib)
# ---------------------------------------------------------------------------
# Prefer an installation pointed to by NETGEN_DIR or CMAKE_PREFIX_PATH.
# When building in-tree (e.g. as a subdirectory of the netgen source tree),
# we can also just use the build-tree targets directly.

# Try to find netgen via its installed CMake config first.
find_package(netgen QUIET)

if(netgen_FOUND)
    message(STATUS "Using installed netgen package")
    set(NGLIB_TARGET nglib)
    set(NGLIB_INCLUDE_DIRS "")   # header exposed via target
else()
    # Fallback: expect the user to supply NGLIB_INCLUDE_DIR and NGLIB_LIBRARY
    # via -D flags, or that we are inside the netgen source tree.
    if(NOT DEFINED NGLIB_INCLUDE_DIR)
        # Assume we are in <netgen_root>/rhino/NetgenWrapper/
        set(NGLIB_INCLUDE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../nglib")
        message(STATUS "NGLIB_INCLUDE_DIR not set – defaulting to ${NGLIB_INCLUDE_DIR}")
    endif()

    if(NOT DEFINED NGLIB_LIBRARY)
        # Look in the build tree or a standard install prefix
        find_library(NGLIB_LIBRARY
            NAMES nglib
            HINTS
                "${CMAKE_CURRENT_SOURCE_DIR}/../../build/nglib"
                "${CMAKE_INSTALL_PREFIX}/lib"
                "/usr/local/lib"
                "/usr/lib")
        if(NOT NGLIB_LIBRARY)
            message(FATAL_ERROR
                "Could not find nglib. "
                "Please build netgen first (cmake --build <build_dir> --target nglib) "
                "and then set NGLIB_LIBRARY=<path/to/libnglib.so>.")
        endif()
    endif()

    message(STATUS "Using nglib: ${NGLIB_LIBRARY}")
    set(NGLIB_TARGET "${NGLIB_LIBRARY}")
    set(NGLIB_INCLUDE_DIRS "${NGLIB_INCLUDE_DIR}")
endif()

# ---------------------------------------------------------------------------
# Build the wrapper shared library
# ---------------------------------------------------------------------------
if(NGWRAPPER_BUILD_SHARED)
    set(_lib_type SHARED)
else()
    set(_lib_type STATIC)
endif()

add_library(netgen_wrapper ${_lib_type}
    netgen_wrapper.cpp
)

target_compile_definitions(netgen_wrapper PRIVATE NGWRAPPER_EXPORTS)

target_include_directories(netgen_wrapper
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}>
    PRIVATE
        ${NGLIB_INCLUDE_DIRS}
)

target_link_libraries(netgen_wrapper
    PRIVATE
        ${NGLIB_TARGET}
)

set_target_properties(netgen_wrapper PROPERTIES
    CXX_STANDARD 17
    CXX_STANDARD_REQUIRED ON
    POSITION_INDEPENDENT_CODE ON
)

# On Windows export the DLL alongside the import lib
if(WIN32)
    set_target_properties(netgen_wrapper PROPERTIES
        RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
    )
endif()

# ---------------------------------------------------------------------------
# Install
# ---------------------------------------------------------------------------
include(GNUInstallDirs)
install(TARGETS netgen_wrapper
    LIBRARY  DESTINATION "${CMAKE_INSTALL_LIBDIR}"
    ARCHIVE  DESTINATION "${CMAKE_INSTALL_LIBDIR}"
    RUNTIME  DESTINATION "${CMAKE_INSTALL_BINDIR}"
)
install(FILES netgen_wrapper.h
    DESTINATION "${CMAKE_INSTALL_INCLUDEDIR}"
)
