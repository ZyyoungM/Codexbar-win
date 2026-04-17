using System.Threading;

namespace CodexBar.Runtime;

public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;

    public bool IsPrimary { get; }

    public SingleInstanceService(string name = "Local\\CodexBarWin")
    {
        _mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        if (createdNew)
        {
            _ownsMutex = true;
            IsPrimary = true;
            return;
        }

        try
        {
            _ownsMutex = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }

        IsPrimary = _ownsMutex;
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
