using System;
using System.Collections.Generic;
using System.Linq;

namespace ChaseNet2.Extensions
{
    public static class DictionaryExtensions
    {
        public static void RemoveAll<K, V>(this IDictionary<K, V> dict, Func<K, V, bool> match)
        {
            foreach (var key in dict.Keys.ToArray()
                         .Where(key => match(key, dict[key])))
                dict.Remove(key);
        }
    }
}