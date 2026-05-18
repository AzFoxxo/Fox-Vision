#!/usr/bin/env bash
set -euo pipefail

ROOT="$(pwd)"
INSTALL_DIR="$HOME/vf16/tools"

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
publish_single Fox16ASM/Fox16ASM.csproj fox16asm
publish_single FoxVision/FoxVision.csproj foxvision
publish_single VF16Decompiler/VF16Decompiler.csproj vf16decompiler
publish_single VF16Linker/VF16Linker.csproj vf16linker

# -----------------------------
# FoxC (Go native static binary)
# -----------------------------
echo "[BUILD] FoxC (Go)"

pushd FoxC > /dev/null
CGO_ENABLED=0 go build -ldflags="-s -w" -o foxc ./cmd/foxc
popd > /dev/null

install -Dm755 FoxC/foxc "$INSTALL_DIR/foxc"

# -----------------------------
# Utilities
# -----------------------------
if [[ -f tools/vf16obj_inspect.py ]]; then
    install -Dm755 tools/vf16obj_inspect.py "$INSTALL_DIR/vf16obj_inspect.py"
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