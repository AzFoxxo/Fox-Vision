# FoxC Headers Demo

This example demonstrates the two things FoxC headers can contain:

1. **Function signatures (prototypes)** — `math.fh` declares `add`, `scale`,
   and `square` without bodies. The definitions live in `main.fc`.
2. **Whole code** — `util.fh` defines the global `pass_count` and the functions
   `twice` and `check` directly in the header.

`main.fc` includes both headers. `util.fh` is included twice on purpose to show
that each header is expanded only once per translation unit.

## Build

From the repository root, with `foxc` on `PATH`:

```sh
foxc --mode extended -I FoxC/examples/headers_demo \
     -i FoxC/examples/headers_demo/main.fc \
     -o headers_demo.bin -a
```

Or from the `FoxC` directory:

```sh
foxc --mode extended -I examples/headers_demo -i examples/headers_demo/main.fc -o headers_demo.bin -a
```

Running the resulting ROM prints `PASS` on the TTY when every check succeeds.
