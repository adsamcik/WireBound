using FluentAssertions;
using NSubstitute;
using TUnit.Core;
using WireBound.Maui.Models;
using WireBound.Maui.Services;
using WireBound.Maui.ViewModels;

namespace WireBound.Maui.Tests.ViewModels;

public class HistoryViewModelTests
{
    private IDataPersistenceService _persistenceMock = null!;

    [Before(Test)]
    public async Task Setup()
    {
        _persistenceMock = Substitute.For<IDataPersistenceService>();

        // Setup default returns
        _persistenceMock.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new List<DailyUsage>());
        _persistenceMock.GetTotalUsageAsync()
            .Returns((0L, 0L));

        await Task.CompletedTask;
    }

    [Test]
    public async Task Constructor_ShouldInitializeDefaultValues()
    {
        // Arrange & Act
        var viewModel = new HistoryViewModel(_persistenceMock);

        // Assert
        viewModel.SelectedDate.Should().BeCloseTo(DateTime.Today, TimeSpan.FromSeconds(1));
        viewModel.TotalReceived.Should().Be("0 B");
        viewModel.TotalSent.Should().Be("0 B");
        viewModel.PeriodReceived.Should().Be("0 B");
        viewModel.PeriodSent.Should().Be("0 B");

        await Task.CompletedTask;
    }

    [Test]
    public async Task Constructor_ShouldSetDateRangeToLast30Days()
    {
        // Arrange & Act
        var viewModel = new HistoryViewModel(_persistenceMock);

        // Assert
        viewModel.EndDate.Should().Be(DateOnly.FromDateTime(DateTime.Today));
        viewModel.StartDate.Should().Be(DateOnly.FromDateTime(DateTime.Today.AddDays(-30)));

        await Task.CompletedTask;
    }

    [Test]
    public async Task DailyUsages_ShouldBeInitialized()
    {
        // Arrange & Act
        var viewModel = new HistoryViewModel(_persistenceMock);

        // Assert
        viewModel.DailyUsages.Should().NotBeNull();

        await Task.CompletedTask;
    }

    [Test]
    public async Task DailySeries_ShouldBeInitialized()
    {
        // Arrange & Act
        var viewModel = new HistoryViewModel(_persistenceMock);

        // Assert
        viewModel.DailySeries.Should().NotBeNull();

        await Task.CompletedTask;
    }

    [Test]
    public async Task XAxes_ShouldBeConfigured()
    {
        // Arrange & Act
        var viewModel = new HistoryViewModel(_persistenceMock);

        // Assert
        viewModel.XAxes.Should().NotBeNull();
        viewModel.XAxes.Should().HaveCount(1);
        viewModel.XAxes[0].Name.Should().Be("Date");

        await Task.CompletedTask;
    }

    [Test]
    public async Task YAxes_ShouldBeConfigured()
    {
        // Arrange & Act
        var viewModel = new HistoryViewModel(_persistenceMock);

        // Assert
        viewModel.YAxes.Should().NotBeNull();
        viewModel.YAxes.Should().HaveCount(1);
        viewModel.YAxes[0].Name.Should().Be("Data Usage");

        await Task.CompletedTask;
    }

    [Test]
    public async Task RefreshCommand_ShouldExist()
    {
        // Arrange
        var viewModel = new HistoryViewModel(_persistenceMock);

        // Assert
        viewModel.RefreshCommand.Should().NotBeNull();

        await Task.CompletedTask;
    }

    [Test]
    public async Task LoadLast7DaysCommand_ShouldExist()
    {
        // Arrange
        var viewModel = new HistoryViewModel(_persistenceMock);

        // Assert
        viewModel.LoadLast7DaysCommand.Should().NotBeNull();

        await Task.CompletedTask;
    }

    [Test]
    public async Task LoadLast30DaysCommand_ShouldExist()
    {
        // Arrange
        var viewModel = new HistoryViewModel(_persistenceMock);

        // Assert
        viewModel.LoadLast30DaysCommand.Should().NotBeNull();

        await Task.CompletedTask;
    }

    [Test]
    public async Task LoadThisMonthCommand_ShouldExist()
    {
        // Arrange
        var viewModel = new HistoryViewModel(_persistenceMock);

        // Assert
        viewModel.LoadThisMonthCommand.Should().NotBeNull();

        await Task.CompletedTask;
    }

    [Test]
    public async Task DateRange_ShouldBeSettable()
    {
        // Arrange
        var viewModel = new HistoryViewModel(_persistenceMock);
        var newStart = DateOnly.FromDateTime(DateTime.Today.AddDays(-7));
        var newEnd = DateOnly.FromDateTime(DateTime.Today);

        // Act
        viewModel.StartDate = newStart;
        viewModel.EndDate = newEnd;

        // Assert
        viewModel.StartDate.Should().Be(newStart);
        viewModel.EndDate.Should().Be(newEnd);

        await Task.CompletedTask;
    }

    [Test]
    public async Task Dispose_ShouldNotThrow()
    {
        // Arrange
        var viewModel = new HistoryViewModel(_persistenceMock);

        // Act
        var act = () => viewModel.Dispose();

        // Assert
        act.Should().NotThrow();

        await Task.CompletedTask;
    }
}
