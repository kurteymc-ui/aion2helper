# A2HMeter

A damage and healing meter for **AION 2**, made for [aion2helper.com](https://aion2helper.com) and its Discord.

- Live DPS, healing and damage taken for your whole group, with a click-through overlay
- Skill breakdown, rotation and buff uptimes for every player
- Upload a boss fight to aion2helper.com (only when you choose to), public or private, and share it in Discord
- Event timetable, field boss timers, pet and gathering map, daily checklist

## How it works

It reads the game's own network traffic on your PC through the [Npcap](https://npcap.com) driver, passively: it never sends a
packet to the game, never reads or changes the game's memory or files, and needs no administrator rights. Nothing about your play
leaves your PC unless you upload it.

NCSOFT has not approved this or any other damage meter. Use it at your own responsibility and check the game's rules.

## Install

1. Install [Npcap](https://npcap.com) (keep its default options).
2. Download the installer from the [latest release](../../releases/latest) and run it.

Updates install themselves from this repository's full releases.

## Credits and license

Based on [Aion DPS – Daevas Power Suite](https://github.com/SkeeveAN/Aion-DPS-Meter) by SkeeveTV, MIT License.
See [LICENSE](LICENSE) and [NOTICE.md](NOTICE.md).
