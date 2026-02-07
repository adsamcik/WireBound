---
globs: ["src/WireBound.Platform.Windows/**/*.cs", "src/WireBound.Platform.Linux/**/*.cs", "src/WireBound.Platform.Stub/**/*.cs"]
description: "Platform service implementation rules"
---

<!-- context-init:managed -->
- Windows classes: annotate with `[SupportedOSPlatform("windows")]`
- Linux classes: annotate with `[SupportedOSPlatform("linux")]`
- All classes should be `sealed`
- Every platform interface MUST have a stub in `Platform.Stub` returning safe defaults
- Register in the platform's `*PlatformServices.Register()` method
- Windows may use WMI, PerformanceCounter, Registry, or native P/Invoke
- Linux should use `/proc`, `nmcli`, `ss`, `iw`, or other CLI tools
- Stubs return zero/empty/default values (e.g., `Task.FromResult(0.0)`)
