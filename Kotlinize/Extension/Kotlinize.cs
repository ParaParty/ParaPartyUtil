using System;
using System.Runtime.CompilerServices;

namespace Paraparty.Kotlinize.Extension
{
    public static class Kotlinize
    {
        /// <summary>
        /// Calls the specified function <see cref="block">block</see> and returns its result.<br/>
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
        /// Calls the specified function <see cref="block">block</see> with <c>this</c> value as its argument and returns its result.<br/>
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
        /// Calls the specified function <see cref="block">block</see> with <c>this</c> value as its argument and returns <c>this</c> value.<br/>
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
