# Architecture

```
OrictronGame (MonoGame Game: 224-high adaptive virtual screen, settings, audio, screens)
 ├─ Original/   OricScreen (HIRES-format picture), OriginalRenderer (port of the Oric version's drawing),
 │              OriginalData.g.cs (its font, sprites, deck charsets and cell maps, panel, frame, logo)
 ├─ Game/       Session (the remade game, a port of the original's main.c), Transfer, Autopilot, Droids,
 │              Deck + DeckData.g.cs (the six decks, exported from the original's generator)
 ├─ Graphics/   IsoRenderer (3D isometric deck, droids, glows), DeckView (25 Hz sim drawn interpolated),
 │              Particles, TextureFactory (procedural atlas + logo), Gfx (2D), BitmapFont (Oric font + smoothed)
 ├─ Audio/      AudioEngine (one streaming mixer), Sounds (enhanced effects), Music, ChipSound (original sound), Synth
 ├─ Input/      InputState (keys, pad, mouse, touch buttons, tilt), TiltController
 ├─ Screens/    IntroScreen, InstructionsScreen, PlayScreen (both looks), Ui
 └─ Persistence/ SaveData (settings, best five scores), SaveStore (atomic JSON + backup)
```

## ORIGINAL: the Oric look, recreated

There is no emulation and no foreign code. `tools/export_original.py` exports the Oric version's
graphics data from its generators: the 6 x 8 font, the pre-shifted droid, explosion and bullet
sprites with their masks, the two planar deck charsets and each deck's cell map, the status panel,
the framed text screen and the logo. `OriginalRenderer` is a C# port of the Oric game's drawing code
(render, pf_rect, put_sprite with set_clip, the panel fields, the text screens and the transfer
battle) writing into an `OricScreen`, a 240 x 224 picture in the Oric's HIRES byte format with serial
attributes, which is then decoded into pixels as the Oric's video chip shows them. The camera steps
6 pixels / 3 rows per game frame, as on the Oric.

`ChipSound` is the Oric game's sound code (laser on voice A, noise on B, blips on C, each fading a
step per frame) played by a three-voice square-wave and noise synthesiser with the AY chip's volume
steps and pitch formula.

## ENHANCED: the remake

Both looks share `Session`, a line-by-line port of the original's C: the same droid tables, movement and collision
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
