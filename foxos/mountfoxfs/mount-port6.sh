#!/usr/bin/env bash
# Build foxfs_mount and map the VF32HardDisk on port 6 to ~/vf16/hd.
set -eu

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
image=${FOXVISION_PORT6_HARDDISK_PATH:-"$HOME/vf16/ports/vf32harddisk/port6.img"}
mount_point=${FOXFS_MOUNT:-"$HOME/vf16/hd"}

if [[ ${1:-} == "-u" || ${1:-} == "--unmount" ]]; then
    exec "$script_dir/unmount-port6.sh"
fi

if [[ ! -f "$image" ]]; then
    echo "port6 image not found: $image" >&2
    exit 1
fi

make -s -C "$script_dir"
mkdir -p "$mount_point"
if mountpoint -q "$mount_point"; then
    fusermount3 -u "$mount_point"
fi

# -f keeps the mount in the foreground; pass other FUSE options through too.
for arg in "$@"; do
    if [[ $arg == -f ]]; then
        exec "$script_dir/foxfs_mount" "$image" "$mount_point" "$@"
    fi
done

"$script_dir/foxfs_mount" "$image" "$mount_point" "$@"
echo "port6 ($image) mounted at $mount_point"
