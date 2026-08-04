using BTCPayServer.Ndeet.Plugins.Tipcards;
using Xunit;

namespace BTCPayServer.Plugins.Tests.Tipcards;

public class TipcardStoreLockTests
{
    [Fact]
    public async Task LockAsync_SerializesWorkForTheSameStore()
    {
        var storeLock = new TipcardStoreLock();
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstLease = await storeLock.LockAsync("store-a", cancellationToken);
        var secondAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var secondTask = Task.Run(async () =>
        {
            secondAttempted.SetResult();
            using (await storeLock.LockAsync("store-a", cancellationToken))
                secondEntered.SetResult();
        }, cancellationToken);

        await secondAttempted.Task;
        await Task.Yield();
        Assert.False(secondEntered.Task.IsCompleted);

        firstLease.Dispose();

        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        await secondTask.WaitAsync(cancellationToken);
    }

    [Fact]
    public async Task LockAsync_AllowsDifferentStoresToProceedIndependently()
    {
        var storeLock = new TipcardStoreLock();
        var cancellationToken = TestContext.Current.CancellationToken;
        using var firstStoreLease = await storeLock.LockAsync("store-a", cancellationToken);

        using var secondStoreLease = await storeLock
            .LockAsync("store-b", cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
    }

    [Fact]
    public async Task LockAsync_HonorsCancellationWhileWaiting()
    {
        var storeLock = new TipcardStoreLock();
        using var firstLease = await storeLock.LockAsync(
            "store-a",
            TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await storeLock.LockAsync("store-a", cancellation.Token));
    }
}
