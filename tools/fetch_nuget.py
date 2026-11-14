#!/usr/bin/env python3
"""Download a NuGet package closure from nuget.org into a local folder feed.

dotnet.exe on this PC is denied outbound TCP, but curl/python are not, so we
resolve the dependency graph here and hand dotnet a plain folder of .nupkgs.
"""
import json
import os
import re
import sys
import urllib.request
import xml.etree.ElementTree as ET

FLAT = "https://api.nuget.org/v3-flatcontainer"
FEED = r"C:\Dev\Projects\ShortcutPad\local-feed"

_version_cache = {}
_download_cache = set()


def get(url):
    req = urllib.request.Request(url, headers={"User-Agent": "nuget-fetch"})
    with urllib.request.urlopen(req, timeout=90) as r:
        return r.read()


def versions(pid):
    if pid not in _version_cache:
        data = json.loads(get(f"{FLAT}/{pid.lower()}/index.json"))
        _version_cache[pid] = [v for v in data["versions"] if not v.endswith("-ci") and "-" not in v]
    return _version_cache[pid]


def vkey(v):
    parts = []
    for piece in re.split(r"[.\-]", v):
        parts.append((0, int(piece)) if piece.isdigit() else (1, piece))
    return parts


def parse_range(rng):
    rng = (rng or "").strip()
    if not rng:
        return None
    if rng.startswith("["):
        inner = rng.strip("[]()")
        return inner.split(",")[0].strip() or None
    return rng


def best_version(pid, rng):
    want = parse_range(rng)
    cands = versions(pid)
    if want:
        try:
            cands = [v for v in cands if vkey(v) >= vkey(want)]
        except Exception:
            pass
    if not cands:
        cands = versions(pid)
    return sorted(cands, key=vkey)[0]


def nuspec_deps(pid, ver):
    raw = get(f"{FLAT}/{pid.lower()}/{ver}/{pid.lower()}.nuspec")
    root = ET.fromstring(raw)
    groups = []
    for group in root.findall(".//{*}group"):
        tfm = group.get("targetFramework", "")
        deps = [(d.get("id"), d.get("version", "1.0.0")) for d in group.findall("{*}dependency")]
        groups.append((tfm, deps))
    if not groups:
        deps = [(d.get("id"), d.get("version", "1.0.0")) for d in root.findall(".//{*}dependencies/{*}dependency")]
        if deps:
            groups.append(("", deps))
    return groups


def pick_group(groups):
    if not groups:
        return []
    def rank(tfm):
        t = tfm.lower().lstrip(".")
        if t in ("net9.0", "net8.0"):
            return (0, t)
        if t.startswith("net8.0"):
            return (1, t)
        if t.startswith("net6.0"):
            return (2, t)
        if t.startswith("netstandard2.1"):
            return (3, t)
        if t.startswith("netstandard2.0"):
            return (4, t)
        if t.startswith("netstandard"):
            return (5, t)
        if t.startswith("net"):
            return (6, t)
        return (7, t)
    return sorted(groups, key=lambda g: rank(g[0]))[0][1]


def download(pid, ver):
    key = f"{pid.lower()}/{ver}"
    if key in _download_cache:
        return
    _download_cache.add(key)
    os.makedirs(FEED, exist_ok=True)
    dest = os.path.join(FEED, f"{pid.lower()}.{ver.lower()}.nupkg")
    if os.path.exists(dest) and os.path.getsize(dest) > 0:
        print(f"have  {pid} {ver}")
        return
    url = f"{FLAT}/{pid.lower()}/{ver}/{pid.lower()}.{ver}.nupkg"
    data = get(url)
    with open(dest, "wb") as f:
        f.write(data)
    print(f"got   {pid} {ver} ({len(data)//1024} KB)")


def walk(roots):
    queue = list(roots)
    seen = set()
    while queue:
        pid, ver = queue.pop(0)
        pid = pid.strip()
        if not pid:
            continue
        key = (pid.lower(), ver)
        if key in seen:
            continue
        seen.add(key)
        download(pid, ver)
        try:
            groups = nuspec_deps(pid, ver)
        except Exception as e:
            print(f"warn  no nuspec for {pid} {ver}: {e}")
            continue
        for dep_id, dep_range in pick_group(groups):
            if dep_id is None:
                continue
            try:
                queue.append((dep_id, best_version(dep_id, dep_range)))
            except Exception as e:
                print(f"warn  cannot resolve {dep_id} ({dep_range}): {e}")


if __name__ == "__main__":
    roots = []
    for arg in sys.argv[1:]:
        pid, _, ver = arg.partition("@")
        roots.append((pid, ver or best_version(pid, None)))
    print(f"feed -> {FEED}")
    walk(roots)
    total = sum(os.path.getsize(os.path.join(FEED, f)) for f in os.listdir(FEED))
    print(f"done: {len(_download_cache)} packages, {total//1024//1024} MB")
