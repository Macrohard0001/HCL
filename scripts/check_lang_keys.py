#!/usr/bin/env python3
"""扫描所有 XAML/CS 引用的语言 key，对照 Strings.zh-CN.xaml / en 检查缺失。"""
import re, glob, sys

root = "/tmp/my-project/src/HardmacroCraft"

def dict_keys(path):
    return set(re.findall(r'x:Key="([^"]+)"', open(path, encoding="utf-8").read()))

zh = dict_keys(f"{root}/Themes/Languages/Strings.zh-CN.xaml")
en = dict_keys(f"{root}/Themes/Languages/Strings.en.xaml")

missing = {}
# 1) XAML 中的 DynamicResource S.*
for f in glob.glob(f"{root}/**/*.xaml", recursive=True):
    if "/Themes/Languages/" in f: continue
    txt = open(f, encoding="utf-8").read()
    for k in re.findall(r'DynamicResource (S\.[A-Za-z0-9.]+)', txt):
        if k not in zh or k not in en:
            missing.setdefault(k, []).append(f.replace(root+"/", ""))
# 2) CS 中的 FindResource/FindRes/TryFindResource("S...")
for f in glob.glob(f"{root}/**/*.cs", recursive=True):
    if "/obj/" in f or "/bin/" in f: continue
    txt = open(f, encoding="utf-8").read()
    for k in re.findall(r'(?:FindResource|FindRes|TryFindResource)\("([^"]+)"\)', txt):
        if k.startswith("S.") and (k not in zh or k not in en):
            missing.setdefault(k, []).append(f.replace(root+"/", "") + " (code)")

print(f"zh keys: {len(zh)}, en keys: {len(en)}")
if missing:
    print("MISSING KEYS:")
    for k, locs in sorted(missing.items()):
        print(f"  {k}  <- {', '.join(sorted(set(locs)))}")
    sys.exit(1)
print("OK: all referenced S.* keys exist in both dictionaries")
