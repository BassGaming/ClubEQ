# ClubEQ

Listen to any music as if it played in the CATCAR garage: a boombox, four PA cabinets in the corners, two crew cars
with their radios, the garage's reverb, and the **Underground** muffle (the bass through a concrete wall at a rave).
You stand in the room and can walk around, sit in a car, move speakers and change everything.

## Setup
1. FMOD Engine (Windows) from fmod.com. Copy `api\core\lib\x64\fmod.dll` and `api\core\inc\fmod.cs`, `fmod_dsp.cs`,
   `fmod_errors.cs` into `fmod\`.
2. `dotnet build`, run `bin\Debug\net10.0-windows\win-x64\ClubEQ.exe`.
3. Route the music: let the browser/player play into a virtual device (here: Voicemeeter's "vm5 Browser" strip), pick
   it as **Input** (it shows as `[loopback]`), pick your headphones as **Output**, press **Start**. Turn off the
   virtual device's direct path to your headphones, or you hear the dry music on top.

## Use
- **Macros** (the strip under the top bar): Underground on/off and how deep. ✎ shows what a macro changes; edit it or
  add your own (+ Macro).
- **Room**: click a speaker to edit it on the right (volume, distance, 3D amount, effects, inside zone). Drag to move,
  mouse wheel to turn (Shift = finer). Drag **YOU** to walk around; drag yourself into a car's dashed circle to sit in it.
- Click empty floor for you, the room reverb, the room size and the preset (save as, rename, export, import, reset).
- Everything saves by itself to `%APPDATA%\ClubEQ`.
- The output meter turns red when the limiter catches peaks: lower the master.
