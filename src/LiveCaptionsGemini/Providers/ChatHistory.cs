using System.Collections.Generic;

namespace LiveCaptionsGemini.Providers;

/// <summary>The last N user/model exchanges of one provider. Thread-safe.</summary>
internal sealed class ChatHistory
{
    public readonly record struct Turn(string User, string Model);

    private readonly object _lock = new();
    private readonly Queue<Turn> _turns = new();
    private readonly int _maxTurns;

    /// <param name="maxTurns">0 disables history.</param>
    public ChatHistory(int maxTurns) => _maxTurns = maxTurns;

    public Turn[] Snapshot()
    {
        lock (_lock) return _turns.ToArray();
    }

    /// <summary>Call only after a successful answer.</summary>
    public void Add(string user, string model)
    {
        if (_maxTurns <= 0) return;
        lock (_lock)
        {
            _turns.Enqueue(new Turn(user, model));
            while (_turns.Count > _maxTurns) _turns.Dequeue();
        }
    }
}
