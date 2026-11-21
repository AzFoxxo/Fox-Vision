CC ?= cc
CFLAGS ?= -O2 -Wall -Wextra
CFLAGS += -D_FILE_OFFSET_BITS=64 $(shell pkg-config --cflags fuse3)
LDLIBS += $(shell pkg-config --libs fuse3)

.PHONY: all clean

all: foxfs_mount

foxfs_mount: foxfs_mount.c
	$(CC) $(CFLAGS) -o $@ $< $(LDLIBS)

clean:
	rm -f foxfs_mount
