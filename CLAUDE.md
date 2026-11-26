# ClubEQ

A Windows app that takes audio from one device and plays it through a virtual room of speakers: the sound of the
garage in **CATCAR Pit Crew** with the user's own Radio mod (boombox, four corner PA cabinets, two crew cars with
their radios, the "Underground" muffle, 3D positions, the garage reverb). Started inside the catcar modding repo's
session (`U:\OpencodeProjects\catcar`, the Radio mod is `catcar/Radio/`); this repo stands alone now.

## Build, test, run
- `dotnet build` (net10.0-windows, WPF, x64). Builds must have 0 warnings.
- `bin\Debug\net10.0-windows\win-x64\ClubEQ.exe --selftest`: model checks without audio (exit code 0 = passed).
- `ClubEQ.exe --devices`: what FMOD sees (inputs, outputs).
- `ClubEQ.exe --smoke "vm5 Browser"`: plays a quiet 440 Hz tone into that device for 8 s, records it through the Garage
  scene in four setups (normal, no room reverb, Underground, both) and prints input/output peaks and per-speaker
  levels. Exercises every DSP. The user hears the tone (they said that's fine).
- Settings and presets: `%APPDATA%\ClubEQ\` (`settings.json`, `Presets\<name>.json`); set `CLUBEQ_HOME` for tests so
  you don't touch the user's.
- **FMOD** is not in git (licence; `.gitignore` has `fmod/`). `fmod\` needs `fmod.dll` from `api\core\lib\x64` (the
  x86 one gives `BadImageFormatException` in `System_Create`; that happened once) and `fmod.cs`, `fmod_dsp.cs`,
  `fmod_errors.cs` from `api\core\inc`. The user installed the FMOD Engine **2.04** (`VERSION.number 0x00020400`) to
  `C:\Program Files (x86)\FMOD SoundSystem\FMOD Studio API Windows\`. FMOD is free for personal/indie use; check the
  licence before ever distributing.

## The user's audio routing
- Voicemeeter Potato + VB-Cable. The browser plays into **"vm5 Browser"**, a Voicemeeter VAIO *input strip* (a Windows
  output device). FMOD can't record it directly: it shows up as `vm5 Browser (VB-Audio Voicemeeter VAIO) [loopback]`
  (WASAPI loopback of that endpoint, 48 kHz stereo), which is the default input on first run.
- The strip also goes straight to the headphones in Voicemeeter, so the dry sound plays on top unless the user turns
  that strip's A bus off while ClubEQ runs. ClubEQ's output must not be "vm5 Browser" itself (feedback; the app warns
  when input and output names match).
- Loopback only delivers data while something plays. During silence the record head stalls, the engine notices the
  latency going negative and restarts the speakers aligned once data flows again.

## Code
- `Model/` (no FMOD in here):
  - `Preset.cs`: `Preset` (room, room reverb, speakers, macros, master), `Speaker` (kind, position, yaw, mute/solo,
    params `P`, effect `Chain`, optional `InsideProfile`), `Effect`, `Macro` + `MacroTarget`, `RoomReverb` (FMOD
    `REVERB_PROPERTIES` fields).
  - `Effects.cs`: the registry. Every effect type with its parameters (`ParamDef`: range, default, unit, log, choices),
    the speaker-level params (`Volume`, `MinDistance`, `MaxDistance`, `Spread`, `Level3D`), FMOD's 24 reverb presets
    (checked equal to `FMOD.PRESET` in fmod.cs 2.04). Parameters are addressed as `"Volume"` or `"LowPass.Cutoff"`
    (= the first effect of that type in the chain).
  - `Mix.cs`: `Targets(preset, speaker, inside)` = own values, then the macros that are on, then the inside profile.
    Pure; the engine smooths towards it. `IsInside`, `ZonePoint` (zone offset turned by the speaker's yaw).
  - `Builtin.cs`: the Garage preset (numbers below). `Store.cs`: JSON (System.Text.Json, fields, enums as strings),
    temp-file-and-swap writes.
- `Audio/`:
  - `Engine.cs`: one FMOD system. `recordStart` into a looping 1 s PCM16 sound; each speaker = a 3D channel playing that
    sound ~50 ms behind the record head, all started under one paused group so they stay sample-aligned, all given the
    same frequency. Drift: proportional nudge of the frequency (max ±0.5 %, FMOD's record example uses ±2 % steps,
    audible on music). The chain layout (`Shape`) changing restarts all channels. Per frame: listener, room reverb
    (`System.setReverbProperties(0)`), smoothing (rate 6/s, log for log params, like the mod's `Fx.Update`), 3D attrs,
    DSP params, meters. Master: a `LIMITER` (ceiling -1 dB) at the head; the output meter reads the master fader before
    it. Errors stop the engine and show in the status line.
  - `Dsp.cs`: effect type -> FMOD DSP. High/low-pass = band A of `MULTIBAND_EQ` (LOWPASS_12DB = 1, HIGHPASS_12DB = 4;
    FMOD 2.03+ dropped the old LOWPASS/HIGHPASS types), Reverb = `SFXREVERB` with a preset + wet adjust + dry,
    Distortion (bypassed at level 0, as the mod), Eq = `THREE_EQ`, Gain = `FADER`, Echo = `ECHO`. Only changed values
    go to FMOD. DSPs are added at the channel's TAIL from the last effect back, so the signal runs first effect -> last
    -> 3D panner, as on a Unity AudioSource.
- `UI/`: `Theme.xaml` = TrackGlow's theme (`U:\OpencodeProjects\trackglow-desktop\Desktop\Theme\Theme.xaml`, the
  user's other app) + `NeonBrush` #FF3DA8 and `CyanBrush` #3DE8FF. `RoomCanvas` (top-down room, drawn in `OnRender`,
  click/drag/wheel), `Inspector` (right panel built in code from the registry: speaker, macro, or room/you/reverb/
  preset when nothing is selected), `Controls.cs` (row builders; slider + typed box, typing may exceed the range,
  double-click resets), `Prompt.cs`.
- `MainWindow`: top bar (preset, input, output, master, meters, start/stop), macro strip (switch + slider per macro, ✎
  edits it), room + inspector (the inspector moves under the room below 1000 px), status line. Edits autosave the
  preset after 1 s. Starts playing on launch once an input was picked before.

## Adding things
- **An effect type**: an `EffectDef` in `Effects.All` + a case in `Dsp.Create` and `Dsp.Apply`. The UI, macros and
  inside profiles pick it up.
- **A mode** (next: "inside a car"): a preset and/or macros, no code. A Car preset = a cabin-sized room, door
  speakers, a short cabin reverb, macros like "windows down". The car speaker's inside profile is already the seed.
- Selftest lines go in `SelfTest.Run`.

## Where the numbers come from (CATCAR Radio mod 1.4.0 and the game)
| In the game (Unity) | ClubEQ |
|---|---|
| AudioSource: 3D (`spatialBlend` 1), Logarithmic roll-off, `minDistance` 7 m garage radio / 6 m corners / 4 m cars, `maxDistance` 150, `spread` 60, doppler 0 | 3D channel, `INVERSE_ROLLOFF`, same distances, `set3DSpread(60)`, doppler 0 |
| Garage radio (boombox): high-pass 90 Hz, low-pass 12 kHz, `AudioReverbFilter` Room | Boombox chain |
| Corner PA cabinets: high-pass 35 Hz, low-pass 16 kHz, Room; 0.4 m off both walls, 2.7 m up, facing the middle, tilted 15° down | Corner 1-4 |
| Underground (garage radio, portables, corners): low-pass exp-lerp 3.5 kHz -> 160 Hz by the slider, high-pass 10 Hz, reverb ParkingLot, distortion 0.12 -> 0.4, `minDistance` x2.5, volume x1.5 | the Underground macro (kinds Boombox and Cabinet) |
| Car radio: 0.9 m above the car, outside low-pass 500 Hz, volume x1.3; inside (camera within 1.5 m of the cockpit camera) low-pass 22 kHz, `spatialBlend` 0.25 | car speakers + inside profile (zone 0.35 m left, 0.2 m back of the car centre: a guess) |
| Volumes: station knob x the player's Volume 0.8 (boombox 0.8 -> 0.64, corners 0.6 -> 0.48, cars 0.8 x 1.3 -> 0.83) | speaker Volume |
| Garage walls from the middle: +x 7.4, -x 7.8, +z 9.6, -z 5.5 m (mod log `Corner speakers: walls at ...`); tile grid 10 x 7 at 1.4 m, the radio on back-row tile 9 | Garage room, boombox at (6.3, 0.75, 4.2) (approx.) |
| The garage's only `AudioReverbZone` "GarageReverb" at (-8.00, 1.40, -341.30) (garage middle (-7.81, -0.15, -345.32)), preset **User**, radii 8/12 m, grown by the mod to cover the whole garage (EvenGarageReverb) | `RoomReverb`, applied to every speaker (values below) |

## Open items
- **GarageReverb values** (read in game with a throwaway plugin, 2026-11-26): room -900, roomHF -400, roomLF 0,
  decayTime 1.2 s, decayHFRatio 0.55, reflections -500 @ 0.008 s, reverb +100 @ 0.018 s, HFReference 5000, LFReference
  250, diffusion 100, density 100. Converted by hand (Unity's own I3DL2 -> FMOD mapping isn't public): decay 1200 ms,
  early/late delay 8/18 ms, HF decay 55 %, low shelf 250 Hz 0 dB, high cut 4066 Hz (roomHF as a 1-pole low-pass at the
  HF reference: g = 10^(roomHF/2000), cut = HFRef * g / sqrt(1-g²)), early/late mix 66.6 % (reflections -5 dB vs reverb
  +1 dB), wet -8 dB (room -9 dB + the louder +1 dB). Still to tune by ear against the game (same song, standing in the
  middle).
- Not heard by the user yet: the whole sound, Underground sweep, the inside-the-car switch, the room view and UI.
- Car parking spots in the garage are guessed (x ±2.6, z 1.5); the boombox spot is approximate.
- Unity's spread/panning and FMOD's are both FMOD under the hood, but Unity's AudioSource may add its own 3D level
  handling; compare by ear.
