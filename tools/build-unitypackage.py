#!/usr/bin/env python3
"""Build the plugin's .unitypackage from a clean git working tree.

    python3 tools/build-unitypackage.py <out.unitypackage>

The package always corresponds to a commit: the script refuses to run while
tracked files are modified. Needs Python 3 and git, nothing else.

What goes in: Editor/ (with Sounds/), Wiki/, LICENSE, README.md, CHANGELOG.md.
What stays out: AGENTS.md, PROJECT_STATE.md, PROMO_KIT.md, project-memory/,
tools/ and anything project-specific.

Layout (mirrors the published v3.3.0 asset):
  ./<guid>/asset        file contents (absent for folders)
  ./<guid>/asset.meta   the tracked .meta
  ./<guid>/pathname     target path, no trailing newline
GUIDs come from the tracked .meta files, so importing over an existing install
updates it in place. The target path is the historical double folder
(…-3.0.0/…-2.0.0) for the same reason — the inner name is NOT the version.
Text is written with CRLF like the published packages; binaries are untouched.
"""
import gzip
import hashlib
import io
import os
import re
import subprocess
import sys
import tarfile

ROOT = "Assets/VRChat-outfit-batch-uploader-3.0.0/VRChat-outfit-batch-uploader-2.0.0"
BINARY = (".mp3", ".wav", ".ogg", ".png", ".jpg", ".jpeg")
FOLDERS = ["Editor", "Editor/Sounds", "Wiki"]
ROOT_FILES = ["LICENSE", "README.md", "CHANGELOG.md"]


def main():
    if len(sys.argv) != 2:
        sys.exit("usage: python3 tools/build-unitypackage.py <out.unitypackage>")
    out = os.path.abspath(sys.argv[1])
    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

    def git(*args):
        return subprocess.run(["git", "-C", repo, *args], capture_output=True, text=True, check=True).stdout

    if git("status", "--porcelain", "--untracked-files=no").strip():
        sys.exit("Tracked files are modified — commit first, so the package matches a commit.")
    tracked = set(git("ls-files").splitlines())
    mtime = int(git("log", "-1", "--format=%ct").strip())

    files = list(ROOT_FILES)
    files += sorted(f for f in tracked if re.fullmatch(r"Editor/[^/]+\.cs", f))
    files += sorted(f for f in tracked if f.startswith("Editor/Sounds/") and not f.endswith(".meta"))
    files += sorted(f for f in tracked if re.fullmatch(r"Wiki/[^/]+\.md", f))

    def read(path):
        with open(os.path.join(repo, path), "rb") as handle:
            return handle.read()

    def crlf(data):
        return data.replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")

    entries = {}
    for path in FOLDERS + files:
        if path + ".meta" not in tracked:
            sys.exit(f"No tracked .meta for {path} — Unity would assign a new GUID on import.")
        meta = read(path + ".meta")
        match = re.search(rb"^guid:\s*([0-9a-f]{32})\s*$", meta, re.M)
        if not match:
            sys.exit(f"No guid in {path}.meta")
        guid = match.group(1).decode()
        if guid in entries:
            sys.exit(f"Duplicate GUID {guid}: {path} and {entries[guid]['path']}")
        asset = None
        if path not in FOLDERS:
            asset = read(path)
            if not path.lower().endswith(BINARY):
                asset = crlf(asset)
        entries[guid] = {"path": path, "asset": asset, "meta": crlf(meta)}

    def add(tar, name, data=None):
        info = tarfile.TarInfo(name)
        info.mtime, info.mode, info.uid, info.gid, info.uname, info.gname = mtime, 0o777, 0, 0, "", ""
        if data is None:
            info.type = tarfile.DIRTYPE
            tar.addfile(info)
        else:
            info.size = len(data)
            tar.addfile(info, io.BytesIO(data))

    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode="w", format=tarfile.GNU_FORMAT) as tar:
        add(tar, ".")
        for guid in sorted(entries):
            entry = entries[guid]
            add(tar, f"./{guid}")
            if entry["asset"] is not None:
                add(tar, f"./{guid}/asset", entry["asset"])
            add(tar, f"./{guid}/asset.meta", entry["meta"])
            add(tar, f"./{guid}/pathname", f"{ROOT}/{entry['path']}".encode("utf-8"))

    # Fixed mtime and no file name in the gzip header: the same commit gives the same bytes.
    packed = io.BytesIO()
    with gzip.GzipFile(fileobj=packed, mode="wb", mtime=mtime, filename="") as gz:
        gz.write(raw.getvalue())
    data = packed.getvalue()
    with open(out, "wb") as handle:
        handle.write(data)

    scripts = sum(1 for e in entries.values() if e["path"].endswith(".cs"))
    print(f"{len(entries)} assets ({scripts} .cs), {len(data)} bytes")
    print(f"commit  {git('rev-parse', '--short', 'HEAD').strip()}")
    print(f"sha256  {hashlib.sha256(data).hexdigest()}")
    print(out)


if __name__ == "__main__":
    main()
