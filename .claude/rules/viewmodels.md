---
globs: ["src/WireBound.Avalonia/ViewModels/**/*.cs"]
description: "MVVM ViewModel conventions for WireBound"
---

<!-- context-init:managed -->
- Inherit from `ObservableObject` (CommunityToolkit.Mvvm)
- Class must be `partial` for source generators
- Use `[ObservableProperty]` on private `_camelCase` fields for observable properties
- Use `[RelayCommand]` on methods to generate `...Command` properties
- Use `partial void On{Property}Changed()` for property change side effects
- Subscribe to service events in constructor, unsubscribe in `Dispose()`
- Implement `IDisposable` if subscribing to events
- ViewModels are registered as **singletons** — avoid storing view-specific state that should reset
- Update UI-bound chart data via `Dispatcher.UIThread.InvokeAsync()`
