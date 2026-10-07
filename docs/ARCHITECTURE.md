# Architecture

```
OrictronGame (MonoGame Game: 224-high adaptive virtual screen, settings, audio, screens)
 ├─ Emulation/  Cpu6502, Via6522, Ay38912, OricMachine (RAM, keyboard matrix, ULA renderer), TapFile, PngWriter
 ├─ Game/       Session (the remade game, a port of the original's main.c), Transfer, Autopilot, Droids,
 │              Deck + DeckData.g.cs (the six decks, exported from the original's generator)
 ├─ Graphics/   IsoRenderer (3D isometric deck, droids, glows), DeckView (25 Hz sim drawn interpolated),
 │              Particles, TextureFactory (procedural atlas + logo), Gfx (2D), BitmapFont (Oric font + smoothed)
 ├─ Audio/      AudioEngine (one streaming mixer), Sounds (synthesised effects), Music (title + deck loops), Synth
 ├─ Input/      InputState (keys, pad, mouse, touch buttons, tilt), TiltController
 ├─ Screens/    IntroScreen, InstructionsScreen, PlayScreen (enhanced), OriginalScreen (emulator), Ui
 └─ Persistence/ SaveData (settings, best five scores), SaveStore (atomic JSON + backup)
```

## ORIGINAL: the tape in an emulator

`orictron.tap` is embedded in Orictron.Core. The game disables interrupts and never calls the ROM, so
the emulator needs no ROM image: `OricMachine.Boot` copies the tape into RAM and jumps to the address
in its BASIC stub (`CALL #50D`). The CPU is cycle-counted; the VIA's timer 2 paces the game exactly as
on a real Atmos; AY register writes are time-stamped in cycles and rendered sample-accurately into the
mixer. The ULA renderer handles HIRES and text lines with serial attributes and inverse video. The
CPU was verified instruction-by-instruction against the original project's py65 harness for two
million instructions.

## ENHANCED: the remake

`Session` is a line-by-line port of the original's C: the same droid tables, movement and collision
(`can_go`), combat, grapple and transfer battle, burnout, energisers and lifts, the same xorshift
random numbers and the same demo autopilot. The original's blocking loops (pauses, the transfer
battle, end screens) are a C# iterator, so each `yield` is one `wait_frame()`. It ticks at the
original's 25 frames per second; `DeckView` interpolates positions so the picture moves smoothly at
60/120 Hz.

`IsoRenderer` keeps the original's projection (screen x = wx − wy, y = (wx + wy)/2 − z) but draws real
geometry with a depth buffer: each tile is a lit column (slab, steps, walls) from the deck heightfield,
droids are lathed meshes, and lights, shots and explosions are additive billboards. The player is also
drawn as an x-ray silhouette wherever a wall hides it.

## Audio

One `AudioEngine` streams everything through a single `DynamicSoundEffectInstance`. It renders exactly
as much audio as game time passes, so the capture tool records the soundtrack offline,
sample-for-sample.

## Persistence

`orictron-save.json` in the app's data folder; writes go to a temp file and the previous file is kept
as `.bak`.
