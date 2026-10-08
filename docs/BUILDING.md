# Building

Prerequisites (macOS host): .NET 10 SDK with the `android` and `ios` workloads, Xcode 27, Android SDK,
Python 3 with Pillow, ffmpeg (store videos only).

```bash
dotnet run --project src/Quazitronic.Desktop                               # desktop (Mac/Windows/Linux)
dotnet test tests/Quazitronic.Tests
dotnet build src/Quazitronic.iOS -c Debug -r iossimulator-arm64 -p:CodesignKey=- -p:CodesignProvision=   # simulator
dotnet build src/Quazitronic.Android -c Debug -t:Run                       # device/emulator
python3 tools/make_icons.py                                             # regenerate icons from art/source/icon-1024.png
python3 tools/export_decks.py                                           # re-export decks (needs oricport/)
```

The master icon is rendered by the game itself:
`dotnet run -c Release --project tools/Quazitronic.Capture -- icon artifacts/capture/icon --script icon`.

iOS device builds use `Apple Development` + profile `devel-quazitronic`.
