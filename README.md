# RecompOne

<p align="center">
  <img src="https://github.com/user-attachments/assets/6d9a8f86-322c-4cd2-a3d3-ba68ba2d37e9" alt="logo" width="400">
</p>

[English](README.md) | [简体中文](README.zh-CN.md) | [日本語](README.ja.md)

RecompOne is a tool to statically recompile PlayStation 1 executables into C# code. it also provides a runtime layer that translates the PS1 hardware environment into something modern PCs can run natively

This project is inspired by [N64Recomp](https://github.com/N64Recomp/N64Recomp) and [XenonRecomp](https://github.com/hedge-dev/XenonRecomp), which are similar tools for N64 and Xbox 360 respectively

We have a discord server too: https://discord.gg/65g8ZEPnbR

## How it works

Static recompilation is similar to emulation in that it recreates the internal state of the hardware, the difference is that instead of fetching, decoding, and executing each instruction at runtime, the recompiler translates all MIPS operations into code ahead of time.

the generated code operates on a `CpuContext` and a memory interface. this `CpuContext` encapsulates the state the CPU would be in and the runtime simulates what the PS1 hardware would be doing and translates it into something modern hardware can understand

For example, an add instruction:
```asm
addiu t0, t0, 0x10
```
becomes:
```csharp
c.T0 = c.T0 + 0x10u;
```

and a memory read:
```asm
lw t0, 0x10(sp)
```
becomes:
```csharp
c.T0 = mem.Read32(c.SP + 0x10u);
```

the runtime, like an emulator, simulates the behaviour the game expects like the BIOS functions, MMIO, GPU drawing commands, CD-ROM, and so on, it also provides reimplementation of some libraries from psyq, since the ones that heavily rely on interrupts dont work properly under the recompiler

## Overlays

PS1 games use overlays since the ps1 ram is extremly limited, so the gane load and unload code and data into the same memory region as needed

each overlay is defined in the config file, during recompilation a separate class is generated for every overlay along with an overlay dispatch table
the dispatcher tracks which overlays are currently loaded at runtime and updates the function mappings as needed. this is needed because different overlays can occupy the same VRAM address range at different times, the runtime loads and unload then as needed so that every virtual address always resolves to the correct recompiled function

## patches

You can provide Patches that will replace a function entirely

patches are registered in the recomp config
```json
{ "address": "800553C4", "name": "MyPatch.MyClass.MyMethod" }
```
The patch class itself lives in the recompiled project, you need to create an file and implement the patched functions, then redirect them

this is for providing "base mods" for the game, like fixing functions the recompiler cant deal with (like self modifying code) or simple qol features

## Creating a recompilation

refer to the [wiki](https://github.com/BlackLabelHQ/RecompOne/wiki)

## TODO
- [ ] **MultiDisc games** current implementation doesnt deal with games that have multiple discs (should be an relatively easy implementation)

## Contributing

any contributions are welcome, This project started mainly as an hobby and i didnt expect to get something that works, but there is still a long way until this tool becomes something as mature as N64Recomp and similar tools, I woundnt say this tool is ready to make actual recomps but it can boot games

Currently im the only mantainer of the project and my free time is limited, my coding skills also arent perfect, so any help is more than welcome, be it as bug reports, feature suggestions or actual code

## Stance on AI

This project is not vibe-coded. AI was not involved in writing the code!

This project does not support vibe-coded ports. Ai can be a useful research tool, but it does not replace the human judgment needed to understand and correctly implement what you are building

You do you but I will not provide help for ports produced that way
