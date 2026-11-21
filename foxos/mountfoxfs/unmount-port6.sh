#!/usr/bin/env bash
# Unmount the VF32HardDisk mounted at ~/vf16/hd by mount-port6.sh.
set -eu

mount_point=${FOXFS_MOUNT:-"$HOME/vf16/hd"}

if ! mountpoint -q "$mount_point"; then
    echo "not mounted: $mount_point"
    exit 0
fi

if fusermount3 -u "$mount_point"; then
    echo "unmounted $mount_point"
elif fusermount3 -zu "$mount_point"; then
    echo "unmounted $mount_point (lazy: a process still had it open)"
else
    echo "could not unmount $mount_point" >&2
    exit 1
fi
