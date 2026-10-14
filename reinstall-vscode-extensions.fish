#!/usr/bin/env fish

set -g root (cd (dirname (status --current-filename)); and pwd)
set -q VSCODE_BIN; or set -g VSCODE_BIN code
set -g tmp_dir (mktemp -d)

function cleanup --on-event fish_exit
    rm -rf $tmp_dir
end

if not command -q $VSCODE_BIN
    echo "VS Code CLI not found: $VSCODE_BIN" >&2
    exit 1
end

if not command -q npx
    echo "npx is required to package the extensions." >&2
    exit 1
end

function install_extension
    set -l directory $argv[1]
    set -l extension_id $argv[2]
    set -l package "$tmp_dir/"(string split -r -m1 . $extension_id)[2]".vsix"

    echo "== Packaging $extension_id =="
    pushd "$root/$directory" >/dev/null
    npx --yes @vscode/vsce package --out $package >/dev/null
    set -l package_status $status
    popd >/dev/null
    test $package_status -eq 0
    or exit $status

    echo "== Reinstalling $extension_id =="
    $VSCODE_BIN --uninstall-extension $extension_id >/dev/null 2>&1
    $VSCODE_BIN --install-extension $package --force
end

install_extension vscode-fox16asm-highlighter azfoxxo.fox16asm-highlighter
or exit $status
install_extension vscode-foxc azfoxxo.foxc-tools
or exit $status

echo "VS Code extensions reinstalled. Reload VS Code to activate the changes."
