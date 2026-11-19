# FoxOS Build System
# =============================================================================
# Builds the 32-bit FoxOS kernel, the shell image, the graphics demo and the
# editor, then optionally packs the applications into the bootable shell image.
#
# FoxOS targets the V2.0 ISA in EM=2 with word addressing (AM=0). Storage is a
# 4 GiB VF32HardDisk cartridge on port 6, addressed as 32-bit words.
#
# The resident kernel (kernel + foxfs) and the shell (foxsh) are separate
# images: foxsh is never compiled into foxos.bin.
#
# Directory layout produced in build/:
#   foxos.bin           - resident kernel ROM (kernel + foxfs), load address 0
#   foxsh.bin           - shell image (kernel + foxfs + foxsh), load address 0
#   graphics_demo.bin   - foreground application ROM (load address 0x100000)
#   edit.bin            - foreground editor ROM (load address 0x100000)
#   foxos-with-demo.bin - bootable shell image with the applications embedded
#
# Prerequisites (must be on PATH or set via TOOL_DIR):
#   foxc          - FoxC compiler  (builds from /Fox-Vision/FoxC)
#   fox16asm      - Fox16 assembler (used by foxc)
#   foxos_pack.py - ROM packer     (from /Fox-Vision/tools/)
#
# Usage:
#   make                   # build kernel, shell, demo, edit, and combined ROM
#   make os                # resident kernel ROM only
#   make shell             # bootable shell image only
#   make demo              # graphics demo ROM only
#   make edit              # editor ROM only
#   make bf                # brainfuck interpreter ROM only
#   make pack              # embed the applications into the shell image
#   make check             # run the 32-bit FoxFS regression test
#   make clean             # remove build artifacts
#   make run               # launch FoxVision with the combined ROM
#   make FOXC=/path/foxc   # override foxc binary location
# =============================================================================

SHELL   := /bin/bash
ROOT    := $(shell cd .. && pwd)
TOOL_DIR ?= $(HOME)/vf16/tools
FOXC    ?= $(TOOL_DIR)/foxc
FOXVISION ?= $(TOOL_DIR)/foxvision
PACKER  ?= $(ROOT)/tools/foxos_pack.py

# foxc invokes fox16asm from PATH.
export PATH := $(TOOL_DIR):$(PATH)

BUILD      := $(CURDIR)/build
KERNEL_ROM := $(BUILD)/foxos.bin
SHELL_ROM  := $(BUILD)/foxsh.bin
APP_ROM    := $(BUILD)/graphics_demo.bin
EDIT_ROM   := $(BUILD)/edit.bin
BF_ROM     := $(BUILD)/brainfuck.bin
COMBINED   := $(BUILD)/foxos-with-demo.bin

# FoxOS release version (also shown by the `version` shell command)
FOXOS_VERSION := $(shell cat $(CURDIR)/VERSION)

# FoxC flags
MODE        := em2_am1
# Mapper 2 is the 32-bit ROM mapper (RomByte8_32). ROM header v3 carries the
# 32-bit load address and word count that go with it.
MAPPER      := 2
ROM_VERSION := 3
FOXC_FLAGS  := --mode $(MODE) --mapper $(MAPPER) --rom-version $(ROM_VERSION) --strict-format -I $(CURDIR)
# -I $(CURDIR) makes <foxos.fh> available to every image and application.

# Foreground programs execute in the FoxOS 32-bit address space.
PROGRAM_BASE := 0x100000

# Applications include <foxos.fh>, which exposes only the public kernel and
# FoxFS API. The kernel, shell and test images include the full modules
# (public API + internal/OS-only helpers). Each entry file supplies main().
KERNEL_SRC      := kernel/kernel.fc
KERNEL_API      := kernel/kernel_api.fc
KERNEL_INTERNAL := kernel/kernel_internal.fc
FOXFS_SRC       := foxfs/foxfs.fc
FOXFS_API       := foxfs/foxfs_api.fc
FOXFS_INTERNAL  := foxfs/foxfs_internal.fc
OS_HEADER       := foxos.fh
BOOT_SRC        := kernel/boot.fc
FOXSH_SRC       := foxsh/foxsh.fc

APP_SRC  := apps/graphics_demo/graphics_demo.fc
EDIT_SRC := apps/edit/edit.fc
BF_SRC   := apps/brainfuck/brainfuck.fc

TEST_SRC      := tests/foxfs32_test.fc
TEST_ROM      := $(BUILD)/foxfs32_test.bin
TEST_DISK     := $(BUILD)/foxfs32-test.img
TEST_LOG      := $(BUILD)/foxfs32-test.log

# All inputs that should trigger a rebuild. The source stamp below compares
# content rather than timestamps, so changes are still picked up when an
# editor or checkout tool preserves an old modification time.
BUILD_INPUTS := $(sort $(OS_HEADER) $(KERNEL_SRC) $(KERNEL_API) \
                $(KERNEL_INTERNAL) $(FOXFS_SRC) $(FOXFS_API) $(FOXFS_INTERNAL) \
                $(BOOT_SRC) $(FOXSH_SRC) $(APP_SRC) $(EDIT_SRC) $(BF_SRC) \
                $(TEST_SRC) $(PACKER) $(MAKEFILE_LIST) $(CURDIR)/VERSION)
SOURCE_STAMP := $(BUILD)/.sources.sha256

# ---------------------------------------------------------------------------
.PHONY: all os kernel shell demo edit bf pack run check clean FORCE

all: os shell demo edit bf pack

# Refresh the content stamp only when an input actually changes. Recipes are
# silent so a no-op make stays quiet.
$(SOURCE_STAMP): $(BUILD_INPUTS) FORCE | $(BUILD)
	@sha256sum $(BUILD_INPUTS) > $@.tmp
	@if cmp -s $@.tmp $@; then rm -f $@.tmp; else mv $@.tmp $@; echo "==> Sources changed"; fi

# ---------------------------------------------------------------------------
# Resident kernel ROM: boot entry + full kernel + foxfs (no shell)
# ---------------------------------------------------------------------------
os kernel: $(KERNEL_ROM)

$(KERNEL_ROM): $(BOOT_SRC) $(SOURCE_STAMP) | $(BUILD)
	@echo "==> Building FoxOS $(FOXOS_VERSION) kernel + foxfs"
	$(FOXC) \
		-i $(BOOT_SRC) \
		-o $@ \
		$(FOXC_FLAGS) \
		--load-address 0
	@echo "    -> $(KERNEL_ROM)"

# ---------------------------------------------------------------------------
# Shell image: foxsh + full kernel + foxfs support code
# ---------------------------------------------------------------------------
shell: $(SHELL_ROM)

$(SHELL_ROM): $(FOXSH_SRC) $(SOURCE_STAMP) | $(BUILD)
	@echo "==> Building FoxSH $(FOXOS_VERSION) shell image"
	$(FOXC) \
		-i $(FOXSH_SRC) \
		-o $@ \
		$(FOXC_FLAGS) \
		--load-address 0
	@echo "    -> $(SHELL_ROM)"

# ---------------------------------------------------------------------------
# Graphics demo application ROM (loaded at 0x100000 by the OS)
# ---------------------------------------------------------------------------
demo: $(APP_ROM)

$(APP_ROM): $(APP_SRC) $(SOURCE_STAMP) | $(BUILD)
	@echo "==> Building graphics_demo (32-bit)"
	$(FOXC) \
		-i $(APP_SRC) \
		-o $@ \
		$(FOXC_FLAGS) \
		--load-address $(PROGRAM_BASE)
	@echo "    -> $(APP_ROM)"

# ---------------------------------------------------------------------------
# Editor application ROM (loaded at 0x100000 by the OS)
#
# The editor includes <foxos.fh> for the kernel terminal and FoxFS helpers,
# so it is built directly from edit.fc.
# ---------------------------------------------------------------------------
edit: $(EDIT_ROM)

$(EDIT_ROM): $(EDIT_SRC) $(SOURCE_STAMP) | $(BUILD)
	@echo "==> Building edit (32-bit)"
	$(FOXC) \
		-i $(EDIT_SRC) \
		-o $@ \
		$(FOXC_FLAGS) \
		--load-address $(PROGRAM_BASE)
	@echo "    -> $(EDIT_ROM)"

# ---------------------------------------------------------------------------
# Brainfuck interpreter application ROM (loaded at 0x100000 by the OS)
# ---------------------------------------------------------------------------
bf: $(BF_ROM)

$(BF_ROM): $(BF_SRC) $(SOURCE_STAMP) | $(BUILD)
	@echo "==> Building bf (32-bit)"
	$(FOXC) \
		-i $(BF_SRC) \
		-o $@ \
		$(FOXC_FLAGS) \
		--load-address $(PROGRAM_BASE)
	@echo "    -> $(BF_ROM)"

# ---------------------------------------------------------------------------
# Pack: embed the applications into the bootable shell image
# ---------------------------------------------------------------------------
pack: $(COMBINED)

$(COMBINED): $(SHELL_ROM) $(APP_ROM) $(EDIT_ROM) $(BF_ROM) $(SOURCE_STAMP) $(PACKER)
	@echo "==> Packing applications into the shell image"
	python3 $(PACKER) \
		$(SHELL_ROM) \
		--embed-program $(APP_ROM) \
		--embed-name graphics \
		--embed-program $(EDIT_ROM) \
		--embed-name edit \
		--embed-program $(BF_ROM) \
		--embed-name bf \
		--embed-address 0x20000 \
		--output $(COMBINED)
	@echo "    -> $(COMBINED)"

# ---------------------------------------------------------------------------
# Run FoxVision
# ---------------------------------------------------------------------------
run: $(COMBINED)
	@echo "==> Launching FoxVision"
	FOXVISION_PORT4_DEVICE=VF16TTY \
	FOXVISION_PORT6_DEVICE=VF32HardDisk \
	$(FOXVISION) --rom $(COMBINED)

# ---------------------------------------------------------------------------
# FoxFS 32-bit regression test (test main + full kernel + foxfs)
# ---------------------------------------------------------------------------
check: $(TEST_ROM)
	@echo "==> Running 32-bit FoxFS test"
	rm -f $(TEST_DISK)
	FOXVISION_HEADLESS=1 \
	FOXVISION_PORT6_DEVICE=VF32HardDisk \
	FOXVISION_PORT6_HARDDISK_PATH=$(TEST_DISK) \
	$(FOXVISION) --headless --rom $(TEST_ROM) > $(TEST_LOG) 2>&1
	@grep -q '^PASS' $(TEST_LOG) || (echo "FoxFS test FAILED"; tail -40 $(TEST_LOG); exit 1)
	@echo "    -> PASS"

$(TEST_ROM): $(TEST_SRC) $(SOURCE_STAMP) | $(BUILD)
	@echo "==> Building 32-bit FoxFS test"
	$(FOXC) \
		-i $(TEST_SRC) \
		-o $@ \
		$(FOXC_FLAGS) \
		--load-address 0
	@echo "    -> $(TEST_ROM)"

# ---------------------------------------------------------------------------
# Housekeeping
# ---------------------------------------------------------------------------
FORCE:

$(BUILD):
	mkdir -p $@

clean:
	rm -rf $(BUILD)
	@echo "clean."
