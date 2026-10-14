#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
INSTALL_DIR="${INSTALL_DIR:-$HOME/vf16/tools}"
BUILD_ROM=""
ROM_OUTPUT=""
ROM_MODE="extended"
ROM_LOAD_ADDRESS="0"

usage() {
    cat <<'EOF'
Usage: ./build.sh [options]

Build and install the native Fox Vision host tools. Use --build-rom to also
compile a FoxC or Fox16ASM program into a ROM image loadable by FoxVision.

Options:
  --build-rom <source>  Build a .fc or .f16 source into a ROM image
  --rom-output <path>   ROM output path (default: build/<source>.bin)
  --mode <mode>         Target mode: legacy or extended (default: extended)
  --load-address <addr> Load ROM code at an address (default: 0)
  --install-dir <path>  Install host tools at this path
  -h, --help            Show this help
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --build-rom)
            [[ $# -ge 2 ]] || { echo "--build-rom requires a source file" >&2; exit 2; }
            BUILD_ROM="$2"
            shift 2
            ;;
        --rom-output)
            [[ $# -ge 2 ]] || { echo "--rom-output requires a path" >&2; exit 2; }
            ROM_OUTPUT="$2"
            shift 2
            ;;
        --mode)
            [[ $# -ge 2 ]] || { echo "--mode requires legacy or extended" >&2; exit 2; }
            ROM_MODE="$2"
            [[ "$ROM_MODE" == "legacy" || "$ROM_MODE" == "extended" ]] ||
                { echo "invalid mode: $ROM_MODE" >&2; exit 2; }
            shift 2
            ;;
        --load-address)
            [[ $# -ge 2 ]] || { echo "--load-address requires an address" >&2; exit 2; }
            ROM_LOAD_ADDRESS="$2"
            shift 2
            ;;
        --install-dir)
            [[ $# -ge 2 ]] || { echo "--install-dir requires a path" >&2; exit 2; }
            INSTALL_DIR="$2"
            shift 2
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "unknown option: $1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

echo "== VF16 TOOLCHAIN BUILD (CLEAN NATIVE MODE) =="
echo "Root: $ROOT"
echo "Install: $INSTALL_DIR"

mkdir -p "$INSTALL_DIR"
rm -rf "$INSTALL_DIR"/*

# -----------------------------
# Normalize dotnet publish into single-file binaries
# -----------------------------
publish_single () {
    local proj="$1"
    local name="$2"

    echo "[PUBLISH] $name"

    dotnet publish "$proj" \
        -c Release \
        -r linux-x64 \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:DebugType=None \
        -p:DebugSymbols=false \
        -o "$INSTALL_DIR/_tmp_$name" >/dev/null

    # Find actual output binary (dotnet may not name it exactly)
    local bin
    bin="$(find "$INSTALL_DIR/_tmp_$name" -maxdepth 1 -type f -executable | head -n 1)"

    if [[ -z "$bin" ]]; then
        echo "!! failed to locate binary for $name"
        exit 1
    fi

    # Install with correct lowercase name
    install -Dm755 "$bin" "$INSTALL_DIR/$name"

    rm -rf "$INSTALL_DIR/_tmp_$name"

    echo "  -> $name installed"
}

# -----------------------------
# Core native tools
# -----------------------------
publish_single "$ROOT/Fox16ASM/Fox16ASM.csproj" fox16asm
publish_single "$ROOT/FoxVision/FoxVision.csproj" foxvision
publish_single "$ROOT/VF16Decompiler/VF16Decompiler.csproj" vf16decompiler
publish_single "$ROOT/VF16Linker/VF16Linker.csproj" vf16linker

# -----------------------------
# FoxC (Go native static binary)
# -----------------------------
echo "[BUILD] FoxC (Go)"

pushd "$ROOT/FoxC" > /dev/null
CGO_ENABLED=0 go build -ldflags="-s -w" -o foxc ./cmd/foxc
popd > /dev/null

install -Dm755 "$ROOT/FoxC/foxc" "$INSTALL_DIR/foxc"

# -----------------------------
# Utilities
# -----------------------------
if [[ -f "$ROOT/tools/vf16obj_inspect.py" ]]; then
    install -Dm755 "$ROOT/tools/vf16obj_inspect.py" "$INSTALL_DIR/vf16obj_inspect.py"
fi
if [[ -f "$ROOT/tools/foxos_pack.py" ]]; then
    install -Dm755 "$ROOT/tools/foxos_pack.py" "$INSTALL_DIR/foxos_pack.py"
fi

# -----------------------------
# Optional target ROM
# -----------------------------
if [[ -n "$BUILD_ROM" ]]; then
    [[ -f "$ROOT/$BUILD_ROM" || -f "$BUILD_ROM" ]] ||
        { echo "ROM source not found: $BUILD_ROM" >&2; exit 1; }

    if [[ "$BUILD_ROM" != /* ]]; then
        BUILD_ROM="$ROOT/$BUILD_ROM"
    fi

    if [[ -z "$ROM_OUTPUT" ]]; then
        ROM_OUTPUT="$ROOT/build/$(basename "${BUILD_ROM%.*}").bin"
    elif [[ "$ROM_OUTPUT" != /* ]]; then
        ROM_OUTPUT="$ROOT/$ROM_OUTPUT"
    fi

    mkdir -p "$(dirname "$ROM_OUTPUT")"
    echo "[BUILD] ROM $(basename "$BUILD_ROM") -> $ROM_OUTPUT ($ROM_MODE)"
    case "${BUILD_ROM##*.}" in
        fc)
            PATH="$INSTALL_DIR:$PATH" "$INSTALL_DIR/foxc" \
                -i "$BUILD_ROM" \
                -o "$ROM_OUTPUT" \
                --mode "$ROM_MODE" \
                --load-address "$ROM_LOAD_ADDRESS" \
                --strict-format
            ;;
        f16)
            "$INSTALL_DIR/fox16asm" \
                -i "$BUILD_ROM" \
                -o "$ROM_OUTPUT" \
                --mode "$ROM_MODE" \
                --load-address "$ROM_LOAD_ADDRESS" \
                --strict-format
            ;;
        *)
            echo "unsupported ROM source: $BUILD_ROM (expected .fc or .f16)" >&2
            exit 1
            ;;
    esac

    [[ -s "$ROM_OUTPUT" ]] ||
        { echo "ROM build did not produce an output file: $ROM_OUTPUT" >&2; exit 1; }
    echo "  -> $ROM_OUTPUT"
fi

# -----------------------------
# Safety check (IMPORTANT)
# -----------------------------
echo ""
echo "== VERIFY BINARIES =="
ls -1 "$INSTALL_DIR"

echo ""
echo "== CHECK FOX16ASM NAME (CRITICAL) =="
file "$INSTALL_DIR/fox16asm" || true

echo ""
echo "== DONE =="
echo "Add to PATH:"
echo "  fish_add_path $INSTALL_DIR"