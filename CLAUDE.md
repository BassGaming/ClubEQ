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
- **Panning is measured correct** (`ClubEQ.exe --pan "vm5 Browser"`: one dry speaker 5 m right/left/front/back, prints
  L/R peaks): right = R only, left = L only, front = back = centre (plain stereo can't tell them apart), turning works.
  The game also renders stereo: Windows' default device is Voicemeeter Def (2 ch) and CATCAR's per-app routing
  (registry `PolicyConfig\PropertyStore`) points at Voicemeeter VAIO out7/out8, not In 3. **Voicemeeter In 3** is the
  only 7.1 device and the only one with Equalizer APO + **HeSuVi** (headphone surround, binaural front/back).
- **Time offsets** (speaker param `Delay`, ms, 0..60 on the slider): each channel starts that much behind. The mod's
  corner/portable speakers are mirrors that copy the garage radio's `timeSamples` on the main thread while audio runs,
  so each sits 0..~20 ms off (re-synced only past 50 ms); the user heard that in game as "speakers not 100 % in sync".
  Sample-exact speakers fuse into one flat phantom source, offsets let the ear pick each one out. Defaults
  (`Builtin.MirrorOffset`): corners 6/14/3/18 ms, cars 9/12, portables by id, garage radio 0. The import fills them in
  for speakers that have none. Changing one restarts all channels. Drift control measures on voice 0 and allows for its
  offset (`lag0`).
- **Audio probe (2026-11-26, throwaway plugin, 5 dumps in the garage at several spots; removed again):**
  - Unity: 48 kHz, **Stereo**, DSP buffer 1024, no spatializer plugin, AudioListener volume 1, no filters on the
    listener (camera `PlayerCamera/Main Camera`, ears **1.0 m** above the floor: the player is a cat).
  - Mixer `MainAudioMixer`: groups Master (-5.9 dB), FX (-14.4 dB, the radios), Engines, Engines Exterior, Music
    (-13.7 dB), Voice (-6 dB). Volumes only as far as the runtime shows.
  - Radio sources: exactly as ClubEQ has them (blend 1, spread 60, Logarithmic, doppler 0, reverbZoneMix 1, no
    bypass, group FX; volume = knob x player Volume 0.25 x 1.5 Underground / x 1.3 car). Custom curves can't be read
    (`GetCustomCurve` stripped).
  - **Unity's reverbs are I3DL2** (`AudioReverbFilter` Room: room -1000, roomHF -454, decay 0.4, HF 0.83, reflections
    -1646, reverb 53 @ 3 ms; ParkingLot: -1000, 0, 1.65, 1.5, -1363, -1153 @ 12 ms). FMOD's preset table is these
    converted, which pinned the conversion (`RoomReverb.FromI3DL2`, selftest checks Room and Generic):
    high cut = HFRef / sqrt((1-g)/g), g = 10^(roomHF/2000); mix = late/(early+late); wet = room/100 + 10 log10(e²+l²).
    The old hand conversion made the garage reverb too dull (high cut 4066 -> **6538 Hz**) and 1 dB too dry (-8 ->
    **-7.03 dB**).
  - Cars parked at CarRadio (-5.81, 1.39, -338.44) and (-9.21, 1.39, -338.44) (= (2.0, 1.54, 6.88) and (-1.4, 1.54,
    6.88) from the middle), facing -z, cockpit camera right under the radio. Garage radio source at y 0.37 world.
  - timeSamples per dump: speakers differ from the garage radio by 0 or ±1024 samples (one DSP block, 21 ms); the
    sign follows the read order, so the real offsets are inside one block (0..21 ms), as ClubEQ's defaults.
  - The import now also sets the garage reverb, the cars' spots and the ear height, and decides "same music" by
    playlist roots (the user's garage radio follows Car 1: link 1 on station 0).
- **Measuring** (`Measure.cs`, 2026-11-26): exponential sine sweeps, left input only then right only (6 s each,
  20 Hz-20 kHz, -10 dBFS). `--serve [8799]` serves `%APPDATA%\ClubEQ\Measure\sweep.wav` for the Radio mod (add
  `http://127.0.0.1:8799/sweep.wav` as a station; ffmpeg plays it, it loops when it ends; not 8765, the Data API mod uses that); `--record "Voicemeeter Def"
  <s> <out.wav>` records the game's output (loopback); `--measure-clubeq <out.wav> <x> <z> <yaw> [preset] [y]` plays
  the sweep into vm5 Browser through ClubEQ's engine and records Voicemeeter Def the same way; `--analyze a.wav [b.wav]`
  deconvolves into the four impulse responses (L/R in -> L/R out, written as `<a>.ir.wav`), prints third-octave EQ per
  ear (each relative to its own 250 Hz-8 kHz average), EDT/T20, crosstalk and the arrivals in the first 80 ms, and with
  two files the differences. Goal: a blind test can't tell CATCAR from ClubEQ, so tune until these match.
- High/low-pass now use FMOD's classic `LOWPASS`/`HIGHPASS` (what Unity's filters are); the multiband EQ only as a
  fallback if the DLL refuses them.
- User A/B (same song, same spot): CATCAR sounds like a larger garage, with better EQ and clarity. The game's mixer
  is "MainAudioMixer" (sharedassets0) with groups Master, Engines, Engines Exterior, Music, Voice (plus the FX group
  the mod routes to, not found in that file); what effects sit on them can't be read from the strings. Next step if
  needed: measure the game (sweep through the Radio mod, loopback-record Voicemeeter Def).
- ClubEQ refuses to start when Output is the device it records (`X [loopback]` in, `X` out): it would record itself.
- The user's `settings.json` never got rewritten after the first launch (presets did). Saving errors now show in the
  status line instead of being swallowed; the cause isn't known yet.
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
  - Per-speaker macros: `Speaker.Macros[name]` (`SpeakerMacro`: `Own`, `On`, `Amount`); `Speaker.Use(macro)` gives the
    speaker's own on/amount when `Own`, else the strip's. The mod has an Underground toggle + slider per speaker; the user
    needs that. Inspector: "Follow strip / On / Off" + amount per macro that targets the speaker; room view: right-click
    a speaker toggles its own switch for the first macro, active macros show under its name. Renaming a macro moves the
    speakers' entries.
  - `Mix.cs`: `Targets(preset, speaker, inside)` = own values, then the macros that are on, then the inside profile.
    Pure; the engine smooths towards it. `IsInside`, `ZonePoint` (zone offset turned by the speaker's yaw).
  - `RadioImport.cs`: "Import from CATCAR Radio" (button in the room/preset view). Reads the mod's
    `BepInEx\config\catcar.radio.state.txt` (S lines: station, vol, muffle, amt, link; P line: the mod's P message with
    corners id -1..-4 and portables, positions in world space) and `LogOutput.log` (`Radio built on ... at (x, y, z)` =
    the garage radio, `Corner speakers: ... from the middle (x, y, z)` = the garage origin, else (-7.81, -0.15,
    -345.32)). Sets volumes and per-speaker Underground, corner and garage radio positions, adds garage portables (on the
    garage playlist), mutes a car unless it is synced to the garage (link 0), turns the strip's Underground off. The
    player's Radio `Volume` (0.25 here) is not copied. The BepInEx folder comes from Steam's libraryfolders.vdf or a
    folder picker, saved as `Settings.CatcarBepInEx`.
  - `Builtin.cs`: the Garage preset (numbers below), with the user's own mod setup of 2026-11-26 baked in: garage
    radio 0.585 with Underground 0.389, corners 0.409/0.4375/0.447/0.466 with Underground on corners 2 (0.557) and 4
    (0.504), Car 1 synced (0.7 x 1.3), Car 2 muted. `Builtin.Corner` / `Builtin.Portable` build mod-style speakers. `Store.cs`: JSON (System.Text.Json, fields, enums as strings),
    temp-file-and-swap writes.
- `Audio/`:
  - `Engine.cs`: one FMOD system. `recordStart` into a looping 1 s PCM16 sound; each speaker = a 3D channel playing that
    sound ~50 ms behind the record head, all started under one paused group so they stay sample-aligned, all given the
    same frequency. Drift: proportional nudge of the frequency (max ±0.5 %, FMOD's record example uses ±2 % steps,
    audible on music). The chain layout (`Shape`) changing restarts all channels. Per frame: listener, room reverb
    (`System.setReverbProperties(0)`), smoothing (rate 6/s, log for log params, like the mod's `Fx.Update`), 3D attrs,
    DSP params, meters. Master: volume x0.25 (`Headroom`, -12 dB: master 100 % ≈ input level with the Garage
    scene), then a `LIMITER` (ceiling -1 dB, release 80 ms) at the head; the output meter reads the master fader before
    it. The mix is forced to **stereo** (`setSoftwareFormat`), as the game; "Voicemeeter In 3" is a 7.1 device and FMOD
    would otherwise pan into rear channels the headphones never get. Errors stop the engine and show in the status line.
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
| Garage walls from the middle: +x 7.4, -x 7.8, +z 9.6, -z 5.5 m (mod log `Corner speakers: walls at ...`); garage radio fixture at (-1.51, -0.15, -349.52) (log `Radio built on ...`) = (6.3, -4.2) from the middle (0.1.0 had z +4.2, mirrored) | Garage room, boombox at (6.3, 0.75, -4.2) |
| The garage's only `AudioReverbZone` "GarageReverb" at (-8.00, 1.40, -341.30) (garage middle (-7.81, -0.15, -345.32)), preset **User**, radii 8/12 m, grown by the mod to cover the whole garage (EvenGarageReverb) | `RoomReverb`, applied to every speaker (values below) |

## Open items
- **GarageReverb values** (read in game with a throwaway plugin, 2026-11-26): room -900, roomHF -400, roomLF 0,
  decayTime 1.2 s, decayHFRatio 0.55, reflections -500 @ 0.008 s, reverb +100 @ 0.018 s, HFReference 5000, LFReference
  250, diffusion 100, density 100. Converted by hand (Unity's own I3DL2 -> FMOD mapping isn't public): decay 1200 ms,
  early/late delay 8/18 ms, HF decay 55 %, low shelf 250 Hz 0 dB, high cut 4066 Hz (roomHF as a 1-pole low-pass at the
  HF reference: g = 10^(roomHF/2000), cut = HFRef * g / sqrt(1-g²)), early/late mix 66.6 % (reflections -5 dB vs reverb
  +1 dB), wet -8 dB (room -9 dB + the louder +1 dB). Still to tune by ear against the game (same song, standing in the
  middle).
- **First listen (0.1.0), user:** "3D way less prominent and worse than in catcar, overall pretty different". Their saved
  preset had all corners at volume 1.0 and master 0.86: the seven speakers summed to ~4x full scale, so the limiter
  squashed everything all the time (flat dynamics, no sense of direction), plus Car 1's 500 Hz bass close by. Fixed
  with the -12 dB headroom (and stereo mix, just in case). Not re-heard yet; if it's still flat, next suspects: the
  per-speaker Room reverb + room reverb washing out the panning (compare with reverbs off), Unity maybe placing the
  AudioSource filters after its panner instead of before, the listener standing still (in game you move and turn).
- Car parking spots in the garage are guessed (x ±2.6, z 1.5). Corner and radio spots are the game's.
- **Still flatter than CATCAR** (user, after the headroom fix, before the import). The import brings the user's real
  mix (Underground on the radio and two corners, which ClubEQ's defaults didn't have). If it still differs, the way to
  copy the game exactly is to measure it: play a sine sweep through the Radio mod (it plays any http URL as a station,
  so serve the sweep from a local HTTP server), loopback-record the game's output device standing at one spot (left
  only, then right only), deconvolve into a 2x2 impulse response, and add a convolution mode. That captures everything
  linear (Unity's panning, filters, both reverbs, distances) exactly for that spot and settings; the Underground
  distortion is the only non-linear part.
- Unity's spread/panning and FMOD's are both FMOD under the hood, but Unity's AudioSource may add its own 3D level
  handling; compare by ear.
