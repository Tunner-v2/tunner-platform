namespace Tunner.Platform.ObjectStorage;

public sealed class ObjectStorageReadResult : IDisposable
{
    private Action? disposeAction;

    public ObjectStorageReadResult(Stream content, Action? disposeAction = null)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        this.disposeAction = disposeAction ?? Content.Dispose;
    }

    public Stream Content { get; }

    public void Dispose()
    {
        var action = Interlocked.Exchange(ref disposeAction, null);
        action?.Invoke();
    }
}
