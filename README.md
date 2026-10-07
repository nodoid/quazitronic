# Orictron

**The Oric Atmos Quazatron**, for iOS/iPadOS, Android, macOS and Windows, made with MonoGame.

Orictron is the Oric Atmos port of Graftgold's 1986 ZX Spectrum game *Quazatron* (published by
Hewson). This app has it two ways, switched from the title screen:

- **ENHANCED** (the default): the game remade in C#, rule for rule, with new graphics and sound.
  The decks are real 3D geometry seen through the original's isometric projection, with
  lighting, glowing pads and energisers, 3D droids with floating lids, shadows, particle
  explosions, smooth scrolling at the display's refresh rate, a glass status panel after the
  original's three capsules, a neon transfer battle, synthesised sound effects and music.
- **ORIGINAL**: the Oric version's own look and sound, recreated natively (no emulation): its
  decks, droid sprites, font, status panel and screens are drawn from the Oric game's graphics data
  by a C# port of its drawing code, and its sound effects are played by a small square-wave synth.

Both looks run the same game, so you can switch between them at any time, even mid-game.

Phones and tablets steer by **tilting** (or an on-screen D-pad), with FIRE / GRAB / LIFT buttons.
The title screen has the line "Written by PFJ, Based on the Oric port of the ZX Spectrum game by
Hewson", scrolling left to right.

| | |
|---|---|
| Identifiers | Apple and Android `uk.co.allthejohnsons.orictron` |
| Controls | Keyboard (arrows or Q A O P, SPACE, T, L, ESC), gamepad, tilt + touch on phones and tablets |
| Version | 1.0.0 (build 1), set in `Directory.Build.props` |

## Repository layout

```
src/Orictron.Core      Shared code: the game, both renderers, audio, screens, input, saves
src/Orictron.Desktop   Windows + macOS head (MonoGame DesktopGL)
src/Orictron.Android   Android head (+ tilt sensor)
src/Orictron.iOS       iOS/iPadOS head (+ CoreMotion tilt)
tests/Orictron.Tests   xUnit: rules, transfer, decks, original screen, sound, tilt, saves, audio, UI
tools/                 Capture tool (store stills, videos, icon), icon/store/copy scripts, deck and graphics export
build/                 Release script, macOS bundle files, Windows MSIX manifest and tiles
art/                   Master icon (rendered by the game) and generated icon files
docs/                  Architecture, building, releasing, playing
```

Not in git: `oricport/` (the Oric original's source), `releases/`, `stores/`, `artifacts/`, `signing/`.

## Quick start

```bash
dotnet run --project src/Orictron.Desktop      # play on Mac/Windows (G on the title switches enhanced/original)
dotnet test tests/Orictron.Tests                # unit tests
build/build-release.sh all                      # release packages -> releases/
```

See [docs/PLAYING.md](docs/PLAYING.md), [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md),
[docs/BUILDING.md](docs/BUILDING.md) and [docs/RELEASING.md](docs/RELEASING.md).

By PFJ. Released under the [DILLIGAF License](LICENSE): do what you like with the code.

Quazatron is © 1986 Graftgold / Hewson Consultants. Orictron is an unofficial tribute: it contains
none of their code or graphics.
