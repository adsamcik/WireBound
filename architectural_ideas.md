# Architectural Improvement Ideas for WireBound UI Responsiveness

## 1. "Headless" ViewModels with Active-View Rehydration
**Pitch**: De-couple ViewModels from the UI thread lifecycle entirely by making them pure state containers that never update UI properties unless "hydrated" by an active View.
**Operation**: Change ViewModels to stop updating `[ObservableProperty]` fields when inactive. Instead, accumulate changes in a lightweight lock-free struct buffer. When the View becomes active (via `OnNavigatedTo`), "rehydrate" the ViewModel by flushing the buffer to the observable properties in one batch.
**Advantage**: Eliminates 100% of property change notification costs for the 7 invisible ViewModels, solving the N-1 hidden listener problem.
**Risk**: State desynchronization bugs where a View wakes up showing stale data if the rehydration logic misses a flag.
**Confidence**: Grounded (Standard MVVM optimization pattern).

## 2. The "Render Proxy" Pattern (UI Thread Isolation)
**Pitch**: Stop ViewModels from touching the UI thread directly; instead, have them write to a "Render Proxy" object that the View polls on its own `CompositionTarget.Rendering` cycle.
**Operation**: ViewModels update a thread-safe `RenderState` object (plain POCO) on a background thread. The View (code-behind) subscribes to `CompositionTarget.Rendering` and manually copies values from `RenderState` to Avalonia Controls/TextBlocks only when a frame is actually being drawn.
**Advantage**: Decouples logic frequency (polling) from render frequency (60fps). Drops frames gracefully under load instead of clogging the Dispatcher queue.
**Risk**: Breaks standard MVVM data binding convenience; requires writing manual "glue" code in code-behind or custom controls.
**Confidence**: Bold/Unconventional (Bypasses bindings for raw performance).

## 3. Dedicated "Metrics Bus" with Priority Lanes
**Pitch**: Replace C# events with a specialized high-performance message bus that supports channel prioritization and automatic shedding of stale messages.
**Operation**: Create a `IMetricsBus` service. Singletons subscribe to specific channels (e.g., "Network.Overview", "Network.Detail"). The bus implementation uses `System.Threading.Channels` with `BoundedChannelFullMode.DropOldest`. When the UI thread is busy, the bus drops intermediate update packets automatically, ensuring the UI only ever processes the *latest* state.
**Advantage**: Provides automatic, centralized backpressure. If the UI slows down, the queue doesn't grow; updates simply skip.
**Risk**: Complexity in tuning channel bounds; potential to miss "edge trigger" events (like "Disconnect") if they are treated as dropped metrics.
**Confidence**: Grounded (Standard pattern in high-frequency trading UIs).

## 4. SkiaSharp Off-Screen Composition
**Pitch**: Move chart rendering entirely off the UI thread by rendering to an off-screen surface and handing a finished bitmap to the UI.
**Operation**: Instead of binding data points to a LiveCharts control on the UI thread, run a background thread that draws the chart to an `SKSurface`. Signal the UI thread only when a new frame is ready, then swap the `WriteableBitmap` pointer.
**Advantage**: Removes the most expensive operation (geometry calculation and rasterization) from the UI thread.
**Risk**: High memory bandwidth usage from passing bitmaps; finding a thread-safe way to host LiveCharts in a headless context.
**Confidence**: Speculative (LiveCharts might heavily depend on UI dispatchers internally).

## 5. "One Ring" Orchestrator (Singleton Inversion)
**Pitch**: Invert the ownership model—instead of 8 ViewModels subscribing to the Service, have one `UxOrchestrator` service that *pushes* state only to the currently active ViewModel.
**Operation**: The `UxOrchestrator` tracks the current `Route`. When data arrives, it looks up the specific ViewModel active for that route and calls `Update(data)` on it directly. Inactive ViewModels receive nothing.
**Advantage**: Reduces event fan-out complexity from O(N) to O(1). Absolute minimal CPU usage for hidden screens.
**Risk**: Tighter coupling between navigation and data flow; makes "background history tracking" in ViewModels harder (they must pull history when becoming active instead of building it live).
**Confidence**: Grounded (Solid architectural simplification).

## 6. Worker-Side ViewModel projection
**Pitch**: Run the entire ViewModel layer on a background thread, using Avalonia's `Dispatcher.Invoke` *only* for the final `PropertyChanged` event invocation.
**Operation**: Wrap the entire `OnNetworkStatsUpdated` logic in `Task.Run()` (or keep it on the thread pool). Perform all calculations, string formatting, and collection diffing on the background thread. Only when the final value is ready, marshal the assignment to the UI thread: `Dispatcher.UIThread.InvokeAsync(() => DisplayValue = calculatedValue)`.
**Advantage**: Keeps business logic computation off the UI thread without changing the architecture significantly.
**Risk**: Thread safety hell. Accessing `ObservableCollection` or other UI-bound types from a background thread requires careful synchronization or usage of `BindingOperations.EnableCollectionSynchronization` equivalents.
**Confidence**: Grounded (Standard parallelism).

## 7. The "Visual Tree Slicing" Strategy
**Pitch**: Physically detach the visual trees of inactive Views from the window to stop Avalonia from participating in layout passes or binding updates for hidden tabs.
**Operation**: Instead of just hiding views (Opacity/Visibility), implementing a custom `ContentControl` that serializes the state of the outgoing view and sets its `Content` to null. When navigating back, re-create the view from scratch or re-attach the cached instance.
**Advantage**: "Hidden" UI elements often still consume layout cycles or binding checks. Removing them stops this overhead completely.
**Risk**: Loss of scroll position, text selection, or transient state not backed by the ViewModel; expensive visual tree reconstruction on tab switch.
**Confidence**: Bold/Unconventional (radical view recycling).

## 8. Delta-Compression Updates
**Pitch**: Only dispatch updates to the UI thread if the visual representation has actually changed significantly (visual delta), not just data delta.
**Operation**: Implement a `SmartProperty<T>` struct that holds a "last rendered value". When `OnNetworkStatsUpdated` fires, calculate the new value on the background thread. Compare `Abs(New - Old) > Threshold`. Only if the change is visible (e.g., >1% pixel shift on a chart, or string text changes), dispatch the update.
**Advantage**: Filters out "micro-updates" (e.g., download speed changing from 10.1MB/s to 10.12MB/s) that consume cycles but provide no user value.
**Risk**: "Frozen" UI feel if thresholds are too high; complexity in defining "significant change" for complex objects.
**Confidence**: Grounded (Standard optimization in game development).
