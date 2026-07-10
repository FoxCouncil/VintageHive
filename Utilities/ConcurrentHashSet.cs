// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections;
using System.Collections.Concurrent;

namespace VintageHive.Utilities;

/// <summary>
/// A thread-safe set backed by a <see cref="ConcurrentDictionary{TKey, TValue}"/>. Used where a plain HashSet
/// was mutated and read concurrently across connections (which can corrupt the bucket chain and pin a thread).
/// </summary>
public sealed class ConcurrentHashSet<T> : IEnumerable<T>
{
    private readonly ConcurrentDictionary<T, byte> _dict;

    public ConcurrentHashSet(IEqualityComparer<T> comparer = null)
    {
        _dict = comparer == null ? new ConcurrentDictionary<T, byte>() : new ConcurrentDictionary<T, byte>(comparer);
    }

    public bool Add(T item) => _dict.TryAdd(item, 0);

    public bool Remove(T item) => _dict.TryRemove(item, out _);

    public bool Contains(T item) => _dict.ContainsKey(item);

    public int Count => _dict.Count;

    public void Clear() => _dict.Clear();

    public IEnumerator<T> GetEnumerator() => _dict.Keys.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
