# RCT3 retail comparison bridge

This directory builds two pinned, 32-bit Windows binaries:

- `RCT3Launcher.exe` verifies the configured retail executable, creates it suspended,
  injects `RCT3Bridge.dll` with `LoadLibraryW`, and resumes it only after injection succeeds.
- `RCT3Bridge.dll` hooks the retail D3D9 device for render-thread backbuffer capture and
  final view/projection matrix telemetry.

The MCP server supplies a random pipe name and nonce through the child environment. The
pipe ACL grants access only to its current Windows owner. The protocol exposes no process
memory operations and rejects oversized, malformed, wrong-version, or wrong-nonce input.
The retail click tool posts bounded client-area input only to the verified owned process and
does not activate the window or move the desktop cursor.

Build and test from PowerShell:

```powershell
./RCT3Bridge/build.ps1 -Test
```

The bridge is loaded from the build output. Nothing is copied into the retail installation.
