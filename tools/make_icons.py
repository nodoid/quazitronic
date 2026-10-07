#!/usr/bin/env python3
"""Generate every platform icon / splash / tile for Orictron from one master image.

The master, art/source/icon-1024.png, is drawn by the game's own renderer:
    dotnet run -c Release --project tools/Orictron.Capture -- icon artifacts/capture/icon --script icon
(then resized to 1024 x 1024). This script cuts it into every size each platform wants.

Usage:  python3 tools/make_icons.py
Requires Pillow. macOS .icns needs `iconutil` (macOS only; skipped elsewhere).
"""
import os
import shutil
import subprocess
import sys

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GEN = os.path.join(ROOT, "art", "generated")
MASTER = os.path.join(ROOT, "art", "source", "icon-1024.png")


def save(img, *parts):
    path = os.path.join(*parts)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)
    return path


def backdrop(w, h):
    """The icon's deep-space gradient, for tiles and splashes of other shapes."""
    img = Image.new("RGB", (w, h))
    d = ImageDraw.Draw(img)
    for y in range(h):
        t = y / max(1, h - 1)
        d.line([(0, y), (w, y)], fill=(int(6 + 34 * t), int(10 + 0 * t), int(40 + 30 * t)))
    return img


def main():
    master = Image.open(MASTER).convert("RGB")
    save(master, GEN, "icon-1024.png")

    # iOS asset catalogue (no alpha allowed).
    ios_dir = os.path.join(ROOT, "src", "Orictron.iOS", "AppIcon.xcassets", "AppIcon.appiconset")
    for s in [20, 29, 40, 58, 60, 76, 80, 87, 120, 152, 167, 180, 1024]:
        save(master.resize((s, s), Image.LANCZOS), ios_dir, f"icon_{s}x{s}.png")
    shutil.copy(os.path.join(os.path.dirname(os.path.abspath(__file__)), "ios-appicon-contents.json"),
                os.path.join(ios_dir, "Contents.json"))

    # Android launcher icons and splash.
    res = os.path.join(ROOT, "src", "Orictron.Android", "Resources")
    for name, s in {"mdpi": 48, "hdpi": 72, "xhdpi": 96, "xxhdpi": 144, "xxxhdpi": 192}.items():
        save(master.resize((s, s), Image.LANCZOS), res, f"drawable-{name}", "icon.png")
    splash = backdrop(960, 540)
    splash.paste(master.resize((380, 380), Image.LANCZOS), (290, 80))
    save(splash, res, "drawable", "splash.png")

    # Windows .ico for the exe, and MSIX tiles.
    win = os.path.join(GEN, "windows")
    os.makedirs(win, exist_ok=True)
    master.save(os.path.join(win, "Orictron.ico"), sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])
    tiles = os.path.join(ROOT, "build", "windows", "Assets")
    for scale in (100, 200):
        f = scale / 100
        for name, s in {"Square44x44Logo": 44, "Square150x150Logo": 150, "StoreLogo": 50, "Square71x71Logo": 71}.items():
            save(master.resize((int(s * f), int(s * f)), Image.LANCZOS), tiles, f"{name}.scale-{scale}.png")
        wide = backdrop(int(310 * f), int(150 * f))
        logo = master.resize((int(150 * f), int(150 * f)), Image.LANCZOS)
        wide.paste(logo, ((wide.width - logo.width) // 2, 0))
        save(wide, tiles, f"Wide310x150Logo.scale-{scale}.png")
        sp = Image.new("RGBA", (int(620 * f), int(300 * f)), (0, 0, 0, 0))
        logo = master.resize((int(260 * f), int(260 * f)), Image.LANCZOS)
        sp.paste(logo, ((sp.width - logo.width) // 2, (sp.height - logo.height) // 2))
        save(sp, tiles, f"SplashScreen.scale-{scale}.png")
    for s in (16, 24, 32, 48, 256):
        save(master.resize((s, s), Image.LANCZOS), tiles, f"Square44x44Logo.targetsize-{s}_altform-unplated.png")

    # macOS .icns: Big Sur style rounded square with the standard margin.
    if sys.platform == "darwin" and shutil.which("iconutil"):
        iconset = os.path.join(GEN, "Orictron.iconset")
        shutil.rmtree(iconset, ignore_errors=True)
        os.makedirs(iconset)
        canvas = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
        inner = master.resize((832, 832), Image.LANCZOS)
        mask = Image.new("L", (832, 832), 0)
        ImageDraw.Draw(mask).rounded_rectangle([0, 0, 831, 831], radius=186, fill=255)
        canvas.paste(inner, (96, 96), mask)
        for s in (16, 32, 128, 256, 512):
            canvas.resize((s, s), Image.LANCZOS).save(os.path.join(iconset, f"icon_{s}x{s}.png"))
            canvas.resize((s * 2, s * 2), Image.LANCZOS).save(os.path.join(iconset, f"icon_{s}x{s}@2x.png"))
        subprocess.run(["iconutil", "-c", "icns", iconset, "-o", os.path.join(ROOT, "build", "macos", "Orictron.icns")], check=True)
        shutil.rmtree(iconset)

    print("icons written")


if __name__ == "__main__":
    main()
