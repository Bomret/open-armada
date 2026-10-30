# Sound fade

The rules of how a combat sound fades with its distance from the camera, recovered from the 1.2 `Armada.exe`
(SHA-256 `c5f32464c6b4322469d79ba19d33f26fde67b5c06ca2ee24a071cd67a52a88b5`). Each rule names its address as
evidence (ADR 0004, INV-0001). A rule the code does not confirm is marked **inferred** or **stand-in**.

## The settings

0x5abbc0 reads `audio.odf` once. Three of its keys set the fade, each read as a float:

| Key | Role | Default (exe) | Stock `audio.odf` |
|---|---|---|---|
| `rolloffFactor` | How fast a sound fades beyond its minimum distance; 0 is no fade | 1 | 0.15 |
| `minimumDistance3D` | The distance within which a sound plays at full level | 1 | 50 |
| `maximumDistance3D` | The distance beyond which a sound is silent | 1e9 | 2000 |

The defaults are the values in the exe's data at 0x62c284, 0x62c28c, and 0x62c290, which the read keeps when
the file lacks a key. `distanceFactor` and `dopplerFactor` set the Doppler shift, and `minimum3DSounds` the
voice limit; neither changes a sound's level.

## A sound's distances

0x5aff40 makes every 3D sound. It takes a minimum scale and a maximum scale, and gives the sound a minimum
distance of `minimumDistance3D` times the minimum scale and a maximum distance of `maximumDistance3D` times
the maximum scale.

- **Fire sound.** 0x50b7c0 passes 1 and 1.
- **Explosion.** 0x51a490 passes 2 (the float at 0x5d2b4c) and 1, so an explosion plays at full level out to
  twice the distance of a fire sound, and goes silent at the same maximum distance.

## The listener

The listener sits at the camera eye. Each frame, 0x5abed0 hands the camera's pose to 0x5acb60, which sets
DirectSound's listener position and orientation from it.

## The fade

The game hands the distances and the rolloff to DirectSound, which computes the fade itself. Microsoft
documents no formula for it. The engine uses this one, **inferred**:

- Within the minimum distance, the sound plays at 0 dB.
- Between the minimum and the maximum distance, the gain is min / (min + rolloff × (distance − min)).
- Beyond the maximum distance, the sound is silent.

With the stock values, a fire sound 509 units from the camera plays at about −7.5 dB and an explosion at
about −4.2 dB.

## In the engine

`SoundFade` holds the three values and the gain. The map sandbox reads them from the install's `audio.odf`
when the map opens. A missing file, an unreadable file, a missing key, a value that is not a number, and an
unusable value (a rolloff below 0, a minimum distance of 0 or less, a maximum distance not above the minimum)
each take the exe's default and are reported once in the gap report.

A combat sound starts at the level the fade gives for its distance from the camera eye. Godot's own distance
fade, its maximum distance, and its attenuation filter apply nothing. The sound stays positional, so it pans
by its direction from the camera. A sound that starts beyond its maximum distance is not played.

**stand-in:** Godot's stereo panning keeps a sound's total power, so a sound straight ahead plays at −3 dB in
each channel. DirectSound plays it at full level in both (**inferred**). The engine lifts every combat sound by
3 dB to match. A sound off to one side then plays up to 3 dB louder in its near channel than the game plays it.
