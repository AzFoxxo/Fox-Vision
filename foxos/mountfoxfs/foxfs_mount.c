// foxfs_mount - mount a VF32HardDisk image containing FoxFS with FUSE3.
//
// Usage: foxfs_mount IMAGE MOUNTPOINT [FUSE options...]
//
// The image is the raw 4 GiB cartridge file (normally
// ~/vf16/ports/vf32harddisk/port6.img). FoxFS entries and file data are
// exposed as a writable tree so a file manager such as Dolphin can browse and
// edit them. Type 3 ("text") files store bytes packed four per disk word, so
// host editors see ordinary text; type 1 files keep the legacy word format.

#define _FILE_OFFSET_BITS 64
#define FUSE_USE_VERSION 30

#include <errno.h>
#include <fcntl.h>
#include <fuse.h>
#include <pthread.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <sys/statvfs.h>
#include <time.h>
#include <unistd.h>

// FoxFS v8 layout, mirrored from foxfs/foxfs_internal.fc.
#define FS_MAGIC          0x464F5833u
#define FS_VERSION        8u
#define ENTRY_COUNT       512u
#define ENTRY_TABLE       4u
#define ENTRY_WORDS       4u
#define ENTRY_TYPE        0u
#define ENTRY_PARENT      1u
#define ENTRY_NAMELEN     2u
#define ENTRY_SIZE        3u
#define NAME_TABLE_BASE   2052u
#define NAMES_PER_SLOT    16u
#define DATA_BASE         10244u
#define FILE_CHUNK_WORDS  0x1FF000u
#define ROOT_SLOT         512u
#define MAX_FILE_BYTES    ((uint64_t)FILE_CHUNK_WORDS * 4u)

struct fox_entry {
    uint32_t type;   // 0 = free, 1 = file, 2 = directory
    uint32_t parent;
    uint32_t namelen;
    uint32_t size;   // in 32-bit words
};

static int image_fd = -1;
static pthread_mutex_t image_lock = PTHREAD_MUTEX_INITIALIZER;

// Serialise FUSE callbacks: the image is one shared file descriptor and
// metadata changes span several word writes.
#define LOCKED(call) do {                       \
    pthread_mutex_lock(&image_lock);            \
    int locked_rc_ = (call);                    \
    pthread_mutex_unlock(&image_lock);          \
    return locked_rc_;                          \
} while (0)

static int pread_all(void *data, size_t len, off_t offset)
{
    unsigned char *cursor = data;

    while (len) {
        ssize_t got = pread(image_fd, cursor, len, offset);
        if (got <= 0)
            return -EIO;
        cursor += got;
        offset += got;
        len -= (size_t)got;
    }
    return 0;
}

static int pwrite_all(const void *data, size_t len, off_t offset)
{
    const unsigned char *cursor = data;

    while (len) {
        ssize_t put = pwrite(image_fd, cursor, len, offset);
        if (put <= 0)
            return -EIO;
        cursor += put;
        offset += put;
        len -= (size_t)put;
    }
    return 0;
}

static uint32_t decode_word(const unsigned char bytes[4])
{
    return (uint32_t)bytes[0] | ((uint32_t)bytes[1] << 8) |
           ((uint32_t)bytes[2] << 16) | ((uint32_t)bytes[3] << 24);
}

static void encode_word(unsigned char bytes[4], uint32_t value)
{
    bytes[0] = (unsigned char)(value & 0xFFu);
    bytes[1] = (unsigned char)((value >> 8) & 0xFFu);
    bytes[2] = (unsigned char)((value >> 16) & 0xFFu);
    bytes[3] = (unsigned char)((value >> 24) & 0xFFu);
}

static int read_word(uint32_t word, uint32_t *value)
{
    unsigned char bytes[4];
    int err = pread_all(bytes, sizeof(bytes), (off_t)word * 4);

    if (err)
        return err;
    *value = decode_word(bytes);
    return 0;
}

static int write_word(uint32_t word, uint32_t value)
{
    unsigned char bytes[4];
    encode_word(bytes, value);
    return pwrite_all(bytes, sizeof(bytes), (off_t)word * 4);
}

static int zero_words(uint32_t first, uint32_t count)
{
    const unsigned char zeros[1024] = { 0 };

    while (count) {
        uint32_t chunk = count > 1024 ? 1024 : count;
        int err = pwrite_all(zeros, chunk * 4, (off_t)first * 4);

        if (err)
            return err;
        first += chunk;
        count -= chunk;
    }
    return 0;
}

static int read_entry(uint32_t slot, struct fox_entry *entry)
{
    uint32_t words[ENTRY_WORDS];
    uint32_t base;
    unsigned int i;
    int err;

    if (slot == ROOT_SLOT) {
        entry->type = 2;
        entry->parent = ROOT_SLOT;
        entry->namelen = 0;
        entry->size = 0;
        return 0;
    }
    if (slot >= ENTRY_COUNT)
        return -ENOENT;

    base = ENTRY_TABLE + slot * ENTRY_WORDS;
    for (i = 0; i < ENTRY_WORDS; i++) {
        err = read_word(base + i, &words[i]);
        if (err)
            return err;
    }
    entry->type = words[ENTRY_TYPE];
    entry->parent = words[ENTRY_PARENT];
    entry->namelen = words[ENTRY_NAMELEN];
    entry->size = words[ENTRY_SIZE];
    return 0;
}

static int write_entry(uint32_t slot, uint32_t type, uint32_t parent,
                       uint32_t namelen, uint32_t size)
{
    uint32_t base;

    if (slot >= ENTRY_COUNT)
        return -ENOSPC;
    base = ENTRY_TABLE + slot * ENTRY_WORDS;
    int err = write_word(base + ENTRY_TYPE, type);
    if (err)
        return err;
    err = write_word(base + ENTRY_PARENT, parent);
    if (err)
        return err;
    err = write_word(base + ENTRY_NAMELEN, namelen);
    if (err)
        return err;
    return write_word(base + ENTRY_SIZE, size);
}

// Copy one entry name, stopping at namelen. Returns non-zero for a bad name.
static int read_name(uint32_t slot, const struct fox_entry *entry,
                     char name[NAMES_PER_SLOT + 1])
{
    uint32_t base = NAME_TABLE_BASE + slot * NAMES_PER_SLOT;
    uint32_t word;
    unsigned int i;
    int err;

    if (slot == ROOT_SLOT || entry->namelen == 0 ||
        entry->namelen > NAMES_PER_SLOT)
        return -1;

    for (i = 0; i < entry->namelen; i++) {
        err = read_word(base + i, &word);
        if (err)
            return err;
        name[i] = (char)(word & 0xFFu);
        if (name[i] == 0 || name[i] == '/')
            return -1;
    }
    name[entry->namelen] = '\0';
    return 0;
}

static int write_name(uint32_t slot, const char *name, size_t length)
{
    uint32_t base = NAME_TABLE_BASE + slot * NAMES_PER_SLOT;
    unsigned int i;

    for (i = 0; i < NAMES_PER_SLOT; i++) {
        uint32_t character = i < length ? (unsigned char)name[i] : 0;
        int err = write_word(base + i, character);

        if (err)
            return err;
    }
    return 0;
}

// Host-created files start as type 3 (byte-packed text). If the bytes being
// written are not printable ASCII plus line breaks, promote the entry to type 1
// so compiled ROMs copied in from the host remain executable by FoxOS.
static int bytes_look_like_text(const unsigned char *data, size_t length)
{
    size_t i;

    for (i = 0; i < length; i++) {
        unsigned char c = data[i];

        if (c == '\t' || c == '\r' || c == '\n')
            continue;
        if (c < 32 || c > 126)
            return 0;
    }
    return 1;
}

static int name_valid(const char *name, size_t length)
{
    size_t i;

    if (length == 0 || length > NAMES_PER_SLOT)
        return 0;
    if (length == 1 && name[0] == '.')
        return 0;
    if (length == 2 && name[0] == '.' && name[1] == '.')
        return 0;
    for (i = 0; i < length; i++) {
        if (name[i] == '/' || name[i] == '\0')
            return 0;
    }
    return 1;
}

// Walk an absolute FoxFS path to its slot. The final component may be a file.
static int resolve_path(const char *path, uint32_t *slot_out,
                        struct fox_entry *entry_out)
{
    const char *cursor = path;
    uint32_t current = ROOT_SLOT;
    struct fox_entry entry;
    int err;

    if (!path || path[0] != '/')
        return -ENOENT;

    while (*cursor) {
        char component[NAMES_PER_SLOT + 1];
        const char *start;
        size_t length;
        uint32_t slot;
        int found = -1;

        while (*cursor == '/')
            cursor++;
        if (!*cursor)
            break;

        start = cursor;
        while (*cursor && *cursor != '/')
            cursor++;
        length = (size_t)(cursor - start);

        err = read_entry(current, &entry);
        if (err)
            return err;

        if (length == 1 && start[0] == '.')
            continue;
        if (length == 2 && start[0] == '.' && start[1] == '.') {
            current = entry.parent;
            continue;
        }
        if (length > NAMES_PER_SLOT)
            return -ENOENT;
        memcpy(component, start, length);
        component[length] = '\0';

        for (slot = 0; slot < ENTRY_COUNT; slot++) {
            struct fox_entry child;
            char name[NAMES_PER_SLOT + 1];

            err = read_entry(slot, &child);
            if (err)
                return err;
            if (child.type == 0 || child.parent != current ||
                child.namelen != length)
                continue;
            if (read_name(slot, &child, name) != 0)
                continue;
            if (memcmp(name, component, length) == 0) {
                found = (int)slot;
                break;
            }
        }
        if (found < 0)
            return -ENOENT;

        current = (uint32_t)found;
        err = read_entry(current, &entry);
        if (err)
            return err;
        if (entry.type == 0)
            return -ENOENT;
        if (entry.type != 2 && *cursor)
            return -ENOTDIR;
    }

    err = read_entry(current, entry_out);
    if (err)
        return err;
    *slot_out = current;
    return 0;
}

static int find_child(uint32_t parent, const char *name, size_t length,
                      uint32_t *slot_out)
{
    uint32_t slot;

    for (slot = 0; slot < ENTRY_COUNT; slot++) {
        struct fox_entry child;
        char entry_name[NAMES_PER_SLOT + 1];
        int err = read_entry(slot, &child);

        if (err)
            return err;
        if (child.type == 0 || child.parent != parent ||
            child.namelen != length)
            continue;
        if (read_name(slot, &child, entry_name) != 0)
            continue;
        if (memcmp(entry_name, name, length) == 0) {
            *slot_out = slot;
            return 0;
        }
    }
    return -ENOENT;
}

static int first_free_slot(uint32_t *slot_out)
{
    uint32_t slot;

    for (slot = 0; slot < ENTRY_COUNT; slot++) {
        struct fox_entry entry;
        int err = read_entry(slot, &entry);

        if (err)
            return err;
        if (entry.type == 0) {
            *slot_out = slot;
            return 0;
        }
    }
    return -ENOSPC;
}

static int split_parent(const char *path, char parent[256],
                        const char **name, size_t *length)
{
    const char *slash = strrchr(path, '/');
    size_t parent_length;

    if (!slash || !slash[1])
        return -EINVAL;
    parent_length = slash == path ? 1 : (size_t)(slash - path);
    if (parent_length >= 256)
        return -ENAMETOOLONG;
    memcpy(parent, path, parent_length);
    parent[parent_length] = '\0';
    *name = slash + 1;
    *length = strlen(slash + 1);
    return 0;
}

static int clear_slot(uint32_t slot)
{
    int err = write_entry(slot, 0, ROOT_SLOT, 0, 0);

    if (err)
        return err;
    return write_name(slot, NULL, 0);
}

static int is_empty_dir(uint32_t slot)
{
    uint32_t child;

    for (child = 0; child < ENTRY_COUNT; child++) {
        struct fox_entry entry;
        int err = read_entry(child, &entry);

        if (err)
            return err;
        if (entry.type != 0 && entry.parent == slot)
            return 0;
    }
    return 1;
}

// Used by open(O_TRUNC), truncate(), and write() when a file grows.
static int truncate_slot(uint32_t slot, uint64_t bytes)
{
    struct fox_entry entry;
    uint32_t new_words;
    uint32_t old_words;
    uint32_t new_size;
    uint32_t base;
    int err;

    if (bytes > MAX_FILE_BYTES)
        return -EFBIG;
    err = read_entry(slot, &entry);
    if (err)
        return err;
    if (entry.type != 1 && entry.type != 3)
        return -EISDIR;

    new_words = (uint32_t)((bytes + 3) / 4);
    old_words = entry.type == 3 ? (entry.size + 3) / 4 : entry.size;
    new_size = entry.type == 3 ? (uint32_t)bytes : new_words;
    base = DATA_BASE + slot * FILE_CHUNK_WORDS;
    if (new_words < old_words)
        err = zero_words(base + new_words, old_words - new_words);
    else if (new_words > old_words)
        err = zero_words(base + old_words, new_words - old_words);
    if (err)
        return err;
    return write_word(ENTRY_TABLE + slot * ENTRY_WORDS + ENTRY_SIZE,
                      new_size);
}

static int open_file(const char *path, struct fuse_file_info *fi,
                     uint32_t *slot, struct fox_entry *entry)
{
    int err;

    if (fi && fi->fh) {
        *slot = (uint32_t)fi->fh - 1;
        err = read_entry(*slot, entry);
        if (err)
            return err;
        return (entry->type == 1 || entry->type == 3) ? 0 : -EBADF;
    }
    err = resolve_path(path, slot, entry);
    if (err)
        return err;
    return (entry->type == 1 || entry->type == 3) ? 0 : -EISDIR;
}

// ---------------------------------------------------------------------------
// FUSE callbacks. Each one locks the shared image for the whole operation.
// ---------------------------------------------------------------------------

static int getattr_impl(const char *path, struct stat *st,
                        struct fuse_file_info *fi)
{
    uint32_t slot;
    struct fox_entry entry;
    int err = resolve_path(path, &slot, &entry);

    (void)fi;
    if (err)
        return err;

    memset(st, 0, sizeof(*st));
    st->st_uid = getuid();
    st->st_gid = getgid();
    st->st_nlink = 1;

    if (entry.type == 2) {
        st->st_mode = S_IFDIR | 0755;
        st->st_nlink = 2;
    } else if (entry.type == 1) {
        st->st_mode = S_IFREG | 0644;
        st->st_size = (off_t)entry.size * 4;
    } else if (entry.type == 3) {
        st->st_mode = S_IFREG | 0644;
        st->st_size = (off_t)entry.size;  // byte-packed text
    } else {
        return -ENOENT;
    }
    return 0;
}
static int fox_getattr(const char *p, struct stat *s, struct fuse_file_info *f)
{
    LOCKED(getattr_impl(p, s, f));
}

static int readdir_impl(const char *path, void *buf, fuse_fill_dir_t filler,
                        off_t offset, struct fuse_file_info *fi,
                        enum fuse_readdir_flags flags)
{
    uint32_t dir_slot;
    uint32_t slot;
    struct fox_entry entry;
    int err = resolve_path(path, &dir_slot, &entry);

    (void)offset;
    (void)fi;
    (void)flags;
    if (err)
        return err;
    if (entry.type != 2)
        return -ENOTDIR;

    filler(buf, ".", NULL, 0, 0);
    filler(buf, "..", NULL, 0, 0);

    for (slot = 0; slot < ENTRY_COUNT; slot++) {
        struct fox_entry child;
        char name[NAMES_PER_SLOT + 1];

        err = read_entry(slot, &child);
        if (err)
            return err;
        if (child.type == 0 || child.parent != dir_slot)
            continue;
        if (read_name(slot, &child, name) != 0)
            continue;
        if (filler(buf, name, NULL, 0, 0))
            return 0;
    }
    return 0;
}
static int fox_readdir(const char *p, void *b, fuse_fill_dir_t f, off_t o,
                       struct fuse_file_info *fi, enum fuse_readdir_flags fl)
{
    LOCKED(readdir_impl(p, b, f, o, fi, fl));
}

static int open_impl(const char *path, struct fuse_file_info *fi)
{
    uint32_t slot;
    struct fox_entry entry;
    int err = resolve_path(path, &slot, &entry);

    if (err)
        return err;
    if (entry.type != 1 && entry.type != 3)
        return -EISDIR;
    if (fi->flags & O_TRUNC) {
        err = truncate_slot(slot, 0);
        if (err)
            return err;
    }
    fi->fh = slot + 1;
    return 0;
}
static int fox_open(const char *p, struct fuse_file_info *f)
{
    LOCKED(open_impl(p, f));
}

static int read_impl(const char *path, char *buf, size_t size, off_t offset,
                     struct fuse_file_info *fi)
{
    uint32_t slot;
    struct fox_entry entry;
    uint64_t file_size;
    uint64_t first_word;
    size_t shift;
    size_t count;
    size_t words;
    unsigned char *data;
    int err = open_file(path, fi, &slot, &entry);

    if (err)
        return err;

    file_size = entry.type == 3 ? (uint64_t)entry.size
                                : (uint64_t)entry.size * 4;
    if (offset < 0 || (uint64_t)offset >= file_size)
        return 0;

    count = size;
    if ((uint64_t)count > file_size - (uint64_t)offset)
        count = (size_t)(file_size - (uint64_t)offset);

    first_word = (uint64_t)offset / 4;
    shift = (size_t)((uint64_t)offset % 4);
    words = (shift + count + 3) / 4;
    data = malloc(words * 4);
    if (!data)
        return -ENOMEM;

    err = pread_all(data, words * 4,
                    (off_t)(DATA_BASE + slot * FILE_CHUNK_WORDS + first_word) * 4);
    if (err) {
        free(data);
        return err;
    }
    memcpy(buf, data + shift, count);
    free(data);
    return (int)count;
}
static int fox_read(const char *p, char *b, size_t s, off_t o,
                    struct fuse_file_info *f)
{
    LOCKED(read_impl(p, b, s, o, f));
}

static int write_impl(const char *path, const char *buf, size_t size,
                      off_t offset, struct fuse_file_info *fi)
{
    const unsigned char *bytes = (const unsigned char *)buf;
    uint32_t slot;
    struct fox_entry entry;
    uint32_t base;
    uint32_t new_words;
    uint32_t old_words;
    uint32_t new_size;
    uint64_t start;
    uint64_t end;
    size_t done = 0;
    size_t head;
    int binary = 0;
    int err = open_file(path, fi, &slot, &entry);

    if (err)
        return err;
    if (offset < 0)
        return -EINVAL;

    start = (uint64_t)offset;
    end = start + size;
    if (end < start || end > MAX_FILE_BYTES)
        return -EFBIG;
    if (size == 0)
        return 0;
    binary = !bytes_look_like_text(bytes, size);

    new_words = (uint32_t)((end + 3) / 4);
    old_words = entry.type == 3 ? (entry.size + 3) / 4 : entry.size;
    new_size = entry.type == 3 ? (uint32_t)end : new_words;
    base = DATA_BASE + slot * FILE_CHUNK_WORDS;
    if (new_words > old_words) {
        err = zero_words(base + old_words, new_words - old_words);
        if (err)
            return err;
    }

    // Merge partial first/last words so byte-level writes keep other bytes.
    head = (size_t)(start % 4);
    if (head) {
        unsigned char word_bytes[4];
        uint32_t word;
        size_t n = 4 - head;

        if (n > size)
            n = size;
        err = read_word(base + (uint32_t)(start / 4), &word);
        if (err)
            return err;
        encode_word(word_bytes, word);
        memcpy(word_bytes + head, bytes, n);
        word = decode_word(word_bytes);
        err = write_word(base + (uint32_t)(start / 4), word);
        if (err)
            return err;
        done = n;
    }

    while (done + 4 <= size) {
        uint32_t word = decode_word(bytes + done);

        err = write_word(base + (uint32_t)((start + done) / 4), word);
        if (err)
            return err;
        done += 4;
    }

    if (done < size) {
        unsigned char word_bytes[4];
        uint32_t word;
        size_t n = size - done;
        uint32_t index = base + (uint32_t)((start + done) / 4);

        err = read_word(index, &word);
        if (err)
            return err;
        encode_word(word_bytes, word);
        memcpy(word_bytes, bytes + done, n);
        word = decode_word(word_bytes);
        err = write_word(index, word);
        if (err)
            return err;
    }

    // A compiled ROM copied in from the host contains non-text bytes. Promote
    // it to a program entry so FoxOS can still execute it.
    if (binary && entry.type == 3) {
        err = write_word(ENTRY_TABLE + slot * ENTRY_WORDS, 1);
        if (err)
            return err;
        new_size = new_words;
    }

    if (new_size != entry.size)
        err = write_word(ENTRY_TABLE + slot * ENTRY_WORDS + ENTRY_SIZE,
                         new_size);
    return err ? err : (int)size;
}
static int fox_write(const char *p, const char *b, size_t s, off_t o,
                     struct fuse_file_info *f)
{
    LOCKED(write_impl(p, b, s, o, f));
}

static int create_impl(const char *path, mode_t mode, struct fuse_file_info *fi)
{
    char parent_path[256];
    const char *name;
    size_t length;
    uint32_t parent;
    uint32_t slot;
    uint32_t existing;
    struct fox_entry entry;
    int err = split_parent(path, parent_path, &name, &length);

    (void)mode;
    if (err)
        return err;
    if (!name_valid(name, length))
        return -EINVAL;

    err = resolve_path(parent_path, &parent, &entry);
    if (err)
        return err;
    if (entry.type != 2)
        return -ENOTDIR;
    err = find_child(parent, name, length, &existing);
    if (err == 0)
        return -EEXIST;
    if (err != -ENOENT)
        return err;
    err = first_free_slot(&slot);
    if (err)
        return err;

    err = write_entry(slot, 3, parent, (uint32_t)length, 0);
    if (err)
        return err;
    err = write_name(slot, name, length);
    if (err)
        return err;
    fi->fh = slot + 1;
    return 0;
}
static int fox_create(const char *p, mode_t m, struct fuse_file_info *f)
{
    LOCKED(create_impl(p, m, f));
}

static int mkdir_impl(const char *path, mode_t mode)
{
    char parent_path[256];
    const char *name;
    size_t length;
    uint32_t parent;
    uint32_t slot;
    uint32_t existing;
    struct fox_entry entry;
    int err = split_parent(path, parent_path, &name, &length);

    (void)mode;
    if (err)
        return err;
    if (!name_valid(name, length))
        return -EINVAL;

    err = resolve_path(parent_path, &parent, &entry);
    if (err)
        return err;
    if (entry.type != 2)
        return -ENOTDIR;
    err = find_child(parent, name, length, &existing);
    if (err == 0)
        return -EEXIST;
    if (err != -ENOENT)
        return err;
    err = first_free_slot(&slot);
    if (err)
        return err;

    err = write_entry(slot, 2, parent, (uint32_t)length, 0);
    if (err)
        return err;
    return write_name(slot, name, length);
}
static int fox_mkdir(const char *p, mode_t m)
{
    LOCKED(mkdir_impl(p, m));
}

static int unlink_impl(const char *path)
{
    uint32_t slot;
    struct fox_entry entry;
    int err = resolve_path(path, &slot, &entry);

    if (err)
        return err;
    if (slot == ROOT_SLOT)
        return -EPERM;
    if (entry.type != 1)
        return -EISDIR;
    return clear_slot(slot);
}
static int fox_unlink(const char *p)
{
    LOCKED(unlink_impl(p));
}

static int rmdir_impl(const char *path)
{
    uint32_t slot;
    struct fox_entry entry;
    int err = resolve_path(path, &slot, &entry);

    if (err)
        return err;
    if (slot == ROOT_SLOT)
        return -EPERM;
    if (entry.type != 2)
        return -ENOTDIR;
    if (!is_empty_dir(slot))
        return -ENOTEMPTY;
    return clear_slot(slot);
}
static int fox_rmdir(const char *p)
{
    LOCKED(rmdir_impl(p));
}

static int rename_impl(const char *old_path, const char *new_path,
                       unsigned int flags)
{
    char old_parent[256];
    char new_parent[256];
    const char *old_name;
    const char *new_name;
    size_t old_length;
    size_t new_length;
    uint32_t old_slot;
    uint32_t new_parent_slot;
    uint32_t target;
    struct fox_entry old_entry;
    struct fox_entry parent_entry;
    struct fox_entry target_entry;
    int err;

    if (flags & 2)  // RENAME_EXCHANGE is not supported.
        return -EINVAL;
    err = split_parent(old_path, old_parent, &old_name, &old_length);
    if (err)
        return err;
    err = split_parent(new_path, new_parent, &new_name, &new_length);
    if (err)
        return err;
    if (!name_valid(new_name, new_length))
        return -EINVAL;

    err = resolve_path(old_path, &old_slot, &old_entry);
    if (err)
        return err;
    if (old_slot == ROOT_SLOT)
        return -EPERM;
    err = resolve_path(new_parent, &new_parent_slot, &parent_entry);
    if (err)
        return err;
    if (parent_entry.type != 2)
        return -ENOTDIR;

    err = find_child(new_parent_slot, new_name, new_length, &target);
    if (err == 0) {
        if (flags & 1)  // RENAME_NOREPLACE
            return -EEXIST;
        if (target == old_slot)
            return 0;
        err = read_entry(target, &target_entry);
        if (err)
            return err;
        if (target_entry.type == 2) {
            if (!is_empty_dir(target))
                return -ENOTEMPTY;
            if (old_entry.type != 2)
                return -ENOTDIR;
        } else if (old_entry.type == 2) {
            return -ENOTDIR;
        }
        err = clear_slot(target);
        if (err)
            return err;
    } else if (err != -ENOENT) {
        return err;
    }

    err = write_entry(old_slot, old_entry.type, new_parent_slot,
                      (uint32_t)new_length, old_entry.size);
    if (err)
        return err;
    return write_name(old_slot, new_name, new_length);
}
static int fox_rename(const char *o, const char *n, unsigned int f)
{
    LOCKED(rename_impl(o, n, f));
}

static int truncate_impl(const char *path, off_t size,
                         struct fuse_file_info *fi)
{
    uint32_t slot;
    struct fox_entry entry;
    int err = open_file(path, fi, &slot, &entry);

    if (err)
        return err;
    return truncate_slot(slot, (uint64_t)size);
}
static int fox_truncate(const char *p, off_t s, struct fuse_file_info *f)
{
    LOCKED(truncate_impl(p, s, f));
}

static int statfs_impl(const char *path, struct statvfs *st)
{
    uint64_t used_bytes = (uint64_t)DATA_BASE * 4;
    uint64_t used_blocks;
    uint32_t free_slots = 0;
    uint32_t slot;

    (void)path;
    for (slot = 0; slot < ENTRY_COUNT; slot++) {
        struct fox_entry entry;
        int err = read_entry(slot, &entry);

        if (err)
            return err;
        if (entry.type == 0) {
            free_slots++;
        } else if (entry.type == 1) {
            used_bytes += (uint64_t)entry.size * 4;
        } else if (entry.type == 3) {
            used_bytes += ((uint64_t)entry.size + 3) / 4 * 4;
        }
    }

    memset(st, 0, sizeof(*st));
    st->f_bsize = 4096;
    st->f_frsize = 4096;
    st->f_blocks = 1u << 20;  // 4 GiB / 4096
    used_blocks = (used_bytes + 4095) / 4096;
    if (used_blocks >= st->f_blocks)
        used_blocks = st->f_blocks - 1;
    st->f_bfree = st->f_bavail = st->f_blocks - used_blocks;
    st->f_files = ENTRY_COUNT;
    st->f_ffree = free_slots;
    st->f_namemax = NAMES_PER_SLOT;
    return 0;
}
static int fox_statfs(const char *p, struct statvfs *s)
{
    LOCKED(statfs_impl(p, s));
}

// Permissions and timestamps are not stored by FoxFS; accept updates so
// tools such as cp do not fail after copying a file in.
static int chmod_impl(const char *path, mode_t mode,
                      struct fuse_file_info *fi)
{
    (void)mode;
    (void)fi;
    uint32_t slot;
    struct fox_entry entry;
    return resolve_path(path, &slot, &entry);
}
static int fox_chmod(const char *p, mode_t m, struct fuse_file_info *f)
{
    LOCKED(chmod_impl(p, m, f));
}

static int utimens_impl(const char *path, const struct timespec tv[2],
                        struct fuse_file_info *fi)
{
    (void)path;
    (void)tv;
    (void)fi;
    return 0;
}
static int fox_utimens(const char *p, const struct timespec t[2],
                       struct fuse_file_info *f)
{
    LOCKED(utimens_impl(p, t, f));
}

static int fox_flush(const char *path, struct fuse_file_info *fi)
{
    (void)path;
    (void)fi;
    return 0;
}

static int fox_fsync(const char *path, int datasync,
                     struct fuse_file_info *fi)
{
    (void)path;
    (void)datasync;
    (void)fi;
    return 0;
}

static struct fuse_operations fox_ops = {
    .getattr = fox_getattr,
    .readdir = fox_readdir,
    .open = fox_open,
    .read = fox_read,
    .write = fox_write,
    .create = fox_create,
    .mkdir = fox_mkdir,
    .unlink = fox_unlink,
    .rmdir = fox_rmdir,
    .rename = fox_rename,
    .truncate = fox_truncate,
    .statfs = fox_statfs,
    .chmod = fox_chmod,
    .utimens = fox_utimens,
    .flush = fox_flush,
    .fsync = fox_fsync,
};

int main(int argc, char **argv)
{
    uint32_t magic, version, entries, data_base;
    char **fuse_argv;
    int fuse_argc = 0;
    int i;

    if (argc < 3) {
        fprintf(stderr,
                "usage: %s IMAGE MOUNTPOINT [FUSE options...]\n"
                "  IMAGE       VF32HardDisk image (e.g. ~/vf16/ports/vf32harddisk/port6.img)\n"
                "  MOUNTPOINT  directory to show the FoxFS tree in\n",
                argv[0]);
        return 2;
    }

    image_fd = open(argv[1], O_RDWR);
    if (image_fd < 0) {
        perror("cannot open image for reading and writing");
        return 1;
    }
    if (read_word(0, &magic) || read_word(1, &version) ||
        read_word(2, &entries) || read_word(3, &data_base)) {
        fprintf(stderr, "cannot read the FoxFS header\n");
        close(image_fd);
        return 1;
    }
    if (magic != FS_MAGIC || version != FS_VERSION ||
        entries != ENTRY_COUNT || data_base != DATA_BASE) {
        fprintf(stderr, "not a FoxFS v8 VF32HardDisk image (bad header)\n");
        close(image_fd);
        return 1;
    }

    // FUSE takes the mountpoint as a positional argument; everything after it
    // is passed through so callers can use -f, -d, -o ro, etc.
    fuse_argv = calloc((size_t)argc + 1, sizeof(*fuse_argv));
    if (!fuse_argv) {
        close(image_fd);
        return 1;
    }
    fuse_argv[fuse_argc++] = argv[0];
    fuse_argv[fuse_argc++] = argv[2];
    for (i = 3; i < argc; i++)
        fuse_argv[fuse_argc++] = argv[i];

    i = fuse_main(fuse_argc, fuse_argv, &fox_ops, NULL);
    free(fuse_argv);
    close(image_fd);
    return i;
}
