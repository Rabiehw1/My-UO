[![Build](https://github.com/Rabiehw1/My-UO/actions/workflows/build-test.yml/badge.svg?branch=main)](https://github.com/Rabiehw1/My-UO/actions/workflows/build-test.yml)

# LordBritishKW Client

**LordBritishKW Client** is an Ultima Online client built on [TazUO](https://github.com/PlayTazUO/TazUO), which is itself
built on [ClassicUO](https://github.com/ClassicUO/ClassicUO). It is developed independently of both projects.

## Features

The client includes everything TazUO offers. ***Most*** features can be turned off if you don't want them.

- **Grid containers** - find and move items with fully customizable grid containers
- **Built-in scripting** - Python and Legion Script
- **Assistant features built in** - auto buy, auto sell, auto loot and more
- **Journal** - an improved journal that is easier to read and organize
- **Alternative paperdoll**, **improved buff bar** and **cooldown bars**
- **Client commands** - many commands for various features
- **Controller support** - play with a gamepad
- **Grid highlighting** - highlight items that have specific properties
- **Tooltip overrides** - customize and override any tooltip text
- **Custom fonts** - bring your own TTF fonts for better readability

Detailed guides for these features are on the [TazUO wiki](https://tazuo.org/wiki/home/). The in-game help links
also point there.

## Building

Requirements: the [.NET 10 SDK](https://dotnet.microsoft.com/download) and Git.

```bash
git clone --recursive https://github.com/Rabiehw1/My-UO.git
cd My-UO
dotnet build -c Release
```

The client is built into `bin/Release/` as `LordBritishKW`. Point it at your Ultima Online data folder on first launch.

## Reporting problems

Open an issue on the [issues page](https://github.com/Rabiehw1/My-UO/issues). If the client crashes, it writes a
crash report you can attach.

## Credits and license

Thanks to the TazUO developers and supporters, and to the ClassicUO developers, whose work this client is built on.

Released under the BSD 2-Clause license; see [LICENSE.md](LICENSE.md).
