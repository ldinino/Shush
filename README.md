# Shush

A tiny Windows tray app to attenuate system audio below Windows' own volume floor.

Windows' volume mixer bottoms out well above true silence, so "1%" can still be
too loud late at night or on sensitive headphones/speakers. Shush sits in the
tray and applies extra, finer-grained attenuation (down to -60 dB) on top of
whatever the Windows volume slider is already doing — without touching the
master/system volume.

## Features

- **Attenuation slider (0 to -60 dB)** — finer and quieter than the Windows UI allows.
- **Per-app session gain, not master volume** — continuously applies gain to every
  audio session on the target device instead of changing the system volume.
- **Mute** — instantly forces silence regardless of the slider position.
- **Output device picker** — target a specific playback device, or follow whatever
  Windows considers the default.
- **Launch at startup** — optionally starts minimized to the tray on sign-in
  (per-user, no admin rights required).
- **Tray-first UX** — closing the window hides it to the tray; a single instance
  is enforced, and re-launching just re-shows the existing window.
- Settings persist to `%LocalAppData%\Shush\settings.json`.

## Requirements

- Windows 10/11 (x64)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) installed
  (the release build is framework-dependent, not self-contained)

## Download

Grab the latest zip from the [Releases](../../releases) page, extract it anywhere,
and run `Shush.exe`.

## Build from source

```powershell
git clone https://github.com/ldinino/Shush.git
cd Shush
dotnet build Shush.slnx -c Release
```

To produce a standalone publish folder (what the releases are built from):

```powershell
dotnet publish src\Shush\Shush.csproj -c Release -o publish\Shush
```

## Running tests

```powershell
dotnet test Shush.slnx
```

## How it works

`Shush.Core` polls the active [WASAPI](https://learn.microsoft.com/windows/win32/coreaudio/wasapi)
render device (via [NAudio](https://github.com/naudio/NAudio)) and drives
`SimpleAudioVolume` on every audio session, converting the slider's dB value to
linear gain (`GainMath`). This attenuates what you hear without ever touching
the Windows master volume or per-app volume mixer settings, so everything
reverts to normal the moment Shush exits.

## Project layout

```
src/Shush         WPF UI (MVVM via CommunityToolkit.Mvvm), tray icon, app shell
src/Shush.Core    Audio attenuation, settings persistence, startup registration
tests/Shush.Tests xUnit tests for the core logic
```

## License

[MIT](LICENSE)
