using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Paraparty.UnityPolyfill
{
    public static class CollectionsPolyfill
    {
#if !NET_STANDARD_2_1
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryPop<T>(this Stack<T> self, /* [MaybeNullWhen(false)] */ out T result)
        {
            if (self.Count > 0)
            {
                result = self.Pop();
                return true;
            }
            else
            {
                result = default;
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> self, TKey key, TValue value)
        {
            if (self.ContainsKey(key))
            {
                return false;
            }
            
            self.Add(key, value);
            return true;
        }
#endif
    }
}
