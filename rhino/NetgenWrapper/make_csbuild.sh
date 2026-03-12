#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
REPO_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/../.." && pwd)
CSBUILD_DIR="$SCRIPT_DIR/csbuild"
CS_PROJECT="$REPO_ROOT/rhino/RhinoNetgenBridge/RhinoNetgenBridge.csproj"
WRAPPER_LIB="$SCRIPT_DIR/build/libnetgen_wrapper.dylib"
NETGEN_LIB_DIR="$REPO_ROOT/.local/netgen-min/Contents/MacOS"
NGLIB_LIB="$NETGEN_LIB_DIR/libnglib.dylib"
NGCORE_LIB="$NETGEN_LIB_DIR/libngcore.dylib"

detect_target_framework() {
    target_framework=$(sed -n 's:.*<TargetFramework>\([^<]*\)</TargetFramework>.*:\1:p' "$CS_PROJECT" | head -n 1)
    if [ -n "${target_framework:-}" ]; then
        printf '%s\n' "$target_framework"
        return 0
    fi

    frameworks=$(sed -n 's:.*<TargetFrameworks>\([^<]*\)</TargetFrameworks>.*:\1:p' "$CS_PROJECT" | head -n 1)
    if [ -z "${frameworks:-}" ]; then
        return 1
    fi

    old_ifs=$IFS
    IFS=';'
    set -- $frameworks
    IFS=$old_ifs

    for framework in "$@"; do
        case "$framework" in
            net[0-9]*.[0-9]*)
                printf '%s\n' "$framework"
                return 0
                ;;
        esac
    done

    return 1
}

require_file() {
    if [ ! -f "$1" ]; then
        echo "missing required file: $1" >&2
        exit 1
    fi
}

require_file "$WRAPPER_LIB"
require_file "$NGLIB_LIB"
require_file "$NGCORE_LIB"
require_file "$CS_PROJECT"

TARGET_FRAMEWORK=$(detect_target_framework) || {
    echo "could not determine a .NET target framework from $CS_PROJECT" >&2
    exit 1
}

CS_OUTPUT_DIR="$REPO_ROOT/rhino/RhinoNetgenBridge/bin/Release/$TARGET_FRAMEWORK"

dotnet build "$CS_PROJECT" -c Release -f "$TARGET_FRAMEWORK"

require_file "$CS_OUTPUT_DIR/RhinoNetgenBridge.dll"

rm -rf "$CSBUILD_DIR"
mkdir -p "$CSBUILD_DIR"

cp "$WRAPPER_LIB" "$CSBUILD_DIR/"
cp "$NGLIB_LIB" "$CSBUILD_DIR/"
cp "$NGCORE_LIB" "$CSBUILD_DIR/"
cp "$CS_OUTPUT_DIR/RhinoNetgenBridge.dll" "$CSBUILD_DIR/"

if [ -f "$CS_OUTPUT_DIR/RhinoNetgenBridge.pdb" ]; then
    cp "$CS_OUTPUT_DIR/RhinoNetgenBridge.pdb" "$CSBUILD_DIR/"
fi

if [ -f "$CS_OUTPUT_DIR/RhinoNetgenBridge.deps.json" ]; then
    cp "$CS_OUTPUT_DIR/RhinoNetgenBridge.deps.json" "$CSBUILD_DIR/"
fi

install_name_tool \
    -change "@rpath/libnglib.dylib" "@loader_path/libnglib.dylib" \
    "$CSBUILD_DIR/libnetgen_wrapper.dylib"

install_name_tool \
    -change "@rpath/libngcore.dylib" "@loader_path/libngcore.dylib" \
    "$CSBUILD_DIR/libnglib.dylib"

echo "Prepared $CSBUILD_DIR"
echo "TargetFramework=$TARGET_FRAMEWORK"
ls -1 "$CSBUILD_DIR"
