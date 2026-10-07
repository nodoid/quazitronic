#!/usr/bin/env python3
"""Writes the self-contained support page (images embedded) to ~/Downloads/Orictron-Support.html.

    python3 tools/make_support_page.py

Uses the master icon and the Mac store captures in artifacts/capture/mac/stills.
"""
import base64
import io
import os

from PIL import Image

import local_settings

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STILLS = os.path.join(ROOT, "artifacts", "capture", "mac", "stills")
OUT = os.path.expanduser("~/Downloads/Orictron-Support.html")
CONTACT = local_settings.get("CONTACT_EMAIL", "you@example.com")


def data_uri(path, width, crop_hud=False, fmt="JPEG"):
    img = Image.open(path).convert("RGB")
    if crop_hud:
        img = img.crop((0, 0, img.width, int(img.height * 0.86)))
    img = img.resize((width, round(img.height * width / img.width)), Image.LANCZOS)
    buf = io.BytesIO()
    img.save(buf, fmt, quality=82) if fmt == "JPEG" else img.save(buf, fmt, optimize=True)
    return f"data:image/{fmt.lower()};base64," + base64.b64encode(buf.getvalue()).decode()


def still(name, width=640, crop_hud=False):
    return data_uri(os.path.join(STILLS, name + ".png"), width, crop_hud)


ICON = data_uri(os.path.join(ROOT, "art", "generated", "icon-1024.png"), 144, fmt="PNG")
FAVICON = data_uri(os.path.join(ROOT, "art", "generated", "icon-1024.png"), 64, fmt="PNG")

DROIDS = [
    ("M1", "Messenger unit", "Unarmed", 12, 50), ("S2", "Sentry droid", "Stands still and shoots", 18, 100),
    ("W3", "Worker unit", "Unarmed", 20, 100), ("G4", "Guard robot", "Armed", 26, 200),
    ("B5", "Battle robot", "Armed", 34, 300), ("R6", "Repair robot", "Armed", 30, 300),
    ("B7", "Battle droid", "Heavily armed", 44, 500), ("X9", "Command unit", "The deadliest; waits on deck 6", 64, 1000),
]

droid_rows = "\n".join(f"<tr><td><strong>{c}</strong></td><td>{n}</td><td>{w}</td><td>{a}</td><td>{p}</td></tr>" for c, n, w, a, p in DROIDS)

HTML = f"""<!doctype html>
<html lang="en-GB">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Orictron Support</title>
<meta name="description" content="Help and support for Orictron, the Oric Atmos Quazatron, for iPhone, iPad, Android, Mac and Windows: controls, how to play, questions, privacy and contact.">
<link rel="icon" href="{FAVICON}">
<style>
:root {{ --bg: #F3F2F7; --pane: #FFFFFF; --chip: #E6E4EE; --text: #1B1A24; --muted: #62607A;
  --accent: #A05A00; --accent-2: #2C4AA8; --border: #DAD7E6; }}
@media (prefers-color-scheme: dark) {{
  :root:not([data-theme="light"]) {{ --bg: #070A22; --pane: #11163A; --chip: #1E2450; --text: #ECEBF5;
    --muted: #A6A8C8; --accent: #FFC93C; --accent-2: #7FD8FF; --border: #262E66; }}
}}
:root[data-theme="dark"] {{ --bg: #070A22; --pane: #11163A; --chip: #1E2450; --text: #ECEBF5;
  --muted: #A6A8C8; --accent: #FFC93C; --accent-2: #7FD8FF; --border: #262E66; }}
* {{ box-sizing: border-box; }}
html {{ scroll-behavior: smooth; }}
body {{ margin: 0; background: var(--bg); color: var(--text);
  font: 16px/1.6 -apple-system, BlinkMacSystemFont, "Segoe UI", system-ui, Roboto, "Helvetica Neue", Arial, sans-serif; }}
a {{ color: var(--accent); }}
.wrap {{ max-width: 1000px; margin: 0 auto; padding: 0 16px; }}
header {{ background: var(--pane); border-bottom: 1px solid var(--border); }}
header .wrap {{ padding-top: 32px; padding-bottom: 26px; }}
.brand {{ display: flex; align-items: center; gap: 16px; }}
.brand img {{ width: 72px; height: 72px; border-radius: 16px; }}
.eyebrow {{ color: var(--accent-2); font-weight: 600; margin: 0; }}
h1 {{ font-size: clamp(28px, 5vw, 40px); line-height: 1.15; margin: 0; }}
.lead {{ font-size: 18px; color: var(--muted); margin: 16px 0 0; max-width: 48em; }}
.hero {{ width: 100%; border-radius: 14px; margin-top: 22px; display: block; }}
nav.toc {{ display: flex; flex-wrap: wrap; gap: 8px; margin-top: 22px; }}
nav.toc a {{ text-decoration: none; color: var(--text); background: var(--chip); border-radius: 999px; padding: 6px 14px; font-size: 14px; }}
main section {{ background: var(--pane); border: 1px solid var(--border); border-radius: 14px; padding: 8px 24px 16px; margin: 20px 0; }}
h2 {{ font-size: 24px; margin: 16px 0 8px; }}
h3 {{ font-size: 17px; margin: 18px 0 2px; }}
table {{ width: 100%; border-collapse: collapse; font-size: 15px; }}
.table {{ overflow-x: auto; }}
th, td {{ text-align: left; padding: 8px 10px; border-bottom: 1px solid var(--border); vertical-align: top; }}
th {{ color: var(--muted); font-weight: 600; }}
kbd {{ font: 13px ui-monospace, SFMono-Regular, Menlo, monospace; background: var(--chip); border: 1px solid var(--border);
  border-radius: 5px; padding: 1px 5px; }}
.gallery {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px; margin: 14px 0 6px; }}
.gallery img {{ width: 100%; border-radius: 10px; display: block; }}
.pixel {{ image-rendering: pixelated; }}
footer {{ color: var(--muted); font-size: 14px; padding: 8px 0 40px; }}
</style>
</head>
<body>
<header><div class="wrap">
  <div class="brand"><img src="{ICON}" alt="">
    <div><p class="eyebrow">Support</p><h1>Orictron</h1></div></div>
  <p class="lead">The Oric Atmos Quazatron: take over the droids and clear the decks. For iPhone, iPad, Android, Mac and Windows.</p>
  <img class="hero" src="{still('02-deck', 960, True)}" alt="Orictron: a deck of the ship in enhanced 3D">
  <nav class="toc"><a href="#about">The game</a><a href="#modes">Enhanced and original</a><a href="#controls">Controls</a><a href="#playing">How to play</a><a href="#droids">The droids</a><a href="#faq">Questions</a><a href="#privacy">Privacy</a><a href="#contact">Contact</a></nav>
</div></header>
<main class="wrap">
<section id="about"><h2>The game</h2>
<p>A ship drifts through space, overrun by rogue droids. You are the influence device, a small robot that can take over any droid's body. Shoot them, ram them, or grapple one and win the transfer battle to make it yours, then clear all six decks.</p>
<p>Orictron is the Oric Atmos version of <em>Quazatron</em>, Graftgold's 1986 ZX Spectrum classic published by Hewson. It has no ads, no in-app purchases and no tracking, and it never needs the internet.</p>
<div class="gallery"><img src="{still('03-combat', 480, True)}" alt="A droid explodes"><img src="{still('05-transfer', 480)}" alt="The transfer battle"><img src="{still('06-deep', 480, True)}" alt="Deck four"></div></section>

<section id="modes"><h2>Enhanced and original</h2>
<p>Orictron comes two ways. Choose with the <em>Graphics</em> line on the title screen (or press <kbd>G</kbd> there on a computer).</p>
<ul>
<li><strong>Enhanced</strong> (the default) is the game remade with the same rules, droids and decks: lit, solid 3D decks with glowing pads and energisers, 3D droids with floating lids, smooth scrolling, explosions and sparks, and new sound effects and music.</li>
<li><strong>Original</strong> is the actual Oric Atmos tape, running in a built-in emulator of the Oric computer, with its own title screen, demo, chip sound and timing.</li>
</ul>
<div class="gallery"><img src="{still('01-intro', 480)}" alt="The title screen"><img class="pixel" src="{still('07-original', 480)}" alt="The original Oric version"></div></section>

<section id="controls"><h2>Controls</h2><div class="table"><table><thead><tr><th></th><th>Mac and Windows</th><th>iPhone, iPad and Android</th></tr></thead><tbody>
<tr><td>Move</td><td>Arrow keys, or <kbd>Q</kbd> <kbd>A</kbd> <kbd>O</kbd> <kbd>P</kbd> as on the Oric</td><td>Tilt the device (or the on-screen D-pad)</td></tr>
<tr><td>Fire</td><td><kbd>Space</kbd></td><td><em>FIRE</em></td></tr>
<tr><td>Grapple</td><td><kbd>T</kbd> or <kbd>Return</kbd>; or hold <kbd>Space</kbd> standing still, then run into a droid</td><td><em>GRAB</em>; or hold <em>FIRE</em> standing still</td></tr>
<tr><td>Ride the lift</td><td><kbd>L</kbd> on the lift hatch</td><td><em>LIFT</em> (appears on the hatch)</td></tr>
<tr><td>Pause</td><td><kbd>Esc</kbd></td><td>The <em>II</em> button; Back on Android</td></tr>
<tr><td>Transfer battle</td><td><kbd>↑</kbd> / <kbd>↓</kbd> choose a wire, <kbd>Space</kbd> fires a pulse</td><td>Tilt to choose and <em>FIRE</em>, or just tap a wire</td></tr>
<tr><td>Full screen</td><td><kbd>F11</kbd> or <kbd>Alt</kbd>+<kbd>Enter</kbd></td><td>Always</td></tr>
</tbody></table></div>
<p>Movement follows the screen: up moves up the screen, left moves left, and so on, so the isometric deck is easy to steer. Game controllers work everywhere: the stick or D-pad moves, A fires, X grapples, Y rides the lift and Start pauses.</p>
<p><strong>Tilt:</strong> however you hold the device when play starts, or when you resume from pause, counts as level. Tip the top edge away to go up the screen, towards you to go down, and left or right to go across. Choose <em>Controls</em> on the title screen to use the on-screen D-pad instead.</p></section>

<section id="playing"><h2>How to play</h2><ul>
<li><strong>Clear the ship.</strong> Six decks hold 52 droids. Destroy or capture every droid on a deck, then take the lift where you arrived to reach the next.</li>
<li><strong>Levels.</strong> The arrow pads are the only way up or down one level of a deck.</li>
<li><strong>Energy.</strong> Your bar is in the yellow panel. Glowing energiser pads recharge you.</li>
<li><strong>Grapple and transfer.</strong> Grappling a droid starts a battle for its body. Your side is yellow, the droid's blue. Fire pulses down the wires: when one reaches the middle, its cell turns your colour for a few seconds. Some wires are dead ends; some split to feed two cells. Hold more of the 13 cells when the clock runs out to take the droid over. A draw is a deadlock, and you fight again.</li>
<li><strong>Hosts.</strong> A captured droid gives you its speed, armour and weapon, but its body slowly burns out. If it is destroyed you are thrown out as the influence device; if that is destroyed the game is over.</li>
<li><strong>Points.</strong> A destroyed droid scores its value, a captured one double, and each cleared deck 500. Your five best scores are kept.</li>
<li><strong>Original mode.</strong> Press <kbd>Space</kbd> (or tap the screen) on the Oric's title screen to start; leave it alone and the original plays a demo. To abandon a game, use the pause menu's <em>Abandon game</em>, which presses the Oric's <kbd>Esc</kbd>.</li>
</ul></section>

<section id="droids"><h2>The droids</h2><div class="table"><table><thead><tr><th>Code</th><th>Class</th><th>Weapon</th><th>Armour</th><th>Points</th></tr></thead><tbody>
{droid_rows}
</tbody></table></div>
<p>Higher class numbers are faster, tougher and hit harder. Your own influence device has armour 30 and a light gun.</p>
<div class="gallery"><img src="{still('08-droids', 640)}" alt="The droid classes"><img src="{still('04-briefing', 640)}" alt="Prepare to engage"></div></section>

<section id="faq"><h2>Questions</h2>
<h3>Tilting doesn't move the way I expect.</h3><p>The game takes the way you're holding the device when play starts, or when you resume from pause, as level. Pause and resume to re-centre it, or switch to the D-pad with <em>Controls</em> on the title screen.</p>
<h3>How do I switch between the enhanced and original game?</h3><p>Use the <em>Graphics</em> line on the title screen. The two are separate games: the original is the real Oric tape, so a game started in one can't continue in the other.</p>
<h3>Why does the original look and sound so different?</h3><p>It is the Oric Atmos version exactly as it was: 240 by 224 pixels, eight colours and the computer's AY sound chip, running at the Oric's own speed.</p>
<h3>How do I turn off the sound or music?</h3><p>Use the <em>Sound</em> and <em>Music</em> lines on the title screen, or <em>Sound</em> in the pause menu.</p>
<h3>Where are my scores kept?</h3><p>Only on your device, in the game's own storage. Uninstalling the game deletes them.</p>
<h3>Can I play with a controller?</h3><p>Yes, on Mac, Windows, iPad, iPhone and Android, with any controller the system recognises.</p></section>

<section id="privacy"><h2>Privacy</h2><p><strong>Orictron collects nothing.</strong> It has no accounts, advertising, analytics or tracking, and it never connects to the internet. It doesn't collect, store, share or sell any personal information, from anyone, including children.</p>
<p>On phones and tablets, tilt controls read the device's motion sensor while you play, only to steer; nothing is recorded. The game keeps one small file in its private storage on your device with your five best scores, games played and settings (graphics, sound, music and controls). It never leaves your device and is deleted when you uninstall the game.</p>
<p>If this policy changes, the new version will be posted here with a new date. Last updated: 7 October 2026.</p></section>

<section id="contact"><h2>Contact</h2><p>Found a bug, or have a question this page doesn't answer? Email <a href="mailto:{CONTACT}?subject=Orictron%20support">{CONTACT}</a>.</p></section>
</main>
<footer class="wrap">Written by PFJ, based on the Oric port of the ZX Spectrum game by Hewson. <em>Quazatron</em> was created by Graftgold and published by Hewson in 1986; Orictron is an unofficial tribute, isn't affiliated with them, and contains none of their code or graphics. © 2026 Paul F. Johnson.</footer>
</body>
</html>
"""

with open(OUT, "w", encoding="utf-8") as f:
    f.write(HTML)
print("wrote", OUT, len(HTML) // 1024, "KB")
