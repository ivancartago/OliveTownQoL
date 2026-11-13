# Olive Town QoL

A quality-of-life mod for **STORY OF SEASONS: Pioneers of Olive Town** (Steam / PC), built on BepInEx 6.

- **Clock control**: time stops automatically inside buildings, and you can switch between normal, frozen and slow (5 real seconds per game minute)
- **Universal maker**: every maker can run every recipe, so one machine replaces a farm full of them. The Lumber Maker is renamed *Universal Maker* and tinted teal
- **Fast makers**: jobs finish almost instantly and take batches of up to 99
- **Bigger storage**: boxes and fridges get 4 pages of 24 slots, stacks go up to 9,999, and one button sorts and merges everything
- **Your own music (optional)**: season songs outdoors, a town song, an evening song at night, and silence in bad weather, at home and in the mines. Festivals and cutscenes keep the game's music. Without songs, you hear the normal game music
- **Controller support** for Xbox-style pads, with a small on-screen message confirming each action

## Install

1. Download **BepInEx 6 IL2CPP win-x64**, tested with build **be.793**: <https://builds.bepinex.dev/projects/bepinex_be>
2. Extract it into the game folder, next to `STORY OF SEASONS Pioneers of Olive Town.exe`.
3. Start the game once and wait for the title screen. The first start takes a minute or two while BepInEx sets itself up. Then close the game.
4. Download `OliveTownQoL.zip` from [Releases](../../releases) and extract it into the game folder. The DLL should end up at `BepInEx\plugins\OliveTownQoL\OliveTownQoL.dll`.
5. Optional: add your own songs (see [Your own music](#your-own-music)).
6. Start the game. A message at the top of the screen says **"Olive Town mods active"**.

Settings are in `BepInEx\config\ivan.olivetown.qol.cfg`. Edit the file in Notepad, then press **F8** in-game to reload it.

## Controls

| Action | Keyboard | Xbox controller |
|---|---|---|
| Clock mode: normal → frozen → slow | F7 | L3 (press left stick), outside menus |
| Sort the open box / fridge | F5 | L3 in a box or fridge |
| Next / previous storage page | Page Down / Page Up | RT / LT in a box or fridge |
| Universal maker mode | F6 | RT / LT in a maker |
| Change song (Spring, Summer, Fall, Winter, Town, Evening, automatic) | F9 | – |
| Reload settings | F8 | – |

PlayStation and Switch Pro controllers work when Steam Input is enabled for the game, because Steam presents them as an Xbox controller.

## Features in detail

### Clock
- The clock freezes inside any building. Mines are optional (`FreezeTimeInMines`).
- Outdoors, the clock mode you choose stays until you change it. Indoors, the mode you choose applies to that building only.
- Slow mode runs at 5 real seconds per game minute by default (`SlowModeSecondsPerMinute`).

### Universal maker
- Every normal-size maker runs all normal recipes, and every giant maker runs all giant recipes. Beehives and mushroom logs work as before.
- Some ingredients have several uses (milk → cheese / butter / yogurt, fruit → jam / seedlings / essence...). The **maker mode** decides which product you get: Auto, Jam, Seeds, Seedlings, Flour, Oil/Sauce, Essence, Dye, Cheese, Butter, Yogurt.
- In Auto mode, a maker keeps its original job for its own ingredients.
- The Lumber Maker and Giant Lumber Maker are renamed and tinted teal. You can change this under `[UniversalMaker]`.

### Storage
- Every storage box and fridge has 4 pages of 24 slots. The panel stays its normal size on screen.
- Stacks hold up to 9,999 items. This also applies to your bag.
- **Before removing the mod**, move items off pages 2–4. Otherwise those items could be lost.

### Your own music
The game has Unity's audio turned off, so the mod plays your songs through Windows directly.

| Where | Day | Night* | Rain / storm / snow |
|---|---|---|---|
| Farm and outdoor areas | season song | Evening | silence |
| Olive Town | Town | Evening | silence |
| Your house, the mines | silence | silence | silence |
| Festivals, cutscenes, shops, café, museum, DLC areas | game music | game music | game music |

\*Night starts when the game's own night music would.

No music is included, and the mod works fine without it. If a song file is missing, that spot plays the game's normal music. With no songs at all, the music sounds exactly like the unmodded game, including on rainy days, at home and in the mines. You can also add only some songs, for example just `Town.wav`.

To use your own songs, put **16-bit 44.1 kHz stereo WAV** files in `BepInEx\plugins\OliveTownQoL\Music\` named `Spring.wav`, `Summer.wav`, `Fall.wav`, `Winter.wav`, `Town.wav` and `Evening.wav`. You can also use other names and set them under `[MyMusic]`.

The songs I use come from the *Harvest Moon: Back to Nature* (PS1) soundtrack: <https://downloads.khinsider.com/game-soundtracks/album/harvest-moon-back-to-nature-ps1-gamerip-1999>
(Spring, Summer, Fall, Winter, Town and Evening.)

To convert a song with ffmpeg:

```
ffmpeg -i song.flac -ar 44100 -ac 2 -c:a pcm_s16le Spring.wav
```

Set `[MyMusic] Enabled = false` to keep the game's music everywhere.

## Compatibility
- Windows 10 and 11. Built for the Steam version (Unity 2019.4.6, IL2CPP). Other PC editions are untested.
- Works alongside [Otakubuns' Portrait Mod](https://github.com/Otakubuns/SoSPoOT-Mods), which adds character portraits to dialogue. Get it from that repo's releases.
- Don't use it together with other mods that change time speed or maker limits; they patch the same things.

## Build from source

You need the .NET 6 SDK or newer, and the game with BepInEx already started once (so `BepInEx\interop` exists).

```
cd src
dotnet build -c Release -p:GamePath="C:\path\to\STORY OF SEASONS Pioneers of Olive Town"
```

The output is `src\bin\Release\net6.0\OliveTownQoL.dll`.

## Uninstall
Delete `BepInEx\plugins\OliveTownQoL`. To turn off all mods at once, delete `winhttp.dll` from the game folder.

## Credits
- [BepInEx](https://github.com/BepInEx/BepInEx), [HarmonyX](https://github.com/BepInEx/HarmonyX) and [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop)
- STORY OF SEASONS: Pioneers of Olive Town © Marvelous Inc. This is an unofficial fan mod, and no game files are included.
