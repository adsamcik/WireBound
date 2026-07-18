using Microsoft.Extensions.Logging;
using WireBound.Avalonia.ViewModels;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Services;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.ViewModels;

public class AppsViewModelTests : IAsyncDisposable
{
    private readonly IUiDispatcher _dispatcher = new SynchronousDispatcher();
    private readonly IAppOverviewService _appOverviewService;
    private readonly IElevationService _elevationService;
    private readonly INavigationService _navigationService;
    private readonly ILogger<AppsViewModel> _logger;
    private readonly List<AppsViewModel> _createdViewModels = [];

    public AppsViewModelTests()
    {
        _appOverviewService = Substitute.For<IAppOverviewService>();
        _elevationService = Substitute.For<IElevationService>();
        _navigationService = Substitute.For<INavigationService>();
        _logger = Substitute.For<ILogger<AppsViewModel>>();

        SetupDefaultMocks();
    }

    [Test]
    public async Task InitializeAsync_LoadsAppsFromService()
    {
        // Arrange
        var apps = new[]
        {
            CreateApp("firefox", "Firefox", "firefox.exe", "Browsers", bytesReceived: 1024, bytesSent: 512),
            CreateApp("code", "Visual Studio Code", "Code.exe", "Development", bytesReceived: 2048, bytesSent: 1024),
            CreateApp("terminal", "Terminal", "wt.exe", "System", bytesReceived: 3072, bytesSent: 1536)
        };

        // Act
        var viewModel = await CreateInitializedViewModelAsync(apps);

        // Assert
        viewModel.Apps.Should().HaveCount(3);
        viewModel.AppCount.Should().Be(3);
        viewModel.TotalDownload.Should().Be("6.00 KB");
        viewModel.TotalUpload.Should().Be("3.00 KB");
    }

    [Test]
    public async Task AvailableCategories_DerivedFromLoadedApps()
    {
        // Arrange
        var apps = new[]
        {
            CreateApp("firefox", "Firefox", "firefox.exe", "Browsers"),
            CreateApp("code", "Visual Studio Code", "Code.exe", "Development"),
            CreateApp("chrome", "Chrome", "chrome.exe", "Browsers"),
            CreateApp("unknown", "Unknown", "unknown.exe", "")
        };

        // Act
        var viewModel = await CreateInitializedViewModelAsync(apps);

        // Assert
        viewModel.AvailableCategories.Should().ContainInOrder("All", "Browsers", "Development");
        viewModel.AvailableCategories.Should().HaveCount(3);
    }

    [Test]
    public async Task SearchText_FiltersByAppNameOrProcessName()
    {
        // Arrange
        var apps = new[]
        {
            CreateApp("firefox", "Firefox", "firefox.exe", "Browsers", bytesReceived: 300),
            CreateApp("chrome", "Chrome", "chrome.exe", "Browsers", bytesReceived: 200),
            CreateApp("code", "Visual Studio Code", "Code.exe", "Development", bytesReceived: 100)
        };
        var viewModel = await CreateInitializedViewModelAsync(apps);

        // Act
        viewModel.SearchText = "fox";
        await WaitForAsync(() => viewModel.Apps.Count == 1);

        // Assert
        viewModel.Apps.Should().ContainSingle();
        viewModel.Apps[0].AppName.Should().Be("Firefox");

        // Act
        viewModel.SearchText = "";
        await WaitForAsync(() => viewModel.Apps.Count == 3);

        // Assert
        viewModel.Apps.Should().HaveCount(3);
    }

    [Test]
    public async Task SelectedCategory_FiltersList()
    {
        // Arrange
        var apps = new[]
        {
            CreateApp("firefox", "Firefox", "firefox.exe", "Browsers", bytesReceived: 300),
            CreateApp("chrome", "Chrome", "chrome.exe", "Browsers", bytesReceived: 200),
            CreateApp("code", "Visual Studio Code", "Code.exe", "Development", bytesReceived: 100)
        };
        var viewModel = await CreateInitializedViewModelAsync(apps);

        // Act
        viewModel.SelectedCategory = "Browsers";

        // Assert
        viewModel.Apps.Should().HaveCount(2);
        viewModel.Apps.Should().OnlyContain(app => app.CategoryName == "Browsers");

        // Act
        viewModel.SelectedCategory = "All";

        // Assert
        viewModel.Apps.Should().HaveCount(3);
    }

    [Test]
    public async Task ToggleSort_FirstClick_SortsDescendingByColumn()
    {
        // Arrange — the VM default sort is TotalBytes descending, so test the
        // "switch to a different column" path by toggling on Name instead.
        var apps = new[]
        {
            CreateApp("small", "Small", "small.exe", "System", bytesReceived: 100),
            CreateApp("large", "Large", "large.exe", "System", bytesReceived: 500),
            CreateApp("middle", "Middle", "middle.exe", "System", bytesReceived: 300)
        };
        var viewModel = await CreateInitializedViewModelAsync(apps);

        // Act
        viewModel.ToggleSortCommand.Execute(AppsSortColumn.Name);

        // Assert
        viewModel.SortColumn.Should().Be(AppsSortColumn.Name);
        viewModel.SortDescending.Should().BeTrue();
        viewModel.Apps[0].AppName.Should().Be("Small");
        viewModel.Apps[2].AppName.Should().Be("Large");
    }

    [Test]
    public async Task ToggleSort_SecondClickSameColumn_FlipsDirection()
    {
        // Arrange
        var apps = new[]
        {
            CreateApp("alpha", "Alpha", "alpha.exe", "System"),
            CreateApp("bravo", "Bravo", "bravo.exe", "System"),
            CreateApp("charlie", "Charlie", "charlie.exe", "System")
        };
        var viewModel = await CreateInitializedViewModelAsync(apps);

        // Act
        viewModel.ToggleSortCommand.Execute(AppsSortColumn.Name);
        var descending = viewModel.Apps.Select(app => app.AppName).ToArray();
        viewModel.ToggleSortCommand.Execute(AppsSortColumn.Name);
        var ascending = viewModel.Apps.Select(app => app.AppName).ToArray();

        // Assert
        descending.Should().Equal("Charlie", "Bravo", "Alpha");
        ascending.Should().Equal("Alpha", "Bravo", "Charlie");
    }

    [Test]
    public async Task ToggleSort_DifferentColumn_ResetsToDescending()
    {
        // Arrange
        var apps = new[]
        {
            CreateApp("alpha", "Alpha", "alpha.exe", "System", bytesReceived: 100),
            CreateApp("bravo", "Bravo", "bravo.exe", "System", bytesReceived: 500),
            CreateApp("charlie", "Charlie", "charlie.exe", "System", bytesReceived: 300)
        };
        var viewModel = await CreateInitializedViewModelAsync(apps);
        viewModel.ToggleSortCommand.Execute(AppsSortColumn.Name);
        viewModel.ToggleSortCommand.Execute(AppsSortColumn.Name);

        // Act
        viewModel.ToggleSortCommand.Execute(AppsSortColumn.BytesReceived);

        // Assert
        viewModel.SortDescending.Should().BeTrue();
        viewModel.Apps.Select(app => app.AppName).Should().Equal("Bravo", "Charlie", "Alpha");
    }

    [Test]
    public async Task SelectAppAsync_PopulatesDetailDrawer()
    {
        // Arrange
        var app = CreateApp("firefox", "Firefox", "firefox.exe", "Browsers");
        var networkHistory = CreateNetworkHistory(5);
        var resourceHistory = CreateResourceHistory(5);
        var topDestinations = CreateTopDestinations(3);
        SetupOverview(app);
        SetupNetworkHistory(networkHistory);
        SetupResourceHistory(resourceHistory);
        SetupTopDestinations(topDestinations);
        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Act
        await viewModel.SelectAppCommand.ExecuteAsync(app);

        // Assert
        viewModel.IsDetailOpen.Should().BeTrue();
        viewModel.SelectedApp.Should().BeSameAs(app);
        viewModel.NetworkHistorySeries.Should().NotBeEmpty();
        viewModel.CpuHistorySeries.Should().NotBeEmpty();
        viewModel.RamHistorySeries.Should().NotBeEmpty();
        viewModel.TopDestinations.Should().HaveCount(3);
    }

    [Test]
    public async Task CloseDetail_ClearsSelectionAndCharts()
    {
        // Arrange
        var app = CreateApp("firefox", "Firefox", "firefox.exe", "Browsers");
        SetupOverview(app);
        SetupNetworkHistory(CreateNetworkHistory(5));
        SetupResourceHistory(CreateResourceHistory(5));
        SetupTopDestinations(CreateTopDestinations(3));
        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;
        await viewModel.SelectAppCommand.ExecuteAsync(app);

        // Act
        viewModel.CloseDetailCommand.Execute(null);

        // Assert
        viewModel.IsDetailOpen.Should().BeFalse();
        viewModel.SelectedApp.Should().BeNull();
        viewModel.NetworkHistorySeries.Should().BeEmpty();
        viewModel.CpuHistorySeries.Should().BeEmpty();
        viewModel.RamHistorySeries.Should().BeEmpty();
        viewModel.TopDestinations.Should().BeEmpty();
    }

    [Test]
    public async Task HelperDisconnected_SetsRequiresElevationAndByteTrackingLimited()
    {
        // Arrange
        _elevationService.IsElevationSupported.Returns(true);
        _elevationService.IsHelperConnected.Returns(true);
        _elevationService.RequiresElevationFor(ElevatedFeature.PerProcessNetworkMonitoring).Returns(true);
        var viewModel = await CreateInitializedViewModelAsync();

        // Act
        _elevationService.IsHelperConnected.Returns(false);
        _elevationService.HelperConnectionStateChanged += Raise.Event<EventHandler<HelperConnectionStateChangedEventArgs>>(
            _elevationService,
            new HelperConnectionStateChangedEventArgs(false, "Disconnected"));

        // Assert
        viewModel.RequiresElevation.Should().BeTrue();
        viewModel.IsByteTrackingLimited.Should().BeTrue();
    }

    [Test]
    public async Task HelperConnected_ClearsRequiresElevationAndByteTrackingLimited()
    {
        // Arrange
        _elevationService.IsElevationSupported.Returns(true);
        _elevationService.IsHelperConnected.Returns(false);
        _elevationService.RequiresElevationFor(ElevatedFeature.PerProcessNetworkMonitoring).Returns(true);
        var viewModel = await CreateInitializedViewModelAsync();

        // Act
        _elevationService.IsHelperConnected.Returns(true);
        _elevationService.HelperConnectionStateChanged += Raise.Event<EventHandler<HelperConnectionStateChangedEventArgs>>(
            _elevationService,
            new HelperConnectionStateChangedEventArgs(true, "Connected"));

        // Assert
        viewModel.RequiresElevation.Should().BeFalse();
        viewModel.IsByteTrackingLimited.Should().BeFalse();
    }

    [Test]
    public async Task IsInitialLoading_FalseAfterFirstLoad()
    {
        // Arrange & Act
        var viewModel = await CreateInitializedViewModelAsync(
            CreateApp("firefox", "Firefox", "firefox.exe", "Browsers"));

        // Assert
        viewModel.IsInitialLoading.Should().BeFalse();

        // Act
        await viewModel.RefreshCommand.ExecuteAsync(null);

        // Assert
        viewModel.IsInitialLoading.Should().BeFalse();
    }

    [Test]
    public async Task Refresh_DoesNotResetIsInitialLoading()
    {
        // Arrange
        var viewModel = await CreateInitializedViewModelAsync();
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshCompleted = new TaskCompletionSource<IReadOnlyList<AppOverview>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _appOverviewService.GetOverviewAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                refreshStarted.TrySetResult();
                return refreshCompleted.Task;
            });

        // Act
        var refreshTask = viewModel.RefreshCommand.ExecuteAsync(null);
        await refreshStarted.Task;

        // Assert
        viewModel.IsInitialLoading.Should().BeFalse();

        // Act
        refreshCompleted.SetResult([]);
        await refreshTask;

        // Assert
        viewModel.IsInitialLoading.Should().BeFalse();
    }

    [Test]
    public async Task Dispose_UnsubscribesFromEvents()
    {
        // Arrange
        _elevationService.IsElevationSupported.Returns(true);
        _elevationService.IsHelperConnected.Returns(true);
        _elevationService.RequiresElevationFor(ElevatedFeature.PerProcessNetworkMonitoring).Returns(true);
        var viewModel = await CreateInitializedViewModelAsync();
        viewModel.RequiresElevation.Should().BeFalse();
        viewModel.IsByteTrackingLimited.Should().BeFalse();

        // Act
        viewModel.Dispose();
        _elevationService.IsHelperConnected.Returns(false);
        _elevationService.HelperConnectionStateChanged += Raise.Event<EventHandler<HelperConnectionStateChangedEventArgs>>(
            _elevationService,
            new HelperConnectionStateChangedEventArgs(false, "Disconnected"));

        // Assert
        viewModel.RequiresElevation.Should().BeFalse();
        viewModel.IsByteTrackingLimited.Should().BeFalse();
    }

    public ValueTask DisposeAsync()
    {
        foreach (var viewModel in _createdViewModels)
        {
            viewModel.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private void SetupDefaultMocks()
    {
        _elevationService.IsElevationSupported.Returns(true);
        _elevationService.IsHelperConnected.Returns(true);
        _elevationService.RequiresElevationFor(Arg.Any<ElevatedFeature>()).Returns(false);

        SetupOverview();
        SetupNetworkHistory();
        SetupResourceHistory();
        SetupTopDestinations();
    }

    private AppsViewModel CreateViewModel()
    {
        var viewModel = new AppsViewModel(
            _dispatcher,
            _appOverviewService,
            _elevationService,
            _navigationService,
            _logger);
        _createdViewModels.Add(viewModel);
        return viewModel;
    }

    private async Task<AppsViewModel> CreateInitializedViewModelAsync(params AppOverview[] apps)
    {
        SetupOverview(apps);
        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;
        return viewModel;
    }

    private void SetupOverview(params AppOverview[] apps)
    {
        _appOverviewService.GetOverviewAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AppOverview>>(apps));
    }

    private void SetupNetworkHistory(params AppNetworkHistoryPoint[] points)
    {
        _appOverviewService.GetNetworkHistoryAsync(
                Arg.Any<string>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AppNetworkHistoryPoint>>(points));
    }

    private void SetupResourceHistory(params AppResourceHistoryPoint[] points)
    {
        _appOverviewService.GetResourceHistoryAsync(
                Arg.Any<string>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AppResourceHistoryPoint>>(points));
    }

    private void SetupTopDestinations(params TopDestinationEntry[] entries)
    {
        _appOverviewService.GetTopDestinationsAsync(
                Arg.Any<int>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TopDestinationEntry>>(entries));
    }

    /// <summary>
    /// Polls <paramref name="condition"/> until it is true or a timeout
    /// elapses, instead of a fixed <c>Task.Delay</c>. The SearchText filter
    /// is applied via a 200ms debounce (see <c>AppsViewModel.DebouncedRecomputeAsync</c>);
    /// a fixed delay left too little margin on slower/loaded CI runners and
    /// made this test flaky there even though it always passed locally.
    /// </summary>
    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }
    }

    private static AppOverview CreateApp(
        string appIdentifier,
        string appName,
        string processName,
        string categoryName,
        long bytesReceived = 0,
        long bytesSent = 0,
        double avgCpuPercent = 0,
        long avgPrivateBytes = 0,
        int hoursActive = 1)
    {
        var now = DateTime.Now;
        return new AppOverview(
            appIdentifier,
            appName,
            processName,
            $@"C:\Apps\{processName}",
            categoryName,
            bytesReceived,
            bytesSent,
            PeakDownloadSpeed: 0,
            PeakUploadSpeed: 0,
            avgCpuPercent,
            MaxCpuPercent: avgCpuPercent,
            avgPrivateBytes,
            PeakPrivateBytes: avgPrivateBytes,
            FirstSeen: now.AddHours(-hoursActive),
            LastSeen: now,
            hoursActive);
    }

    private static AppNetworkHistoryPoint[] CreateNetworkHistory(int count)
    {
        var start = DateTime.Now.AddHours(-count);
        return Enumerable.Range(0, count)
            .Select(index => new AppNetworkHistoryPoint(start.AddHours(index), (index + 1) * 1024, (index + 1) * 512))
            .ToArray();
    }

    private static AppResourceHistoryPoint[] CreateResourceHistory(int count)
    {
        var start = DateTime.Now.AddHours(-count);
        return Enumerable.Range(0, count)
            .Select(index => new AppResourceHistoryPoint(start.AddHours(index), 10 + index, (index + 1) * 1_048_576))
            .ToArray();
    }

    private static TopDestinationEntry[] CreateTopDestinations(int count)
    {
        return Enumerable.Range(1, count)
            .Select(index => new TopDestinationEntry($"192.0.2.{index}", $"host-{index}.example", 443, "TCP", index * 2048, index * 4096))
            .ToArray();
    }
}
