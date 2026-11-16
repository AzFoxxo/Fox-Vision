# FoxOS Build System
# =============================================================================
# Builds the FoxOS kernel, the shell image, and the graphics demo, then
# optionally packs the demo into the bootable shell image.
#
# The resident kernel (kernel + foxfs) and the shell (foxsh) are separate
# images: foxsh is never compiled into foxos.bin.
#
# Directory layout produced in build/:
#   foxos.bin           - resident kernel ROM (kernel + foxfs), load address 0
#   foxsh.bin           - shell image (kernel + foxfs + foxsh), load address 0
#   graphics_demo.bin   - foreground application ROM (load address 0x4000)
#   foxos-with-demo.bin - bootable shell image with the demo embedded
#
# Prerequisites (must be on PATH or set via TOOL_DIR):
#   foxc          - FoxC compiler  (builds from /Fox-Vision/FoxC)
#   fox16asm      - Fox16 assembler (used by foxc)
#   foxos_pack.py - ROM packer     (from /Fox-Vision/tools/)
#
# Usage:
#   make                   # build kernel, shell, demo, and combined ROM
#   make os                # resident kernel ROM only
#   make shell             # bootable shell image only
#   make demo              # graphics demo ROM only
#   make pack              # embed demo into the shell image
#   make clean             # remove build artifacts
#   make run               # launch FoxVision with the combined ROM
#   make FOXC=/path/foxc   # override foxc binary location
# =============================================================================

SHELL   := /bin/bash
ROOT    := $(shell cd .. && pwd)
TOOL_DIR ?= $(HOME)/vf16/tools
FOXC    ?= $(TOOL_DIR)/foxc
PACKER  ?= $(ROOT)/tools/foxos_pack.py

# foxc invokes fox16asm from PATH.
export PATH := $(TOOL_DIR):$(PATH)

BUILD      := $(CURDIR)/build
KERNEL_ROM := $(BUILD)/foxos.bin
SHELL_ROM  := $(BUILD)/foxsh.bin
APP_ROM    := $(BUILD)/graphics_demo.bin
COMBINED   := $(BUILD)/foxos-with-demo.bin

# FoxC flags
MODE    := extended
FOXC_FLAGS := --mode $(MODE) --strict-format

# FoxC accepts a single input file, so related sources are concatenated in
# dependency order before compilation.
#
# Resident kernel: kernel and filesystem only. boot.fc supplies main().
KERNEL_SRC := kernel/kernel.fc
FOXFS_SRC  := foxfs/foxfs.fc
BOOT_SRC   := kernel/boot.fc
KERNEL_IMAGE_SRCS := $(KERNEL_SRC) $(FOXFS_SRC) $(BOOT_SRC)

# Shell image: kernel and filesystem are support code for the shell, which
# is the top-level translation unit and supplies main().
FOXSH_SRC := foxsh/foxsh.fc
SHELL_IMAGE_SRCS := $(KERNEL_SRC) $(FOXFS_SRC) $(FOXSH_SRC)

APP_SRC := apps/graphics_demo/graphics_demo.fc

KERNEL_IMAGE := $(BUILD)/foxos-image.fc
SHELL_IMAGE  := $(BUILD)/foxsh-image.fc

# ---------------------------------------------------------------------------
.PHONY: all os kernel shell demo pack run clean

all: os shell demo pack

# ---------------------------------------------------------------------------
# Resident kernel ROM: kernel + foxfs (no shell)
# ---------------------------------------------------------------------------
os kernel: $(KERNEL_ROM)

$(KERNEL_IMAGE): $(KERNEL_IMAGE_SRCS) | $(BUILD)
	@echo "==> Combining resident kernel sources"
	cat $^ > $@

$(KERNEL_ROM): $(KERNEL_IMAGE)
	@echo "==> Building FoxOS kernel + foxfs"
	$(FOXC) \
		-i $< \
		-o $@ \
		$(FOXC_FLAGS) \
		--load-address 0
	@echo "    -> $(KERNEL_ROM)"

# ---------------------------------------------------------------------------
# Shell image: kernel + foxfs + foxsh
# ---------------------------------------------------------------------------
shell: $(SHELL_ROM)

$(SHELL_IMAGE): $(SHELL_IMAGE_SRCS) | $(BUILD)
	@echo "==> Combining shell image sources"
	cat $^ > $@

$(SHELL_ROM): $(SHELL_IMAGE)
	@echo "==> Building FoxSH shell image"
	$(FOXC) \
		-i $< \
		-o $@ \
		$(FOXC_FLAGS) \
		--load-address 0
	@echo "    -> $(SHELL_ROM)"

# ---------------------------------------------------------------------------
# Graphics demo application ROM (loaded at 0x4000 by the OS)
# ---------------------------------------------------------------------------
demo: $(APP_ROM)

$(APP_ROM): $(APP_SRC) | $(BUILD)
	@echo "==> Building graphics_demo"
	$(FOXC) \
		-i $(APP_SRC) \
		-o $@ \
		$(FOXC_FLAGS) \
		--load-address 0x4000
	@echo "    -> $(APP_ROM)"

# ---------------------------------------------------------------------------
# Pack: embed demo into the bootable shell image
# ---------------------------------------------------------------------------
pack: $(COMBINED)

$(COMBINED): $(SHELL_ROM) $(APP_ROM)
	@echo "==> Packing graphics_demo into the shell image"
	python3 $(PACKER) \
		$(SHELL_ROM) \
		--embed-program $(APP_ROM) \
		--embed-name graphics \
		--output $(COMBINED)
	@echo "    -> $(COMBINED)"

# ---------------------------------------------------------------------------
# Run FoxVision
# ---------------------------------------------------------------------------
run: $(COMBINED)
	@echo "==> Launching FoxVision"
	FOXVISION_PORT4_DEVICE=VF16TTY \
	FOXVISION_PORT5_DEVICE=VF16Flash \
	$(TOOL_DIR)/foxvision --rom $(COMBINED)

# ---------------------------------------------------------------------------
# Housekeeping
# ---------------------------------------------------------------------------
$(BUILD):
	mkdir -p $@

clean:
	rm -rf $(BUILD)
	@echo "clean."
