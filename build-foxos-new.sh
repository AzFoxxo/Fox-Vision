#!/usr/bin/env bash
# build-foxos-new.sh – thin wrapper that builds the VF16 toolchain first,
# then delegates to foxos/Makefile to compile and pack FoxOS + graphics demo.
#
# Usage:
#   ./build-foxos-new.sh [--install-dir PATH] [--output-dir PATH] [--run]
#
# Options:
#   --install-dir PATH   Where to install the VF16 host tools (default: ~/vf16/tools)
#   --output-dir  PATH   Override the foxos/ build output directory (default: foxos/build)
#   --run                Launch FoxVision immediately after a successful build
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
INSTALL_DIR="${INSTALL_DIR:-$HOME/vf16/tools}"
FOXOS_DIR="$SCRIPT_DIR/foxos"
OUTPUT_DIR="$FOXOS_DIR/build"
DO_RUN=0

usage() {
    sed -n '2,/^set /p' "$0" | grep -E '^#' | sed 's/^# \?//'
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --install-dir)
            [[ $# -ge 2 ]] || { echo "--install-dir requires a path" >&2; exit 2; }
            INSTALL_DIR="$2"; shift 2 ;;
        --output-dir)
            [[ $# -ge 2 ]] || { echo "--output-dir requires a path" >&2; exit 2; }
            OUTPUT_DIR="$2"; shift 2 ;;
        --run)
            DO_RUN=1; shift ;;
        -h|--help)
            usage; exit 0 ;;
        *)
            echo "unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
done

echo "============================================"
echo "  STEP 1 – Build & install VF16 toolchain"
echo "============================================"
"$SCRIPT_DIR/build.sh" --install-dir "$INSTALL_DIR"

echo ""
echo "============================================"
echo "  STEP 2 – Build FoxOS (kernel, shell, graphics demo)"
echo "============================================"
make -C "$FOXOS_DIR" \
    BUILD="$OUTPUT_DIR" \
    FOXC="$INSTALL_DIR/foxc" \
    TOOL_DIR="$INSTALL_DIR" \
    all

COMBINED="$OUTPUT_DIR/foxos-with-demo.bin"

echo ""
echo "============================================"
echo "  BUILD COMPLETE"
echo "============================================"
echo "Combined ROM : $COMBINED"
echo ""
echo "To launch manually:"
echo "  FOXVISION_PORT4_DEVICE=VF16TTY \\"
echo "  FOXVISION_PORT6_DEVICE=VF32HardDisk \\"
echo "  $INSTALL_DIR/foxvision --rom \"$COMBINED\""
echo ""
echo "At the FoxOS prompt:"
echo "  install          # copies graphics demo into /bin/graphics"
echo "  run /bin/graphics"

if [[ $DO_RUN -eq 1 ]]; then
    echo ""
    echo "============================================"
    echo "  STEP 3 – Launching FoxVision"
    echo "============================================"
    FOXVISION_PORT4_DEVICE=VF16TTY \
    FOXVISION_PORT6_DEVICE=VF32HardDisk \
    "$INSTALL_DIR/foxvision" --rom "$COMBINED"
fi
