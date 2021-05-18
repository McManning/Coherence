using System;
using System.Collections.Generic;

namespace Coherence
{
    /// <summary>
    /// Convenience methods for a Dictionary containing a HashSet.
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <typeparam name="TValue"></typeparam>
    internal class DictionarySet<TKey, TValue> : Dictionary<TKey, HashSet<TValue>>
    {
        internal HashSet<TValue> GetOrCreateSet(TKey key)
        {
            if (!TryGetValue(key, out HashSet<TValue> set))
            {
                set = new HashSet<TValue>();
                Add(key, set);
            }

            return set;
        }

        internal bool Add(TKey key, TValue value)
        {
            return GetOrCreateSet(key).Add(value);
        }

        internal bool Remove(TKey key, TValue value)
        {
            if (TryGetValue(key, out HashSet<TValue> set))
            {
                return set.Remove(value);
            }

            return false;
        }

        internal void RemoveAll(TValue value)
        {
            foreach (var set in Values)
            {
                set.Remove(value);
            }
        }

        internal IEnumerable<TValue> Items(TKey key)
        {
            return GetOrCreateSet(key);
        }

        internal TKey[] KeysAsArray()
        {
            var keys = new TKey[Keys.Count];
            Keys.CopyTo(keys, 0);

            return keys;
        }
    }
}
