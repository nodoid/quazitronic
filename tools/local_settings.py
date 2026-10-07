"""Reads personal/account settings from the git-ignored signing/local.properties
(see build/local.properties.example), so nothing personal is kept in the repository."""
import os
import shlex

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PATH = os.path.join(ROOT, "signing", "local.properties")


def load():
    values = {}
    if os.path.exists(PATH):
        for line in open(PATH, encoding="utf-8"):
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            key, value = line.split("=", 1)
            key = key.replace("export ", "").strip()
            parts = shlex.split(value)
            values[key] = parts[0] if parts else ""
    return values


def get(key, default):
    return load().get(key) or os.environ.get("ORICTRON_" + key) or default
