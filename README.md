<p align="center"><img src="Assets/ClubEQ.png" width="128" alt="ClubEQ icon"></p>

<h1 align="center">ClubEQ</h1>

<p align="center"><b>Listen to any music as if it played in the CATCAR Pit Crew garage.</b></p>

ClubEQ takes the sound from one audio device (your browser, Spotify, anything) and plays it through a virtual room
of speakers: a boombox, four PA cabinets in the corners, two crew cars with their radios, the garage's reverb, and the
**Underground** muffle (the bass through a concrete wall at a rave). You stand in the room, walk around, sit in a car,
move the speakers and change everything.

The garage is measured from the game itself (CATCAR Pit Crew with the Radio mod): speaker positions, distances,
filters, the reverb zone and the loudness all match what you hear in the game.

## Features
- **A room you can walk in**: top-down view, drag yourself around, turn with the mouse wheel, sit in a car.
- **3D speakers**: each with volume, distance roll-off, spread, 3D amount and its own effect chain (high/low-pass,
  reverb, distortion, EQ, echo, gain).
- **Macros**: one switch and slider that move many settings at once, e.g. *Underground*. Per speaker you can follow
  the macro, force it on or off.
- **Room reverb**: the garage's own reverb, or any of FMOD's 24 presets.
- **Master section**: 10-band EQ, master gain up to +24 dB, optional limiter, L/R peak meters with clip indicator.
- **Presets**: save, save as, rename, export/import as JSON, reset built-ins. Changes save by themselves.
- **Import from CATCAR Radio**: copies your Radio mod's current setup (volumes, Underground, speaker positions).
- **Voicemeeter insert**: ClubEQ works right inside a Voicemeeter strip: ~4 ms delay, no dry sound on top.
- **Latency settings**: mix block, buffers and gap, with Safe / Low / Lowest presets.

## Download
Grab `ClubEQ-<version>-win-x64.zip` from [Releases](../../releases), unzip it anywhere and run `ClubEQ.exe`.
Windows 10/11, 64-bit. Nothing else to install.

## Setup: route your music through ClubEQ
**With Voicemeeter (recommended)**: ClubEQ replaces one Voicemeeter strip's sound with its mix, inside Voicemeeter.
The delay is about one Voicemeeter buffer and you never hear the dry music on top.

1. Windows sound settings (Windows 10: *Advanced sound options*, Windows 11: *Volume mixer*): set your browser's
   output to a Voicemeeter input, e.g. *Voicemeeter In 5*.
2. In Voicemeeter that strip's meter now moves; its **A1** (your headphones) must be on. *Menu > System Settings*:
   48000 Hz; a smaller buffering (192 or 256) means less delay.
3. In ClubEQ: **Settings** > Path *Voicemeeter insert*, pick the strip, Latency *Lowest*, Apply, **Start**.

The **Help** button in ClubEQ shows these steps.

**Without Voicemeeter**: ClubEQ records one device and plays the result to another, so the music has to go into a
device you don't listen to directly:

1. Let your browser or player play into a **virtual audio device** (e.g. a [VB-Cable](https://vb-audio.com/Cable/)
   or a Voicemeeter strip).
2. In ClubEQ pick that device as **Input**. Playback devices show up as `<name> [loopback]`.
3. Pick your headphones or speakers as **Output** and press **Start**.
4. Make sure the virtual device doesn't also go straight to your headphones, or you hear the dry music on top.

Input and output must not be the same device (ClubEQ would record itself; it refuses to start).

## Use
- **Macro strip** (under the top bar): Underground on/off and how deep. ✎ shows what a macro changes; edit it or add
  your own (+ Macro).
- **Room**: click a speaker to edit it on the right. Drag to move, mouse wheel to turn (Shift = finer). Right-click a
  speaker to toggle its own Underground. Drag **YOU** to walk; drag yourself into a car's dashed circle to sit in it.
- **Empty floor**: you, the room reverb, the room size and the preset buttons.
- **Master**: 0 dB plays as loud as the game. The meters read before the limiter; the chip shows **LIMIT** when the
  limiter works and **CLIP** when the limiter is off and a peak went over 0 dBFS (click it to reset). If it lights up
  a lot, turn up Windows or your mixer instead of the master.
- Settings and presets live in `%APPDATA%\ClubEQ`.

## Build from source
1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download) and the
   [FMOD Engine](https://www.fmod.com/download) 2.04 for Windows.
2. Copy into a `fmod\` folder next to `ClubEQ.csproj`:
   `api\core\lib\x64\fmod.dll` (the x64 one) and `api\core\inc\fmod.cs`, `fmod_dsp.cs`, `fmod_errors.cs`.
   FMOD isn't in this repository because of its licence.
3. `dotnet build`, then run `bin\Debug\net10.0-windows\win-x64\ClubEQ.exe`.

Command-line tools: `--selftest` (model checks, no audio), `--devices` (what FMOD sees, Voicemeeter's strips),
`--smoke "<input>"` (plays a quiet test tone through every effect), `--smoke clocked` (the Voicemeeter insert's
engine, no Voicemeeter needed), `--vm-latency [block gapMs]` (times clicks from the strip to bus A1 inside Voicemeeter).

Release build:
```powershell
dotnet publish -c Release --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o publish
```

## Credits
- Audio engine: **FMOD Core** by Firelight Technologies Pty Ltd. *Made with FMOD Studio by Firelight Technologies
  Pty Ltd.* FMOD is © Firelight Technologies and used under its free non-commercial/indie licence; it is not covered by
  this project.
- The garage, its sound and the Radio mod come from **CATCAR Pit Crew**. ClubEQ is a fan project and not affiliated
  with the game's developers.
