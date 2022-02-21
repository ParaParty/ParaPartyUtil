using System;
using System.Runtime.CompilerServices;

namespace Paraparty.Kotlinize
{
    /// <summary>
    /// ScopedFunction
    /// </summary>
    public static class ScopedFunction
    {
        /// <summary>
        /// Calls the specified function <paramref name="block">block</paramref> and returns its result.<br/>
        /// 
        /// For detailed usage information see the documentation for <a href="https://kotlinlang.org/docs/reference/scope-functions.html#let">scope functions</a><br/>
        /// </summary>
        /// <param name="block"></param>
        /// <typeparam name="TR"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TR Run<TR>(Func<TR> block)
            => block();

        /// <summary>
        /// Calls the specified function <paramref name="block">block</paramref> with <c>this</c> value as its argument and returns its result.<br/>
        /// 
        /// For detailed usage information see the documentation for <a href="https://kotlinlang.org/docs/reference/scope-functions.html#let">scope functions</a><br/>
        /// </summary>
        /// <param name="self"></param>
        /// <param name="block"></param>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TR"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TR Let<T, TR>(this T self, Func<T, TR> block)
            => block(self);

        /// <summary>
        /// Calls the specified function <paramref name="block">block</paramref> with <c>this</c> value as its argument and returns its result.<br/>
        /// 
        /// For detailed usage information see the documentation for <a href="https://kotlinlang.org/docs/reference/scope-functions.html#let">scope functions</a><br/>
        /// </summary>
        /// <param name="self"></param>
        /// <param name="block"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static void Let<T>(this T self, Action<T> block)
            => block(self);

        /// <summary>
        /// Calls the specified function <paramref name="block">block</paramref> with <c>this</c> value as its argument and returns <c>this</c> value.<br/>
        ///
        /// For detailed usage information see the documentation for <a href="https://kotlinlang.org/docs/reference/scope-functions.html#let">scope functions</a><br/>
        /// </summary>
        /// <param name="self"></param>
        /// <param name="block"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Also<T>(this T self, Action<T> block)
        {
            block(self);
            return self;
        }
    }
}
