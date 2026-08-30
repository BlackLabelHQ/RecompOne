namespace RecompOne.Runtime.Events;

/// <summary>type routed event bus to listen and send events</summary>
public static class Event
{
    private static readonly object _gate = new();

    private static class Bag<T> where T : IEvent
    {
        public static Action<T>[] Handlers = Array.Empty<Action<T>>();
    }

    private static readonly Dictionary<string, Action<IEvent>[]> _byId = new(StringComparer.Ordinal);
    private static volatile bool _anyId;

    public static void AddListener<T>(Action<T> fn) where T : IEvent
    {
        lock (_gate)
        {
            var cur = Bag<T>.Handlers;
            var next = new Action<T>[cur.Length + 1];
            Array.Copy(cur, next, cur.Length);
            next[cur.Length] = fn;
            Bag<T>.Handlers = next;
        }
    }

    public static bool RemoveListener<T>(Action<T> fn) where T : IEvent
    {
        lock (_gate)
        {
            var cur = Bag<T>.Handlers;
            var i = Array.IndexOf(cur, fn);
            if (i < 0) return false;
            if (cur.Length == 1)
            {
                Bag<T>.Handlers = Array.Empty<Action<T>>();
                return true;
            }

            var next = new Action<T>[cur.Length - 1];
            Array.Copy(cur, 0, next, 0, i);
            Array.Copy(cur, i + 1, next, i, cur.Length - i - 1);
            Bag<T>.Handlers = next;
            return true;
        }
    }

    public static void AddListener(string id, Action<IEvent> fn)
    {
        lock (_gate)
        {
            _byId.TryGetValue(id, out var cur);
            cur ??= Array.Empty<Action<IEvent>>();
            var next = new Action<IEvent>[cur.Length + 1];
            Array.Copy(cur, next, cur.Length);
            next[cur.Length] = fn;
            _byId[id] = next;
            _anyId = true;
        }
    }

    public static bool RemoveListener(string id, Action<IEvent> fn)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(id, out var cur)) return false;
            var i = Array.IndexOf(cur, fn);
            if (i < 0) return false;
            if (cur.Length == 1)
            {
                _byId.Remove(id);
            }
            else
            {
                var next = new Action<IEvent>[cur.Length - 1];
                Array.Copy(cur, 0, next, 0, i);
                Array.Copy(cur, i + 1, next, i, cur.Length - i - 1);
                _byId[id] = next;
            }

            _anyId = _byId.Count != 0;
            return true;
        }
    }

    public static bool HasListeners<T>() where T : IEvent
    {
        return Bag<T>.Handlers.Length != 0;
    }

    public static bool HasListeners(string id)
    {
        return _byId.TryGetValue(id, out var h) && h.Length != 0;
    }

    public static bool HasAnyListeners<T>() where T : IEvent
    {
        return Bag<T>.Handlers.Length != 0 || (_anyId && _byId.ContainsKey(typeof(T).Name));
    }

    public static void Dispatch<T>(T evt) where T : IEvent
    {
        var handlers = Bag<T>.Handlers;
        for (var i = 0; i < handlers.Length; i++)
            try
            {
                handlers[i](evt);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[Event] {typeof(T).Name} listener throwed: {e}");
            }

        if (_anyId && _byId.TryGetValue(evt.Id, out var idh))
            for (var i = 0; i < idh.Length; i++)
                try
                {
                    idh[i](evt);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"[Event] {evt.Id} listener throwed: {e}");
                }
    }
}