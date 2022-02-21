using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Paraparty.Kotlinize
{
    /// <summary>
    /// Collections Utils
    /// </summary>
    public static class Collections
    {
        /// <summary>
        /// Create a list by given item. <br/>
        ///
        /// This function is equivalent to linq expression <code>items.ToList()</code> .
        /// </summary>
        /// <param name="items"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static List<T> ListOf<T>(params T[] items)
        {
            var ret = new List<T>();
            ret.AddRange(items);
            return ret;
        }

        /// <summary>
        /// Create a KeyValuePair by given key and value. <br/>
        ///
        /// This function is equivalent to linq expression <code>new KeyValuePair&lt;TKey, TValue&gt;(key, value)</code> .
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static KeyValuePair<TKey, TValue> To<TKey, TValue>(this TKey key, TValue value)
            => new KeyValuePair<TKey, TValue>(key, value);

        /// <summary>
        /// Create a KeyValuePair by given key and value. <br/>
        ///
        /// This function is equivalent to linq expression <code>new KeyValuePair&lt;TKey, TValue&gt;(key, value)</code> .
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static KeyValuePair<TKey, TValue> PairOf<TKey, TValue>(TKey key, TValue value)
            => new KeyValuePair<TKey, TValue>(key, value);

        /// <summary>
        /// Create a Dictionary by given items. <br/>
        ///
        /// This function is equivalent to linq expression <code>items.ToDictionary(s => s.Key, s => s.Item)</code> .
        /// </summary>
        /// <param name="items"></param>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dictionary<TKey, TValue> MapOf<TKey, TValue>(params KeyValuePair<TKey, TValue>[] items)
        {
            var ret = new Dictionary<TKey, TValue>();
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var item in items)
            {
                ret.Add(item.Key, item.Value);
            }
            return ret;
        }

        /// <summary>
        /// Create a Set by given items. <br/>
        /// </summary>
        /// <param name="items"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ISet<T> SetOf<T>(params T[] items)
        {
            var ret = new HashSet<T>();
            foreach (var item in items)
            {
                ret.Add(item);
            }
            return ret;
        }
    }
}
