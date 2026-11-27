# ClubEQ

A Windows app that takes audio from one device and plays it through a virtual room of speakers: the sound of the
garage in **CATCAR Pit Crew** with the user's own Radio mod (boombox, four corner PA cabinets, two crew cars with
their radios, the "Underground" muffle, 3D positions, the garage reverb). Started inside the catcar modding repo's
session (`U:\OpencodeProjects\catcar`, the Radio mod is `catcar/Radio/`); this repo stands alone now.

## Build, test, run
- `dotnet build` (net10.0-windows, WPF, x64). Builds must have 0 warnings.
- `bin\Debug\net10.0-windows\win-x64\ClubEQ.exe --selftest`: model checks without audio (exit code 0 = passed).
- `ClubEQ.exe --devices`: what FMOD sees (inputs, outputs) and Voicemeeter's input strips.
- `ClubEQ.exe --smoke "vm5 Browser"`: plays a quiet 440 Hz tone into that device for 8 s, records it through the Garage
  scene in four setups (normal, no room reverb, Underground, both) and prints input/output peaks and per-speaker
  levels. Exercises every DSP. The user hears the tone (they said that's fine). `--smoke "vm5 Browser" 256 3 25` = with
  that mix block, buffer count and gap. `--smoke clocked` = the Voicemeeter insert's engine side without Voicemeeter
  (must come out at 0.037 like the normal path, no start burst); `--smoke "voicemeeter:vm5 Browser"` = the real
  insert for 6 s. `--vm-latency [block gapMs]` times clicks played into vm5 Browser from the strip's input to bus A1
  inside Voicemeeter (registers IN|OUT, `Voicemeeter.Probe`): exact, on Voicemeeter's clock. Windows can't record A1
  (the headphones device fails to open; "Voicemeeter Out A1".. record silence).
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
- **Time offsets** (speaker param `Delay`, ms): each channel starts that much behind. **Default 0**: in every normal
  game recording the speakers arrive in sync (one arrival), and offsets of a few ms made ClubEQ's EQ up to 7 dB wrong
  (comb filtering). Only with all reverbs off did the game show a spread of a few ms. Changing one restarts all
  channels; drift control measures on voice 0 and allows for its offset (`lag0`).
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
  - The import now also sets the garage reverb, the cars' spots and the ear height, and decides "same music" by
    playlist roots (the user's garage radio follows Car 1: link 1 on station 0).
- **Measuring** (`Measure.cs`): exponential sine sweeps, left input only then right only (6 s each, 20 Hz-20 kHz,
  -10 dBFS). Commands:
  - `--serve [8799]` serves `%APPDATA%\ClubEQ\Measure\sweep.wav` (add `http://127.0.0.1:8799/sweep.wav` as a
    station in the Radio mod; it loops). Not 8765: the Data API mod uses that.
  - `--record "Voicemeeter Def" <s> <out.wav>` records the game's output (WASAPI loopback). Close ClubEQ and silence
    the browser first: anything else on that device gets in.
  - `--measure-clubeq <out.wav> <x> <z> <yaw> [preset] [y]` plays the sweep into vm5 Browser and records ClubEQ's
    **own final mix inside the engine** (a capture DSP at the master head), so no other sound or device gets in.
    Recording a device's loopback instead was unreliable (Line 1 drops samples; Voicemeeter Def picks up the game).
  - `--analyze a.wav [b.wav]` deconvolves into the four impulse responses (L/R in -> L/R out, written as
    `<a>.ir.wav`), prints third-octave EQ per ear (each relative to its own 250 Hz-8 kHz average), EDT/T20,
    crosstalk, the arrivals in the first 80 ms, and with two files the differences.
  - Build the tools with `dotnet build -o <scratch folder>` while ClubEQ runs (the exe in bin is locked then), and
    never run a command an older exe doesn't know: it opens the GUI instead.
- **Sandboxed shell vs the user's AppData**: files the shell writes under `%APPDATA%` (e.g. `ClubEQ\Measure`) are not
  visible to the game, and the `settings.json` the shell reads may be the sandbox's copy, not the one the user's
  ClubEQ writes. Put files the game must read into the game folder (the probe reads `BepInEx\plugins\clubeq-sweep.wav`).
- **The game measured (2026-11-26, a throwaway probe plugin `AudioProbe.dll` with F8 dump / F9 states / L zone test,
  removed from the game afterwards; source only in the session scratchpad):**
  - F9 states at one spot: all on EDT 0.63 s / T20 0.82 s; reverb **zone off** 0.09 / 0.31; the speakers'
    `AudioReverbFilter`s off 0.63 / 0.81 (inaudible next to the zone); both off 0.07 / 0.04. The zone is the sound.
  - L (zone alone): a plain 2D source at volume 1 without filters, zone on vs off. Same decay shape as FMOD's system
    reverb with the converted values, 7 dB louder.
  - **How Unity feeds the zone**: an AudioSource's filters sit **after the fader** (volume, distance, panning), and
    the reverb send is taken at the fader. The room hears each speaker **unfiltered**, the Underground low-pass and
    distortion only shape the direct sound: that is why the game sounds clearer and like a big garage. With the
    filters after the fader, the system reverb and its wet level `FromI3DL2(...) - 1.8 dB` (`Builtin.UnityZoneGain`;
    first fitted as +10.2 while the master was a group volume, see Engine), and the master as our own gain DSP,
    ClubEQ matched the game at two spots: level within 0.1 dB, EDT 0.61 s vs 0.63, T20 0.77-0.78 vs 0.82, crosstalk
    within 0.1 dB, third-octave EQ per ear within 1.7 dB (ClubEQ ~1 dB more at 5-10 kHz). Before: EDT 0.07 s.
  - Dead ends, removed again: a reverb bus fed by SEND DSPs (its early/late balance differs from the system reverb);
    a second silent channel per speaker as a reverb feed (FMOD scales a channel's send by its parent groups' volume, so
    a silent group silences the send too); fitting FMOD's reverb offline in NRT mode to the zone (didn't converge).
- ClubEQ refuses to start when Output is the device it records (`X [loopback]` in, `X` out): it would record itself.
- The user's `settings.json` never got rewritten after the first launch (presets did). Saving errors now show in the
  status line instead of being swallowed; the cause isn't known yet.
- **Latency (Settings dialog, `UI/SettingsDialog.cs`, saved in `settings.json`)**: mix block (`BufferLength`), output
  buffers (`BufferCount`), gap between record and play head (`GapMs`), presets Safe 512x4/50 (the old fixed values),
  Low 256x3/25, Lowest 128x3/12; all three ran clean on the loopback path (2026-11-27). Apply rebuilds the engine.
- **Voicemeeter insert** (`Settings.Voicemeeter`, `Audio/Voicemeeter.cs`): Voicemeeter's Remote API
  (`VoicemeeterRemote64.dll`, `VBVMR_AudioCallbackRegister` mode IN) calls ClubEQ on its audio thread with every
  input strip; ClubEQ takes one strip's L/R, runs the Engine **clocked** (`NOSOUND_NRT`, `StartClocked`, `Write` then
  `Render` per callback, UI thread only sets parameters, no drift control, gap >= one block + 64) and writes the mix
  back. The strip then carries ClubEQ, not the dry sound, so no doubled sound, no loopback, no Output device. Verified
  with Voicemeeter's meters: ClubEQ master 0 = strip and every bus silent. The user's browser strip is **hardware
  strip 5 "Browser"** (channels 8/9; Potato = 5 hardware strips x 2 ch, then 3 virtual x 8), found by its label.
  48 kHz only. **Measured strip -> A1 (Voicemeeter buffer 192, 2026-11-27)**: passed through 4.0 ms; ClubEQ with
  block/gap 512/50 54 ms, 256/25 25, 128/12 16, 256/0 6.7, **128/0 4.0** (= Voicemeeter alone), 64/0 6.7; the Garage
  +0.4-1.7 ms. The gap only adds delay here (one clock), so the dialog's presets use gap 0 on this path and choosing
  the path sets 128/0 (Windows path: 512/4/50). The user tapped +20-47 ms at 512/50 before (tap tests are +-15 ms).
  Help button (`UI/Help.cs`, top bar and Settings) = the setup steps. FMOD's ASIO output was a dead end: the Potato insert ASIO driver crashes FMOD (0xC0000409, its 38-char
  name), the other Voicemeeter ASIO drivers show no record inputs in FMOD.
- `Stop()` clears the clocked flag, so `StartExternal(.., clock)` sets it after its own `Stop()` (setting it before
  made the UI thread mix too: play heads overran the writes, the voices restarted every few frames, 8x peaks).
- Voices fade in over 0.2 s (`FadeIn`): FMOD's effects start from their own defaults and glide to ours, so every
  start (Start, preset switch, chain edit, restart) began with ~0.1 s at up to 7x the level (0.27 vs 0.037 with the
  smoke tone), on both paths. Not a parameter-order problem (all values reach FMOD before the first mix).
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
    radio 0.533 with Underground 0.389, corners 0.409/0.4375/0.447/0.466 with Underground on corners 2 (0.557) and 4
    (0.504), Car 1 synced (0.7 x 1.3), Car 2 muted, master +8.7 dB with the limiter off, you at (0.6, 3.45) facing the back wall. `Builtin.Corner` / `Builtin.Portable` build mod-style speakers. `Store.cs`: JSON (System.Text.Json, fields, enums as strings),
    temp-file-and-swap writes.
- `Audio/`:
  - `Engine.cs`: one FMOD system. `recordStart` into a looping 1 s PCM16 sound; each speaker = a 3D channel playing that
    sound ~50 ms behind the record head, all started under one paused group so they stay sample-aligned, all given the
    same frequency. Drift: proportional nudge of the frequency (max ±0.5 %, FMOD's record example uses ±2 % steps,
    audible on music). The chain layout (`Shape`) changing restarts all channels. Per frame: listener, room reverb
    (`System.setReverbProperties(0)`), smoothing (rate 6/s, log for log params, like the mod's `Fx.Update`), 3D attrs,
    DSP params, meters. Master: x0.0268 (`Headroom`: master 100 % = the game's loudness, measured with the same
    sweep) in our own gain DSP, then a `LIMITER` (ceiling -1 dB, release 80 ms) at the head; the output meter reads the
    gain DSP (`Preset.Limiter` bypasses the limiter; the UI master is a dB slider -40..+24 over `Preset.Master`, and
    the top bar has L/R meters with peak hold and a latching LIMIT/CLIP chip). **Never use a group volume or a `FADER` DSP for the master**: FMOD counts those into each channel's
    audibility, which scales its reverb send, so the room got the master gain twice (at x0.25 it sat 12 dB low; at
    x0.0268 it vanished). At x0.25 ClubEQ also ran 19 dB hotter than the game and music sat ~10 dB in the limiter:
    the user heard more bass, dull highs and less 3D. The mix is forced to **stereo** (`setSoftwareFormat`), as the game; "Voicemeeter In 3" is a 7.1 device and FMOD
    would otherwise pan into rear channels the headphones never get. Errors stop the engine and show in the status line.
  - `Dsp.cs`: effect type -> FMOD DSP. High/low-pass = FMOD's classic `LOWPASS`/`HIGHPASS` (what Unity's filters
    are; band A of `MULTIBAND_EQ` only as a fallback), Reverb = `SFXREVERB` with a preset + wet adjust + dry,
    Distortion (bypassed at level 0, as the mod), Eq = `THREE_EQ`, Gain = `FADER`, Echo = `ECHO`. Only changed values
    go to FMOD. The chain is added **after the channel's fader** (at HEAD, in order), as Unity wires an AudioSource:
    fader (volume, distance, panning, reverb send) -> first effect -> last.
- `UI/`: `Theme.xaml` = TrackGlow's theme (`U:\OpencodeProjects\trackglow-desktop\Desktop\Theme\Theme.xaml`, the
  user's other app) + `NeonBrush` #FF3DA8 and `CyanBrush` #3DE8FF. `RoomCanvas` (top-down room, drawn in `OnRender`,
  click/drag/wheel), `Inspector` (right panel built in code from the registry: speaker, macro, or room/you/reverb/
  preset when nothing is selected), `Controls.cs` (row builders; slider + typed box, typing may exceed the range,
  double-click resets), `Prompt.cs`.
- `MainWindow`: top bar (preset, input, output, master, meters, EQ, Settings, start/stop), macro strip (switch + slider per macro, ✎
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
| The garage's only `AudioReverbZone` "GarageReverb" at (-8.00, 1.40, -341.30) (garage middle (-7.81, -0.15, -345.32)), preset **User**, radii 8/12 m, grown by the mod to cover the whole garage (EvenGarageReverb) | `RoomReverb` = FMOD's system reverb, `FromI3DL2` - 1.8 dB (measured), fed by every speaker before its filters |

## Extras beyond the game (2026-11-26)
- **Master EQ** (`Preset.Eq`, `MasterEq`): ten peaking bands 31 Hz-16 kHz, Q 1.4, two `MULTIBAND_EQ` DSPs first on
  the master (before the gain DSP, limiter and capture). Opened with the EQ pill; flat and on, it leaves the measured
  match unchanged.
- **Front/back cue** (`Room.RearShade`, dB, default 6; "Front/back cue" under You): a high shelf at 3.5 kHz, last on
  each speaker's direct sound, cutting by `RearShade x behind x Level3D` (`behind` = 0 beside/in front, 1 straight
  behind). Stereo panning (FMOD's and Unity's) sounds the same front and back; in the game the user turns the
  camera, which ClubEQ's static listener can't. 0 = exactly the game (measured: unchanged match).
- **Loudness**: the user matched CATCAR by ear at Master **+8.7 dB, limiter off**, Firefox and YouTube at 100 %.
- **Icon**: `tools/make-icon.py` (Pillow) draws `Assets/ClubEQ.ico`/`.png` in TrackGlow's tile style with glowing EQ
  bars; the exe (`ApplicationIcon`) and the window use it.

## Open items
- The match above holds for the user's settings (garage radio Underground 39 %, corners 2/4 Underground, Car 1) and
  their in-game FX volume (-14.4 dB): if they change the FX slider in the game, the game's direct/reverb balance may
  change. Not heard by the user yet after the fix.
- Not measured: walking around (only two spots, ~2 m apart, facing the back wall), the car interiors, other mixes.
- Unity's custom roll-off curve can't be read (`GetCustomCurve` stripped); ClubEQ uses FMOD's inverse roll-off.
