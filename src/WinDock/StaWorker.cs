namespace WinDock;

/// <summary>Runs Shell COM work away from the dispatcher in its own STA apartment.</summary>
internal static class StaWorker
{
    internal static Task<T> RunAsync<T>(Func<CancellationToken, T> work, CancellationToken token)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { token.ThrowIfCancellationRequested(); completion.TrySetResult(work(token)); }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true, Name = "WinDock Shell catalogue" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
