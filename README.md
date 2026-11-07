# Ovomium

A mod for **Lonely Mountains: Downhill** (Steam, Windows, Linux and Steam Deck) that adds LAN multiplayer and a few quality-of-life improvements.

## What it adds

### Multiplayer

- **Ride together on a local network**: other players appear on your track as ghosts, with no server or setup. Players on different networks can connect by IP (via Tailscale, for example).
- **Races**: start a race on your track and invite everyone, then compare results at the finish.
- **Player appearance**: each ghost wears its player's outfit and rides their bike. If you don't own the matching DLC, you see a copy of your own bike instead.
- **Ghost styles**: normal, translucent or tinted with the player's color.
- **Player list**: shows who is connected, where they are and their ping.

### Camera

- **First-person and third-person views**: switch between them and the normal view while riding. Includes the [LMD-Camera](https://github.com/flo-st/LMD-Camera) mod.
- **Sharp image**: the game's depth of field is turned off in these two views and comes back in the normal view.

### Comfort

- **Quick start**: skips the logos and the "Press any key" screen.
- **Automatic keyboard layout**: the game's letter keys follow your AZERTY or QWERTZ keyboard.
- **Automatic updates**: new versions download by themselves, are checked against a signature, and install the next time you launch the game.
- **Easy install**: installers for Windows and Linux, including the Windows firewall setup.

## Controls

| Action                                 | Keyboard   | Gamepad                   |
| -------------------------------------- | ---------- | ------------------------- |
| Start a race / accept an invitation    | F1         | Hold Select, then press A |
| Quit the race                          | Hold F1    | Hold Select + B           |
| Change ghost style                     | F2         |                           |
| Show / hide the player list            | F3         |                           |
| Change view                            | V          | Right stick click         |

Keys can be changed in `UserData/Ovomium.cfg`.

## Good to know

- Everyone needs the same version of Ovomium.
- Races can force a track for all players: times and progress are not saved on a forced track.
- Ovomium works alongside the [LMD-VR](https://github.com/visual-gh/LMD-VR) mod, which is installed separately. Their default keys don't overlap.

## Install

Download the zip from the [releases page](https://github.com/Delocca/downhill-ovomium/releases), extract it and follow `LISEZ-MOI.txt` (in French).

Changes between versions are listed in [CHANGELOG.md](CHANGELOG.md).
