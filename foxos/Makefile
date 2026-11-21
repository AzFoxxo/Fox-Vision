# FoxOS Build System
# =============================================================================
# Builds the 32-bit FoxOS kernel, the shell image, the graphics demo, the
# editor, the Brainfuck interpreter, the Tiny BASIC interpreter and the
# f32asm in-OS assembler, then optionally packs the applications into the
# bootable shell image.
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
#   brainfuck.bin       - foreground Brainfuck interpreter ROM (0x100000)
#   basic.bin           - foreground Tiny BASIC interpreter ROM (0x100000)
#   f32asm.bin          - foreground f32 assembler ROM (0x100000)
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
#   make basic             # Tiny BASIC interpreter ROM only
#   make f32asm            # in-OS f32 assembler ROM only
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
BASIC_ROM  := $(BUILD)/basic.bin
F32ASM_ROM := $(BUILD)/f32asm.bin
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

# Resident images start at word 0 and now contain the full syscall ABI, so
# their text extends past the default $A000 global base. FoxC keeps their
# logical globals at $D000; the emulator's kernel data window (DB) relocates
# them above the text. User programs keep $A000 with their own DB base.
KERNEL_GLOBAL_BASE := 0xD000
RESIDENT_FLAGS   := $(FOXC_FLAGS) --global-base $(KERNEL_GLOBAL_BASE)

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
ABI_SRC         := kernel/abi.fc
ABI_HEADER      := abi.fh
FOXLIB          := foxlib.fc

APP_SRC  := apps/graphics_demo/graphics_demo.fc
EDIT_SRC := apps/edit/edit.fc
EDIT_CORE_SRC := apps/edit/edit_core.fc
BF_SRC   := apps/brainfuck/brainfuck.fc
BASIC_SRC := apps/basic/basic.fc
F32ASM_SRC := apps/f32asm/f32asm.fc

TEST_SRC      := tests/foxfs32_test.fc
TEST_ROM      := $(BUILD)/foxfs32_test.bin
TEST_DISK     := $(BUILD)/foxfs32-test.img
TEST_LOG      := $(BUILD)/foxfs32-test.log

ABI_USER_SRC  := tests/abi_user.fc
ABI_USER_ROM  := $(BUILD)/abi_user.bin
ABI_TEST_SRC  := tests/abi_test.fc
ABI_TEST_ROM  := $(BUILD)/abi_test.bin
ABI_TEST_BASE := $(BUILD)/abi_test_base.bin
ABI_TEST_DISK := $(BUILD)/abi-test.img
ABI_TEST_LOG  := $(BUILD)/abi-test.log

EDITOR_USER_SRC  := tests/editor_test_user.fc
EDITOR_USER_ROM  := $(BUILD)/editor_test_user.bin
EDITOR_TEST_SRC  := tests/editor_test.fc
EDITOR_TEST_ROM  := $(BUILD)/editor_test.bin
EDITOR_TEST_BASE := $(BUILD)/editor_test_base.bin
EDITOR_TEST_DISK := $(BUILD)/editor-test.img
EDITOR_TEST_LOG  := $(BUILD)/editor-test.log

EXEC_USER_SRC  := tests/exec_user.fc
EXEC_USER_ROM  := $(BUILD)/exec_user.bin
EXEC_TEST_SRC  := tests/exec_test.fc
EXEC_TEST_ROM  := $(BUILD)/exec_test.bin
EXEC_TEST_BASE := $(BUILD)/exec_test_base.bin
EXEC_TEST_DISK := $(BUILD)/exec-test.img
EXEC_TEST_LOG  := $(BUILD)/exec-test.log

# All inputs that should trigger a rebuild. The source stamp below compares
# content rather than timestamps, so changes are still picked up when an
# editor or checkout tool preserves an old modification time.
BUILD_INPUTS := $(sort $(OS_HEADER) $(KERNEL_SRC) $(KERNEL_API) \
                $(KERNEL_INTERNAL) $(FOXFS_SRC) $(FOXFS_API) $(FOXFS_INTERNAL) \
                $(BOOT_SRC) $(FOXSH_SRC) $(APP_SRC) $(EDIT_SRC) $(EDIT_CORE_SRC) $(BF_SRC) \
                $(BASIC_SRC) $(F32ASM_SRC) \
                $(TEST_SRC) $(ABI_USER_SRC) $(ABI_TEST_SRC) \
                $(EDITOR_USER_SRC) $(EDITOR_TEST_SRC) \
                $(EXEC_USER_SRC) $(EXEC_TEST_SRC) \
                $(ABI_SRC) $(ABI_HEADER) $(FOXLIB) \
                $(PACKER) $(MAKEFILE_LIST) $(CURDIR)/VERSION)
SOURCE_STAMP := $(BUILD)/.sources.sha256

# ---------------------------------------------------------------------------
.PHONY: all os kernel shell demo edit bf basic f32asm pack run check abi editor-check exec-check clean FORCE

all: os shell demo edit bf basic f32asm pack

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
		$(RESIDENT_FLAGS) \
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
		$(RESIDENT_FLAGS) \
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
# Tiny BASIC interpreter application ROM (loaded at 0x100000 by the OS)
# ---------------------------------------------------------------------------
basic: $(BASIC_ROM)

$(BASIC_ROM): $(BASIC_SRC) $(SOURCE_STAMP) | $(BUILD)
	@echo "==> Building basic (32-bit)"
	$(FOXC) \
		-i $(BASIC_SRC) \
		-o $@ \
		$(FOXC_FLAGS) \
		--load-address $(PROGRAM_BASE)
	@echo "    -> $(BASIC_ROM)"

# ---------------------------------------------------------------------------
# f32asm application ROM (loaded at 0x100000 by the OS)
#
# This assembler runs inside FoxOS: it reads an assembly source file from
# FoxFS and writes a bare type-1 program file that `run` can execute.
# ---------------------------------------------------------------------------
f32asm: $(F32ASM_ROM)

$(F32ASM_ROM): $(F32ASM_SRC) $(SOURCE_STAMP) | $(BUILD)
	@echo "==> Building f32asm (32-bit)"
	$(FOXC) \
		-i $(F32ASM_SRC) \
		-o $@ \
		$(FOXC_FLAGS) \
		--load-address $(PROGRAM_BASE)
	@echo "    -> $(F32ASM_ROM)"

# ---------------------------------------------------------------------------
# Pack: embed the applications into the bootable shell image
# ---------------------------------------------------------------------------
pack: $(COMBINED)

$(COMBINED): $(SHELL_ROM) $(APP_ROM) $(EDIT_ROM) $(BF_ROM) $(BASIC_ROM) $(F32ASM_ROM) $(SOURCE_STAMP) $(PACKER)
	@echo "==> Packing applications into the shell image"
	python3 $(PACKER) \
		$(SHELL_ROM) \
		--embed-program $(APP_ROM) \
		--embed-name graphics \
		--embed-program $(EDIT_ROM) \
		--embed-name edit \
		--embed-program $(BF_ROM) \
		--embed-name bf \
		--embed-program $(BASIC_ROM) \
		--embed-name basic \
		--embed-program $(F32ASM_ROM) \
		--embed-name f32asm \
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
check: $(TEST_ROM) $(ABI_TEST_ROM) $(EDITOR_TEST_ROM) $(EXEC_TEST_ROM)
	@echo "==> Running 32-bit FoxFS test"
	rm -f $(TEST_DISK)
	FOXVISION_HEADLESS=1 \
	FOXVISION_PORT6_DEVICE=VF32HardDisk \
	FOXVISION_PORT6_HARDDISK_PATH=$(TEST_DISK) \
	$(FOXVISION) --headless --rom $(TEST_ROM) > $(TEST_LOG) 2>&1
	@grep -q '^PASS' $(TEST_LOG) || (echo "FoxFS test FAILED"; tail -40 $(TEST_LOG); exit 1)
	@echo "    -> PASS"
	@echo "==> Running syscall/process ABI test"
	rm -f $(ABI_TEST_DISK)
	FOXVISION_HEADLESS=1 \
	FOXVISION_PORT6_DEVICE=VF32HardDisk \
	FOXVISION_PORT6_HARDDISK_PATH=$(ABI_TEST_DISK) \
	$(FOXVISION) --headless --rom $(ABI_TEST_ROM) > $(ABI_TEST_LOG) 2>&1
	@grep -q 'PASS ABI' $(ABI_TEST_LOG) || (echo "ABI test FAILED"; tail -60 $(ABI_TEST_LOG); exit 1)
	@grep -q 'FAIL' $(ABI_TEST_LOG) && (echo "ABI test FAILED"; tail -60 $(ABI_TEST_LOG); exit 1) || true
	@echo "    -> PASS"
	@echo "==> Running editor ABI test"
	rm -f $(EDITOR_TEST_DISK)
	FOXVISION_HEADLESS=1 \
	FOXVISION_PORT6_DEVICE=VF32HardDisk \
	FOXVISION_PORT6_HARDDISK_PATH=$(EDITOR_TEST_DISK) \
	$(FOXVISION) --headless --rom $(EDITOR_TEST_ROM) > $(EDITOR_TEST_LOG) 2>&1
	@grep -q 'PASS EDITOR' $(EDITOR_TEST_LOG) || (echo "Editor test FAILED"; tail -60 $(EDITOR_TEST_LOG); exit 1)
	@grep -q 'FAIL' $(EDITOR_TEST_LOG) && (echo "Editor test FAILED"; tail -60 $(EDITOR_TEST_LOG); exit 1) || true
	@echo "    -> PASS"
	@echo "==> Running disk-load exec test"
	rm -f $(EXEC_TEST_DISK)
	FOXVISION_HEADLESS=1 \
	FOXVISION_PORT6_DEVICE=VF32HardDisk \
	FOXVISION_PORT6_HARDDISK_PATH=$(EXEC_TEST_DISK) \
	$(FOXVISION) --headless --rom $(EXEC_TEST_ROM) > $(EXEC_TEST_LOG) 2>&1
	@grep -q 'PASS EXEC' $(EXEC_TEST_LOG) || (echo "Exec test FAILED"; tail -60 $(EXEC_TEST_LOG); exit 1)
	@grep -q 'FAIL' $(EXEC_TEST_LOG) && (echo "Exec test FAILED"; tail -60 $(EXEC_TEST_LOG); exit 1) || true
	@echo "    -> PASS"

# Build the embedded foreground ABI test program (load 0x100000).
abi: $(ABI_TEST_ROM)

# Editor application ABI integration test.
editor-check: $(EDITOR_TEST_ROM)

# External-program disk-load regression test.
exec-check: $(EXEC_TEST_ROM)

$(ABI_USER_ROM): $(ABI_USER_SRC) $(SOURCE_STAMP) | $(BUILD)
	@echo "==> Building ABI user program"
	$(FOXC) \
		-i $(ABI_USER_SRC) \
		-o $@ \
		$(FOXC_FLAGS) \
		--load-address $(PROGRAM_BASE)
	@echo "    -> $(ABI_USER_ROM)"

# Build the resident harness, then embed the user program in its image.
$(ABI_TEST_ROM): $(ABI_TEST_SRC) $(ABI_USER_ROM) $(SOURCE_STAMP) $(PACKER) | $(BUILD)
	@echo "==> Building ABI test harness"
	$(FOXC) \
		-i $(ABI_TEST_SRC) \
		-o $(ABI_TEST_BASE) \
		$(RESIDENT_FLAGS) \
		--load-address 0
	@echo "==> Embedding ABI user program"
	python3 $(PACKER) \
		$(ABI_TEST_BASE) \
		--embed-program $(ABI_USER_ROM) \
		--embed-name abiuser \
		--embed-address 0x20000 \
		--output $@
	@echo "    -> $(ABI_TEST_ROM)"

# Build the embedded foreground editor test program (load 0x100000).
$(EDITOR_USER_ROM): $(EDITOR_USER_SRC) $(EDIT_CORE_SRC) $(SOURCE_STAMP) | $(BUILD)
	@echo "==> Building editor ABI user program"
	$(FOXC) \
		-i $(EDITOR_USER_SRC) \
		-o $@ \
		$(FOXC_FLAGS) \
		--load-address $(PROGRAM_BASE)
	@echo "    -> $(EDITOR_USER_ROM)"

# Build the resident harness, then embed the editor test program in its image.
$(EDITOR_TEST_ROM): $(EDITOR_TEST_SRC) $(EDITOR_USER_ROM) $(SOURCE_STAMP) $(PACKER) | $(BUILD)
	@echo "==> Building editor ABI test harness"
	$(FOXC) \
		-i $(EDITOR_TEST_SRC) \
		-o $(EDITOR_TEST_BASE) \
		$(RESIDENT_FLAGS) \
		--load-address 0
	@echo "==> Embedding editor test user program"
	python3 $(PACKER) \
		$(EDITOR_TEST_BASE) \
		--embed-program $(EDITOR_USER_ROM) \
		--embed-name editort \
		--embed-address 0x20000 \
		--output $@
	@echo "    -> $(EDITOR_TEST_ROM)"

# Build the embedded foreground disk-load test program (load 0x100000).
$(EXEC_USER_ROM): $(EXEC_USER_SRC) $(SOURCE_STAMP) | $(BUILD)
	@echo "==> Building exec test user program"
	$(FOXC) \
		-i $(EXEC_USER_SRC) \
		-o $@ \
		$(FOXC_FLAGS) \
		--load-address $(PROGRAM_BASE)
	@echo "    -> $(EXEC_USER_ROM)"

# Build the resident harness, then embed the program stored in FoxFS.
$(EXEC_TEST_ROM): $(EXEC_TEST_SRC) $(EXEC_USER_ROM) $(SOURCE_STAMP) $(PACKER) | $(BUILD)
	@echo "==> Building exec test harness"
	$(FOXC) \
		-i $(EXEC_TEST_SRC) \
		-o $(EXEC_TEST_BASE) \
		$(RESIDENT_FLAGS) \
		--load-address 0
	@echo "==> Embedding exec test user program"
	python3 $(PACKER) \
		$(EXEC_TEST_BASE) \
		--embed-program $(EXEC_USER_ROM) \
		--embed-name exect \
		--embed-address 0x20000 \
		--output $@
	@echo "    -> $(EXEC_TEST_ROM)"

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
