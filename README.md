# UES-UnrealEngineSharp

[![License](https://img.shields.io/github/license/ErickCHIN000/UES-UnrealEngineSharp)](LICENSE)
[![GitHub stars](https://img.shields.io/github/stars/ErickCHIN000/UES-UnrealEngineSharp)](https://github.com/ErickCHIN000/UES-UnrealEngineSharp/stargazers)

## Overview

**UES-UnrealEngineSharp** is a toolkit designed to simplify the creation of cheats and mods for Unreal Engine games using C#. This project provides tools, bindings, and utilities for modders and cheat developers who want to leverage the flexibility and productivity of the C# language when interacting with Unreal Engine internals.

> **This project was originally based on [@shalzuth/UnrealSharp](https://github.com/shalzuth/UnrealSharp).**

## Features

- Write cheats and mods for Unreal Engine games in C#.
- Easy-to-use API for interacting with Unreal Engine objects and memory.
- Example mods and cheats included.
- Hot-reload support for rapid development and testing.
- Supports a wide range of Unreal Engine versions (see [Compatibility](#compatibility)).
- Includes SDK dumper to generate SDKs for your target games.

## Getting Started

> **Note:** This section and linked documentation are still being worked on.

### Prerequisites

- Unreal Engine game (the target for mods/cheats)
- .NET SDK (latest stable recommended)
- Visual Studio or another C#-compatible IDE

### Installation

1. **Clone this repository:**
    ```bash
    git clone https://github.com/ErickCHIN000/UES-UnrealEngineSharp.git
    ```

2. **Build the toolkit or use the included release:**
    - You must either build the toolkit yourself or use the included release binaries.
    - The SDK dumper tool (included) must be used to generate SDKs for your specific Unreal Engine game before you can create your own cheats/mods.

3. **Dump SDKs:**
    - Run the SDK dumper against your target Unreal Engine game to generate the necessary SDK files.
    - These SDKs are essential for enabling game-specific modding and cheat development.

4. **Inject or load the mod/cheat:**
    - Inject the built library into the target Unreal Engine game process.
    - Use a loader or injector of your choice, or follow instructions provided in this repository.

5. **Develop your cheat or mod in C#:**
    - Use provided example code as a starting point (see [examples/](examples/)).  
      _Examples are still being worked on._
    - Reference Unreal Engine APIs and available bindings.


## Documentation

> **Note:** Documentation is still being worked on.

- [Getting Started](docs/GETTING_STARTED.md)
- [API Reference](docs/API_REFERENCE.md)
- [Cheat & Mod Examples](examples/)
- [FAQ](docs/FAQ.md)

## Compatibility

> **Note:** Compatibility information is still being worked on.

- Supported Unreal Engine versions: see [docs/COMPATIBILITY.md](docs/COMPATIBILITY.md)
- Supported platforms: Windows (primary)

## Contributing

Contributions are welcome for new features, bug fixes, and cheat/mod examples. Please read [CONTRIBUTING.md](CONTRIBUTING.md) before submitting pull requests.

## Disclaimer

This project is for educational and research purposes only. Usage of this toolkit in violation of applicable laws or game terms of service is strictly discouraged. The authors are not responsible for any misuse or damage.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.

---

> _Unleash the power of C# for Unreal Engine game modding and cheat development!_
