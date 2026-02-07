---
globs: ["tests/**/*.cs"]
description: "Test conventions for WireBound"
---

<!-- context-init:managed -->
- Framework: **TUnit** — use `[Test]` attribute, NOT `[Fact]` or `[Theory]`
- Mocking: **NSubstitute** — use `Substitute.For<T>()`, NOT `new Mock<T>()`
- Assertions: **AwesomeAssertions** — use `.Should().Be()` fluent syntax
- Database tests: extend `DatabaseTestBase` for in-memory EF Core
- LiveCharts tests: `LiveChartsHook` assembly fixture handles initialization
- Follow Arrange/Act/Assert pattern
- Name tests: `MethodName_Condition_ExpectedResult`
- Implement `IAsyncDisposable` to clean up ViewModels
