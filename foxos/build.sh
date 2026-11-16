#!/usr/bin/env bash
# build.sh – build the FoxOS kernel image, shell image, and graphics demo.
#
# This is a thin wrapper around the Makefile so both entry points stay in
# sync. Pass any make target or variable override through, for example:
#
#   ./build.sh                 # build everything and pack the demo
#   ./build.sh os              # resident kernel only
#   ./build.sh shell           # bootable shell image only
#   ./build.sh TOOL_DIR=/opt/vf16/tools
set -euo pipefail

cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
exec make "$@"
