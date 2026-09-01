# Compilation

## Requirements

- Windows 11
- .NET 8 SDK
- Visual Studio 2022 (17.8 or later) with the ".NET desktop development"
  and "F# desktop language support" workloads, or the `dotnet` CLI
- Inno Setup 7 (only required to build the installer)

## Embedding FFmpeg

Before building, place a Windows build of `ffmpeg.exe` at:

```
tools/ffmpeg/ffmpeg.exe
```

This file is intentionally excluded from source control (see `.gitignore`)
due to its size. It is required for the `Videotoy.App` project to produce
a working build and for the installer to embed it.

Then generate the expected SHA-256 hash file, checked by the application at
every startup before FFmpeg is used:

```
powershell -ExecutionPolicy Bypass -File tools/ffmpeg/generate-hash.ps1
```

This produces `tools/ffmpeg/ffmpeg.exe.sha256`, which is tracked in source
control (unlike `ffmpeg.exe` itself) and must be regenerated whenever the
embedded `ffmpeg.exe` binary is updated.

## Embedding Tint (optional, WGSL support)

Unlike FFmpeg, embedding Tint is **entirely optional**. Videotoy builds and
runs fine without it — GLSL and native HLSL shaders are unaffected. Only
opening a `.wgsl` file requires it, and its absence surfaces as a normal
error in the "Shader Issues" panel rather than a startup failure.

To enable WGSL support, place a Windows build of `tint.exe` (the WGSL
frontend/backend CLI from the Dawn project) at:

```
tools/tint/tint.exe
```

This file is intentionally excluded from source control (see `.gitignore`)
due to its size, same as `ffmpeg.exe`. Then generate its expected SHA-256
hash file:

```
powershell -ExecutionPolicy Bypass -File tools/tint/generate-hash.ps1
```

This produces `tools/tint/tint.exe.sha256`, tracked in source control, and
must be regenerated whenever the embedded `tint.exe` binary is updated. See
`tools/tint/README.md` for details, including a note on the exact CLI
invocation assumption made by `WgslTranspilerProcess` (`Videotoy.Transpiler`)
— verify it against the actual binary obtained, since it could not be
confirmed without network access at the time this was written.

## Building from the command line

```
dotnet restore Videotoy.sln
dotnet build Videotoy.sln -c Release
```

## Running

```
dotnet run --project src/Videotoy.App/Videotoy.App.csproj
```

## Building the installer

1. Build the solution in `Release` configuration.
2. Open `installer/Videotoy.iss` in Inno Setup 7.
3. Compile the script to produce the final installer executable.

The installer's version, application icon (Start Menu / Desktop shortcuts)
and installer icon are all picked up automatically: the version is read
directly from `Videotoy.exe`'s compiled version resource (no manual edit
needed in `Videotoy.iss`), and the icons come from
`src/Videotoy.App/Assets/Icons/app.ico` and `installer/installer.ico`. The
script fails to compile with a clear error if the Release build, either
icon, or the embedded `ffmpeg.exe` is missing.
