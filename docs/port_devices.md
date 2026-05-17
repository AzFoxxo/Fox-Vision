# Port Devices Specification

Fox Vision exposes port-based I/O for input and output devices when running in extension mode (`EM = 0x0001`).

## Overview

**NOTE:** V1.10 (`EM=1` mode) introduces ports. When enabled, the system exposes eight generic 16-bit I/O ports (`0x0000`–`0x0007`). Ports are simple bidirectional data endpoints with no fixed semantic meaning in the ISA. Device behaviour is defined externally by the emulator/system configuration.

Port I/O is not available in legacy mode. Programs must initialize extension mode by writing `0x0001` to the `EM` register:

```
MOV %1 EM
```

### Port Characteristics

- Eight ports are exposed
- Each port is 16 bits wide
- Port numbers are encoded as 16-bit immediate values in the range `0x0000` to `0x0007`
- Ports are not memory-mapped
- Ports do not latch, queue, clear, or acknowledge input. They always return the most recent device state.
- Device state (including controllers) is maintained continuously by the emulator as a live snapshot

## Port I/O Instructions (V1.10)

- `0000 0000` `0011 0000` - `IN` `PORT DST` - Read the 16-bit value from port `PORT` into register `DST`
- `0000 0000` `0011 0001` - `OUT` `SRC PORT` - Write the 16-bit value from register `SRC` to port `PORT`

Each `IN` instruction reads the current state of the connected device at the time of execution.

## Default Emulator Port Convention

The following mapping is the emulator's convention, not a CPU requirement. Device behaviour remains implementation-defined. Ports `0x0006` through `0x0007` are available for additional emulator-defined devices or custom mappings.

- PORT0 (`0x0000`) - VF16Pad Player 1
- PORT1 (`0x0001`) - VF16Pad Player 2
- PORT2 (`0x0002`) - VF16Keyboard
- PORT3 (`0x0003`) - VF16Mouse
- PORT4 (`0x0004`) - VF16TTY
- PORT5 (`0x0005`) - VF16Flash

---

## VF16Pad Controller

### Port Layout

Controller state is a level-based snapshot exposed by a configured port device:

- `1` = Button is currently held
- `0` = Button is not held

State is continuously updated by the emulator from host input events.

Bit layout:

| Bit  | Button   |
| ---- | -------- |
| 0    | Up       |
| 1    | Down     |
| 2    | Left     |
| 3    | Right    |
| 4    | A        |
| 5    | B        |
| 6    | Start    |
| 7    | Select   |
| 8-15 | Reserved |

### Behaviour

- The device returns a level-based snapshot of currently pressed buttons.
- Buttons are represented as discrete bits; combinations behave predictably (e.g., pressing Up + Right sets both bits).
- State is continuously updated by the emulator from host input events.
- Reads do not clear or acknowledge the state.

### Examples

- `00000000` = No buttons held
- `00010000` = A held
- `01000000` = Start held
- `00001001` = Up + Right held

---

## VF16Keyboard

### Port Layout

The `VF16Keyboard` port device encodes a conventional keyboard report into a single 16-bit value suitable for a 16-bit port bus. The encoding mirrors the common USB HID keyboard report's modifier + usage organisation so ROMs and host mappings can be consistent with existing keyboard tooling.

Format (bits):

- Bits 0–7 (low byte): USB HID usage ID (keycode) for a single non-modifier key (0x00 = none). Usage IDs follow the USB HID Usage Tables for keyboards (e.g. `0x04` = `A`, `0x05` = `B`, `0x28` = Enter).
- Bits 8–15 (high byte): Modifier bitmap (same bit positions as USB HID modifiers):

| Bit | Modifier | Meaning                  |
| --- | -------- | ------------------------ |
| 8   | LCTRL    | Left Control             |
| 9   | LSHIFT   | Left Shift               |
| 10  | LALT     | Left Alt                 |
| 11  | LGUI     | Left GUI / Meta / OS key |
| 12  | RCTRL    | Right Control            |
| 13  | RSHIFT   | Right Shift              |
| 14  | RALT     | Right Alt / AltGr        |
| 15  | RGUI     | Right GUI / Meta         |

### Behaviour

- The keyboard port provides a stateless snapshot: each `IN` returns the current modifier mask and a single representative usage ID (no event queueing).
- When multiple non-modifier keys are held simultaneously, the emulator selects a single usage ID to expose (recommendation: the most-recently-pressed non-modifier key). ROM authors should not rely on full N-key rollover via this 16-bit encoding.
- Modifiers are independent bits; combinations behave predictably (for example Left Shift + `A` uses usage `0x04` with `LSHIFT` bit set in the high byte).
- Control and non-printable keys use their HID usage IDs (for example `0x28` = Enter, `0x2A` = Backspace, `0x2B` = Tab, `0x29` = Escape). When no non-modifier key is held the low byte is `0x00`.

### Port Mapping and Configuration

- Map `VF16Keyboard` via the same `FOXVISION_PORT<N>_DEVICE` environment variables (for example `FOXVISION_PORT0_DEVICE=VF16Keyboard`).
- Host-to-HID mapping (layouts, key repeat, dead-keys) is implementation-defined; the emulator may expose `FOXVISION_KEYBOARD_LAYOUT` or GUI settings to control translation (recommended: follow platform-native layout mapping to HID usage IDs).

### Programming Model

- Use `IN PORT DST` to read the 16-bit keyboard report. Inspect the low byte for the HID usage ID and the high byte for modifiers.
- Implement debouncing, key-repeat, and composition logic in the ROM if needed — the port provides instantaneous state only.

### Examples

- `0x0000` — No key held
- `0x0004` — `A` held (HID usage `0x04`, no modifiers)
- `0x0204` — `A` held with Left Shift (modifier bit 9 => 0x02 << 8 = 0x0200; combined value `0x0204`)
- `0x2800` — Enter held (HID usage `0x28`, no modifiers)

---

## VF16Mouse

### Port Layout

The `VF16Mouse` port device exposes a single 16-bit packed snapshot so it behaves like the other devices: one port, one read.

Port layout (`PORT N`):
- Bits 0-2: Buttons
  - Bit 0: Left button
  - Bit 1: Right button
  - Bit 2: Middle button
- Bit 3: Wheel up flag
- Bit 4: Wheel down flag
- Bits 5-9: X delta, signed 5-bit two's complement (`-16..15`)
- Bits 10-14: Y delta, signed 5-bit two's complement (`-16..15`)
- Bit 15: Reserved

### Behaviour

- The device returns the current snapshot of buttons and relative motion in a single read.
- X/Y deltas are clamped to `-16..15` for the packed report.
- The wheel is represented as direction flags rather than a magnitude. Positive scroll sets bit 3, negative scroll sets bit 4.
- Reads do not clear or acknowledge the state.

### Platform Compatibility

- USB HID and PS/2 mouse events are still the source of truth inside the emulator; they are translated into the packed 16-bit report above.
- The 16-bit report keeps the mouse port-mapped and one-read-per-device like the keyboard and gamepad ports.

### Examples

- Left-click with no motion: `0x0001`
- Move right by 3 and up by 2: X = `3`, Y = `-2`, result uses the X/Y fields with no button bits set
- Scroll up with right button held: right button bit plus wheel-up bit set in the same word

---

## VF16TTY

The `VF16TTY` port device behaves like a simple ASCII teletype on a single port.
It is fixed to the fifth port in the default emulator layout (`PORT5` in the UI, assembler port `PORT4`, zero-based index `4`).

### Port Layout

- `OUT` writes the low byte as an ASCII character to the console
- `IN` reads the next available ASCII character from the console into the low byte
- High byte is reserved and currently reads as `0x0000`

### Behaviour

- Output is immediate from the ROM's perspective.
- Input is polled; if no character is available, `IN` returns `0x0000`.
- ASCII values are returned as 7-bit/8-bit character codes.
- Non-ASCII keys are translated to `?`.
- Enter, tab, and backspace are translated to standard ASCII control codes (`0x0D`, `0x09`, `0x08`).

### Examples

- `0x0041` written via `OUT` prints `A`
- `IN` returning `0x0061` means the user typed `a`
- `IN` returning `0x000D` means the user pressed Enter

### Programming Model

- Use `IN PORT DST` to poll console input and `OUT SRC PORT` to emit console output.
- ROMs should treat `0x0000` as no input available.

---

## VF16Flash

Persistent 32K word flash storage device for all ROMs to use for external persistent storage across power cycles.

### Port Layout

Flash storage is accessed via a two-step protocol:

**Command Word (bits):**
- Bit 0: Operation (0 = read, 1 = write)
- Bits 1–15: Address (0x0000–0x7FFF for 32K words)

**Data Word:**
- Bits 0–15: 16-bit value to write (write operations only)

### Behaviour

- Storage is persistent across emulator sessions.
- Address range: `0x0000` to `0x7FFF` (32,768 words).
- Read operation: `OUT address+cmd PORT`, then `IN PORT` to retrieve value.
- Write operation: `OUT address+cmd PORT`, then `OUT value PORT` to store value.
- Reading from an uninitialized address returns `0x0000`.
- Writing beyond the address space is ignored.
- Access time is immediate from the ROM's perspective; no latency or wait-states.

### Protocol Examples

Reading from address `0x1234`:
```
MOV %0x1234 X      ; Address in bits 1-15, cmd bit 0 = 0 (read)
OUT X PORT3        ; Issue read command (assuming VF16Flash on PORT3)
IN PORT3 Y         ; Read the value into Y
```

Writing `0xABCD` to address `0x5678`:
```
MOV %0xD678 X      ; Address 0x5678 in bits 1-15, cmd bit 0 = 1 (write)
OUT X PORT3        ; Issue write command
MOV %0xABCD Y      ; Value to write
OUT Y PORT3        ; Write the value
```

### Programming Model

- Use `OUT` to issue commands and write data.
- Use `IN` to read data (after a read command).
- Storage is automatically flushed to persistent media by the emulator (frequency is implementation-defined).
- ROMs should not assume immediate persistence for reliability; treat as best-effort.

---

- Programs read controller state through the configured port device.
- Input is polled; it is not event-driven.
- No explicit clearing or acknowledgment is required.
- Frame timing (including VBlank) is a system-level synchronisation mechanism used for rendering and scheduling. It is not part of the I/O system and is not transmitted through ports or memory-mapped registers.

