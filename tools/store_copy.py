#!/usr/bin/env python3
"""Writes the store listing copy: ONE file per platform (stores/copy/en/{ios,macos,android,windows}.txt),
each holding every field that store's submission form asks for, in console order, with character
counts checked against the store's limits. Edit the text here and re-run:

    python3 tools/store_copy.py
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import local_settings  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "stores", "copy", "en")
VERSION = re.search(r"<OrictronVersion>(.*?)</OrictronVersion>", open(os.path.join(ROOT, "Directory.Build.props")).read()).group(1)
SUPPORT = "https://<your web site>/Orictron-Support.html"
CONTACT = local_settings.get("CONTACT_EMAIL", "<your email>")
WIN_IDENTITY = local_settings.get("WINDOWS_IDENTITY", "<package identity>")
WIN_PUBLISHER = local_settings.get("WINDOWS_PUBLISHER", "<publisher>")
WIN_PUBLISHER_NAME = local_settings.get("WINDOWS_PUBLISHER_NAME", "<publisher display name>")
NAME = "Orictron"
errors = []

# ---------------------------------------------------------------- shared text

INTRO = ("A ship drifts through space, overrun by rogue droids. You are the influence device, a small robot "
         "that can take over any droid's body. Shoot them, ram them, or grapple one and win the transfer battle "
         "to make it yours, then clear all six decks.")

ABOUT = ("Orictron is the Oric Atmos version of Quazatron, the 1986 ZX Spectrum classic. This app gives it to you "
         "two ways: ENHANCED, with new 3D graphics, sound and music, and ORIGINAL, the Oric version's own "
         "eight-colour look and chip sound, faithfully recreated. Switch between them at any time, even mid-game.")

FEATURES = [
    "Six decks of smooth-scrolling isometric ship, now in lit, solid 3D with glowing energisers, step pads and lifts",
    "52 droids in nine classes, from harmless messengers to the X9 command unit, each a 3D model with its own lid",
    "The transfer battle: fire pulses down the wires and hold more cells than the droid to take it over",
    "Take over a bigger droid and you are faster, tougher and deadlier - until its body burns out",
    "New synthesised sound effects and music, with explosions, sparks and shimmering lights",
    "The original Oric look and sound, recreated: switch to it at any moment",
    "Instructions built in, and your five best scores saved",
]

CONTROLS = {
    "mobile": "Tilt your iPhone or iPad to move, with FIRE, GRAB and LIFT buttons; or use the on-screen D-pad. Game controllers work too",
    "mac": "Keyboard (arrows or Q A O P, as on the Oric) or a game controller, in a resizable window or full screen",
    "android": "Tilt to move, with FIRE, GRAB and LIFT buttons, or use the on-screen D-pad; game controllers work too",
    "windows": "Keyboard (arrows or Q A O P, as on the Oric) or an Xbox controller, in a resizable window or full screen (F11)",
}

CREDIT = ("Written by PFJ, based on the Oric port of the ZX Spectrum game by Hewson. Quazatron was created by "
          "Graftgold and published by Hewson in 1986; this is an unofficial tribute, not affiliated with them, and "
          "contains none of their code or graphics. No ads, no tracking, no internet connection needed.")

WHATS_NEW = ("First release: the Oric Atmos Quazatron, remade with new 3D graphics, sound and music, plus the original "
             "Oric version's look and sound, recreated. Tilt controls on phones and tablets.")


def description(platform, bullet="•"):
    lines = [INTRO, "", ABOUT, "", "FEATURES", ""]
    lines += [f"{bullet} {f}" for f in FEATURES + [CONTROLS[platform]]]
    lines += ["", CREDIT]
    return "\n".join(lines)


REVIEW_NOTES = """Orictron is an offline, single-player arcade game. No account, sign-in, network connection or purchase is needed; everything is available immediately. The app collects no data.

HOW TO PLAY
On the title screen choose PLAY. INSTRUCTIONS explains the game in five pages. GRAPHICS switches between ENHANCED (new 3D graphics and sound, the default) and ORIGINAL (the look and sound of the 1980s Oric Atmos version, recreated in the app); G or the pause menu also switches, even mid-game.
{controls}

QUICK TEST (ENHANCED)
- Move around the deck; FIRE shoots in the direction you last moved.
- Touch a droid and press GRAB (or hold FIRE while standing still, then walk into one) to start a transfer battle: choose a wire and fire pulses; whoever holds more of the 13 middle cells when the timer ends wins.
- Stand on the round lift hatch where you started and press LIFT to go to the next deck.

QUICK TEST (ORIGINAL)
- Set GRAPHICS to ORIGINAL and choose PLAY: the same game, drawn and played the way the Oric version looked and sounded."""

REVIEW_CONTROLS = {
    "ios": "On iPhone and iPad, movement is by tilting the device: however it is held when play starts counts as level. Tip it away to go up the screen, towards you to go down, left or right to go across. FIRE, GRAB and LIFT are on-screen buttons; II pauses. The title menu's CONTROLS line switches to an on-screen D-pad if tilting is inconvenient in review.",
    "macos": "Arrow keys (or Q A O P) move, Space fires, T grapples, L rides the lift, Esc pauses, F11 toggles full screen, G switches graphics on the title screen. A game controller also works.",
}

APPLE_AGE = [
    ("Parental controls / age assurance", "No / No"),
    ("Unrestricted web access", "No"),
    ("User-generated content", "No"),
    ("Messaging and chat", "No"),
    ("Advertising", "No"),
    ("Profanity or crude humour", "None"),
    ("Horror or fear themes", "None"),
    ("Alcohol, tobacco or drug use or references", "None"),
    ("Medical or treatment information", "None"),
    ("Health or wellness topics", "No"),
    ("Sexual content or nudity", "None"),
    ("Cartoon or fantasy violence", "Infrequent/Mild (shooting robots)"),
    ("Realistic violence", "None"),
    ("Prolonged graphic or sadistic violence", "None"),
    ("Guns or other weapons", "None"),
    ("Simulated gambling / contests / loot boxes", "None / No / No"),
    ("Result", "9+"),
]

IARC = [
    ("Category", "Game"),
    ("Violence", "Fantasy violence: shooting and ramming robots, which explode; no humans, no blood, no gore"),
    ("Fear", "No"),
    ("Sexuality / nudity / crude language", "No / No / No"),
    ("Controlled substances", "No"),
    ("Gambling", "No"),
    ("Users interact or share content", "No"),
    ("Shares location / digital purchases", "No / No"),
    ("Unrestricted internet", "No"),
]

# ---------------------------------------------------------------- writing helpers


class Doc:
    def __init__(self, title):
        self.parts = [title, "=" * len(title)]

    def field(self, name, text, limit=None):
        n = len(text)
        head = f"{name}  ({n}/{limit} characters)" if limit else name
        if limit and n > limit:
            errors.append(f"{self.parts[0]}: {name} is {n} > {limit}")
        self.parts += ["", head, "-" * len(head), text]

    def lines(self, name, items, limit_each=None, max_items=None, note=""):
        longest = max(len(i) for i in items)
        head = name + (f"  (longest line {longest}/{limit_each})" if limit_each else "") + note
        if limit_each and longest > limit_each:
            errors.append(f"{self.parts[0]}: {name} line {longest} > {limit_each}")
        if max_items and len(items) > max_items:
            errors.append(f"{self.parts[0]}: {name} has {len(items)} > {max_items}")
        self.parts += ["", head, "-" * len(head)] + items

    def table(self, name, rows):
        w = max(len(k) for k, _ in rows)
        self.parts += ["", name, "-" * len(name)] + [f"{k.ljust(w)}  {v}" for k, v in rows]

    def save(self, filename):
        os.makedirs(OUT, exist_ok=True)
        with open(os.path.join(OUT, filename), "w", encoding="utf-8") as f:
            f.write("\n".join(self.parts) + "\n")


KEYWORDS = "quazatron,retro,oric,8-bit,droid,robot,isometric,arcade,classic,shooter,spectrum,80s,8bit"

# ---------------------------------------------------------------- the four files


def apple(platform):
    ios = platform == "ios"
    d = Doc(f"ORICTRON - {'APP STORE (IPHONE AND IPAD)' if ios else 'MAC APP STORE'} - ENGLISH")
    d.parts.append(f"App Store Connect > {NAME} > {'iOS' if ios else 'macOS'} App > {VERSION} > English (U.K.)")
    d.field("NAME", NAME, 30)
    d.field("SUBTITLE", "The Oric Atmos Quazatron", 30)
    d.field("PROMOTIONAL TEXT", "Take over the droids, clear the decks. The 8-bit classic remade in lit 3D with new "
            "sound and music - or switch to the original Oric look at any time.", 170)
    d.field("KEYWORDS", KEYWORDS, 100)
    d.field("DESCRIPTION", description("mobile" if ios else "mac"), 4000)
    d.field("WHAT'S NEW", WHATS_NEW, 4000)
    if ios:
        files = [
            "- iPhone 6.9\" Dynamic Island, large: stores/ios/iphone-dynamic-island-large/ (8 x 2868x1320)",
            "- iPhone 6.3\" Dynamic Island, medium (REQUIRED): stores/ios/iphone-dynamic-island-medium/ (8 x 2622x1206)",
            "- iPhone Face ID, large: stores/ios/iphone-face-id-large/ (8 x 2778x1284)",
            "- iPad 13\" (REQUIRED): stores/ios/ipad-13in/ (8 x 2752x2064)",
            "- App previews (English): stores/ios/app-preview-iphone-1920x886.mp4, stores/ios/app-preview-ipad-1600x1200.mp4",
            f"- Build: releases/Orictron-{VERSION}-ios.ipa (Transporter)",
        ]
    else:
        files = [
            "- Screenshots: stores/macos/ (8 x 2880x1800)",
            "- App preview (English): stores/macos/app-preview-mac-1920x1080.mp4",
            f"- Build: releases/Orictron-{VERSION}-macos.pkg (Transporter)",
        ]
    d.lines("FILES TO UPLOAD", files)
    d.table("ANSWERS IN THE CONSOLE", [
        ("Bundle ID", "uk.co.allthejohnsons.orictron"),
        ("SKU", "orictron"),
        ("Primary language", "English (U.K.)"),
        ("Category", "Games > Arcade (secondary: Games > Action)"),
        ("Price", "Free (or your choice); no in-app purchases"),
        ("Support URL", f"{SUPPORT}  (upload ~/Downloads/Orictron-Support.html)"),
        ("Privacy Policy URL", f"{SUPPORT}#privacy"),
        ("Copyright", "© 2026 Paul F. Johnson"),
        ("App Privacy", "Data Not Collected; no tracking"),
        ("Encryption", "None (ITSAppUsesNonExemptEncryption is already false in Info.plist)"),
        ("Sign-in required", "No (App Review Information: untick \"Sign-in required\")"),
        ("Devices", "iPhone and iPad, landscape, iOS 15+" if ios else "Apple silicon Macs, macOS 12+"),
        ("Contact", f"Paul F. Johnson, {CONTACT}"),
    ])
    d.table("AGE RATING", APPLE_AGE)
    notes = REVIEW_NOTES.format(controls=REVIEW_CONTROLS[platform], start="tap the screen or press FIRE" if ios else "press Space")
    d.field("APP REVIEW NOTES", notes, 4000)
    d.save(f"{platform}.txt")


def android():
    d = Doc("ORICTRON - GOOGLE PLAY - ENGLISH")
    d.parts.append(f"Play Console > {NAME} > Grow > Store presence > Main store listing > English (United Kingdom)")
    d.field("APP NAME", NAME, 30)
    d.field("SHORT DESCRIPTION", "Take over the droids, clear the decks: the Oric Atmos Quazatron, remade in 3D.", 80)
    d.field("FULL DESCRIPTION", description("android"), 4000)
    d.field("RELEASE NOTES (en-GB)", WHATS_NEW, 500)
    d.lines("FILES TO UPLOAD", [
        "- App icon: stores/android/icon-512.png",
        "- Feature graphic: stores/android/feature-graphic-1024x500.png",
        "- Phone screenshots: stores/android/phone/ (8 x 1920x1080)",
        "- 7-inch tablet: stores/android/tablet-7in/ (1920x1200); 10-inch tablet: stores/android/tablet-10in/ (2560x1600)",
        f"- App bundle: releases/Orictron-{VERSION}-android.aab (Production or a testing track)",
    ])
    d.table("ANSWERS IN THE CONSOLE", [
        ("Package name", "uk.co.allthejohnsons.orictron"),
        ("App or game", "Game"),
        ("Category", "Arcade"),
        ("Tags", "Arcade, Retro, Shooter, Robots, Single player, Offline"),
        ("Free or paid", "Free; no in-app purchases"),
        ("Contains ads", "No"),
        ("Email", CONTACT),
        ("Website", f"{SUPPORT}  (upload ~/Downloads/Orictron-Support.html)"),
        ("Privacy policy", f"{SUPPORT}#privacy"),
        ("App access", "All functionality available without special access"),
        ("Target audience", "13 and over (retro arcade game; not designed for children)"),
        ("Data safety", "No data collected; no data shared; nothing to encrypt in transit"),
        ("Government app / financial / health", "No / No / No"),
        ("Play App Signing", "Enrol; upload key signing/orictron-upload.keystore (alias orictron)"),
    ])
    d.table("CONTENT RATING (IARC)", IARC + [("Expected result", "PEGI 7 / ESRB Everyone 10+ (or similar)")])
    d.save("android.txt")


def windows():
    d = Doc("ORICTRON - MICROSOFT STORE - ENGLISH")
    d.parts.append(f"Partner Center > {NAME} > Submission > Store listings > en-gb")
    d.field("PRODUCT NAME", NAME, 256)
    d.field("SHORT DESCRIPTION", "The Oric Atmos Quazatron. You are the influence device aboard a ship overrun by rogue "
            "droids: shoot them, ram them, or win the transfer battle to take one over, and clear all six decks. Play the "
            "enhanced remake in lit 3D with new sound and music, or switch to the original Oric look and sound at any time.", 1000)
    d.field("DESCRIPTION", description("windows"), 10000)
    d.lines("PRODUCT FEATURES (ONE PER BOX)", [
        "The Oric Atmos Quazatron, remade in lit, solid isometric 3D",
        "52 droids in nine classes, each a 3D model with its own lid",
        "The transfer battle: win it to take over a droid's body",
        "New synthesised sound effects and music",
        "The original Oric look and sound, recreated, switchable mid-game",
        "Switch between enhanced and original on the title screen (G)",
        "Keyboard (as on the Oric) and Xbox controller support",
        "Resizable window or full screen; runs natively on x64 and Arm PCs",
    ], 200, 20)
    d.lines("SEARCH TERMS (ONE PER BOX)", ["quazatron", "retro", "oric", "droid", "isometric", "arcade", "8-bit"], 30, 7)
    d.field("WHAT'S NEW IN THIS VERSION", WHATS_NEW, 1500)
    d.field("SHORT TITLE", "Orictron", 50)
    d.field("NOTES FOR CERTIFICATION",
            "Offline single-player arcade game; no account, sign-in, network or purchase needed. On the title screen choose "
            "PLAY (Enter). Arrow keys or Q A O P move, Space fires, T grapples a droid, L rides the lift, Esc pauses, F11 "
            "toggles full screen. G on the title screen (or the GRAPHICS line) switches between the enhanced remake and "
            "ORIGINAL, the look and sound of the 1980s Oric Atmos version, recreated (also switchable mid-game with G). An Xbox controller works throughout. Best scores are saved in the "
            "app's own data folder. The package is a full-trust desktop app (see restricted capabilities).", 2000)
    d.lines("FILES TO UPLOAD", [
        "- Screenshots (8, no text on them): stores/windows/ (1920x1080); captions below",
        "- 1:1 App tile icon: stores/windows/art/app-tile-icon-300x300.png",
        "- 2:3 Poster art (title in top two-thirds): stores/windows/art/poster-art-1440x2160.png (also 720x1080)",
        "- 1:1 Box art (title in top two-thirds): stores/windows/art/box-art-2160x2160.png (also 1080x1080)",
        "- 16:9 Super hero art (no text): stores/windows/art/super-hero-art-3840x2160.png (also 1920x1080)",
        "- Xbox branded key art: stores/windows/xbox/branded-key-art-584x800.png",
        "- Xbox titled hero art: stores/windows/xbox/titled-hero-art-1920x1080.png",
        "- Xbox featured promotional square art (no title): stores/windows/xbox/featured-promotional-square-art-1080x1080.png",
        "- Trailer: stores/windows/trailer/trailer-1920x1080.mp4 with thumbnail trailer-thumbnail-1920x1080.png",
        f"- Packages (upload both): releases/Orictron-{VERSION}-windows-x64.msix and -windows-arm64.msix",
    ])
    d.lines("SCREENSHOT CAPTIONS (IN ORDER)", CAPTIONS, 200)
    d.field("TRAILER TITLE", "Orictron - gameplay", 255)
    d.field("RESTRICTED CAPABILITIES",
            "Submission options > \"Why does your app need these capabilities?\" (runFullTrust is the only one declared):\n\n"
            "runFullTrust: Orictron is a packaged desktop (Win32) game, built with .NET 10 and MonoGame (SDL2 with OpenGL "
            "for graphics, OpenAL for sound). Every packaged Win32 desktop app needs runFullTrust to start its executable "
            "(Windows.FullTrustApplication entry point). The game uses it only to run its own process. It does not use the "
            "internet, other apps or processes, the user's documents or system settings, and needs no elevation. It reads "
            "the keyboard, mouse and game controllers, draws with OpenGL, plays sound, and saves its settings and best "
            "scores in its own app data folder.", 1000)
    d.table("SYSTEM REQUIREMENTS", [
        ("OS", "Windows 10 version 1809 (build 17763) or later; Windows 11"),
        ("Architecture", "x64 and Arm64 (native package for each)"),
        ("Graphics (minimum)", "OpenGL 3.0 capable GPU and driver"),
        ("Memory (minimum / recommended)", "2 GB / 4 GB"),
        ("Storage", "About 150 MB"),
        ("Input", "Keyboard (required); mouse and Xbox controller optional"),
        ("Network", "Not required"),
    ])
    d.table("PRODUCT DECLARATIONS", [
        ("Accesses, collects or transmits personal information", "No"),
        ("Tested to meet accessibility guidelines", "No (leave unticked)"),
        ("Customers can install to alternate drives / removable storage", "Yes"),
        ("Allow Windows to back up app data", "Yes"),
        ("Depends on non-Microsoft drivers or NT services", "No"),
        ("Requires a Windows Mixed Reality headset", "No"),
        ("Uses the Microsoft Store in-app purchase system", "No"),
    ])
    d.table("ANSWERS IN THE CONSOLE", [
        ("Package identity", WIN_IDENTITY),
        ("Publisher", f"{WIN_PUBLISHER} (check Partner Center > Product identity)"),
        ("Publisher display name", WIN_PUBLISHER_NAME),
        ("Category", "Games > Action & adventure (subcategory Arcade if offered)"),
        ("Pricing", "Free; no in-app purchases"),
        ("Age ratings", "IARC questionnaire: same answers as below"),
        ("Privacy policy URL", f"{SUPPORT}#privacy"),
        ("Website", f"{SUPPORT}  (upload ~/Downloads/Orictron-Support.html)"),
        ("Support contact", CONTACT),
        ("Copyright", "© 2026 Paul F. Johnson"),
        ("Game options", "Single player; Xbox controller supported; no online play"),
        ("Display", "Windowed and full screen; landscape"),
        ("Languages", "en-gb, en-us (as declared in the package manifest)"),
    ])
    d.table("CONTENT RATING (IARC)", IARC)
    d.save("windows.txt")


# The screenshot order and captions (store_assets.py uses the same list).
CAPTIONS = [
    "01  The title screen: the Oric Atmos Quazatron, remade",
    "02  Six decks of isometric starship in lit, solid 3D",
    "03  Shoot, ram or grapple 52 rogue droids",
    "04  Grapple a droid and prepare to engage",
    "05  The transfer battle: hold more cells to take the droid over",
    "06  Deeper decks, tougher droids",
    "07  ORIGINAL mode: the Oric version's own look and sound",
    "08  Nine droid classes, explained in the instructions",
]


def main():
    for f in os.listdir(OUT) if os.path.isdir(OUT) else []:
        os.remove(os.path.join(OUT, f))  # this script owns stores/copy/en
    apple("ios")
    apple("macos")
    android()
    windows()
    if errors:
        print("OVER LIMIT:\n  " + "\n  ".join(errors))
        sys.exit(1)
    print("wrote", ", ".join(sorted(os.listdir(OUT))))


if __name__ == "__main__":
    main()
