#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
INSTALL_DIR="${INSTALL_DIR:-$HOME/vf16/tools}"
OUTPUT_DIR="$ROOT/build"
OS_SOURCE="$ROOT/FoxC/examples/foxos.fc"
PROGRAM_SOURCE="$ROOT/FoxC/examples/graphics_demo.fc"
PROGRAM_NAME="graphics"

usage() {
    cat <<'EOF'
Usage: ./build-foxos.sh [options]

Build the FoxOS ROM, build a foreground program for $4000, and embed that
program into the combined ROM used by FoxVision.

Options:
  --program <source>       Foreground .fc or .f16 source
                           (default: FoxC/examples/graphics_demo.fc)
  --program-name <name>    Name to create under /bin (1-8 ASCII characters)
                           (default: graphics)
  --output-dir <path>      Generated ROM directory (default: ./build)
  --install-dir <path>     Host tool installation directory
  -h, --help               Show this help
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --program)
            [[ $# -ge 2 ]] || { echo "--program requires a source file" >&2; exit 2; }
            PROGRAM_SOURCE="$2"
            shift 2
            ;;
        --output-dir)
            [[ $# -ge 2 ]] || { echo "--output-dir requires a path" >&2; exit 2; }
            OUTPUT_DIR="$2"
            shift 2
            ;;
        --program-name)
            [[ $# -ge 2 ]] || { echo "--program-name requires a name" >&2; exit 2; }
            PROGRAM_NAME="$2"
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

[[ -f "$OS_SOURCE" ]] || { echo "OS source not found: $OS_SOURCE" >&2; exit 1; }
[[ -f "$ROOT/$PROGRAM_SOURCE" || -f "$PROGRAM_SOURCE" ]] ||
    { echo "program source not found: $PROGRAM_SOURCE" >&2; exit 1; }

if [[ "$PROGRAM_SOURCE" != /* ]]; then
    PROGRAM_SOURCE="$ROOT/$PROGRAM_SOURCE"
fi

mkdir -p "$OUTPUT_DIR"
OS_ROM="$OUTPUT_DIR/foxxo-os.bin"
PROGRAM_ROM="$OUTPUT_DIR/$(basename "${PROGRAM_SOURCE%.*}").bin"

echo "== BUILD FOXOS SYSTEM IMAGE =="
"$ROOT/build.sh" \
    --install-dir "$INSTALL_DIR" \
    --build-rom "$OS_SOURCE" \
    --rom-output "$OS_ROM" \
    --mode extended

echo "== BUILD FOREGROUND PROGRAM =="
case "${PROGRAM_SOURCE##*.}" in
    fc)
        PATH="$INSTALL_DIR:$PATH" "$INSTALL_DIR/foxc" \
            -i "$PROGRAM_SOURCE" \
            -o "$PROGRAM_ROM" \
            --mode extended \
            --load-address 0x4000 \
            --strict-format
        ;;
    f16)
        "$INSTALL_DIR/fox16asm" \
            -i "$PROGRAM_SOURCE" \
            -o "$PROGRAM_ROM" \
            --mode extended \
            --load-address 0x4000 \
            --strict-format
        ;;
    *)
        echo "unsupported program source: $PROGRAM_SOURCE (expected .fc or .f16)" >&2
        exit 1
        ;;
esac

echo "== PACK FOREGROUND PROGRAM =="
TEMP_OS_ROM="$OUTPUT_DIR/.foxxo-os-with-program.bin"
python3 "$ROOT/tools/foxos_pack.py" \
    "$OS_ROM" \
    --embed-program "$PROGRAM_ROM" \
    --embed-name "$PROGRAM_NAME" \
    --output "$TEMP_OS_ROM"
mv "$TEMP_OS_ROM" "$OS_ROM"

echo ""
echo "== FOXOS BUILD COMPLETE =="
echo "OS ROM:      $OS_ROM"
echo "Program ROM: $PROGRAM_ROM"
echo ""
echo "Start FoxVision with:"
echo "  FOXVISION_PORT4_DEVICE=VF16TTY \\"
echo "  FOXVISION_PORT5_DEVICE=VF16Flash \\"
echo "  $INSTALL_DIR/foxvision --rom \"$OS_ROM\""
echo ""
echo "At the FoxOS prompt, run 'install' once, then:"
echo "  run /bin/$PROGRAM_NAME"
