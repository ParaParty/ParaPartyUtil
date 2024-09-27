using System;
using System.Collections.Generic;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Used for managing the unmanaged resourcesc.
    /// </summary>
    public sealed class ResourcesTracker : IDisposable
    {
        private readonly ISet<DisposableObject> _trackedObjects = new HashSet<DisposableObject>();
        private readonly object _asyncLock = new object();

        /// <summary>
        /// Trace the object obj, and return it
        /// </summary>
        /// <typeparam name="TNativeObject"></typeparam>
        /// <param name="obj"></param>
        /// <returns></returns>
        public TNativeObject T<TNativeObject>(TNativeObject obj)
            where TNativeObject : DisposableObject
        {
            if (obj is null)
                throw new ArgumentNullException(nameof(obj));

            lock (_asyncLock)
            {
                _trackedObjects.Add(obj);
            }

            return obj;
        }

        /// <summary>
        /// Trace an array of objects , and return them
        /// </summary>
        /// <typeparam name="TNativeObject"></typeparam>
        /// <param name="objects"></param>
        /// <returns></returns>
        public TNativeObject[] T<TNativeObject>(TNativeObject[] objects)
            where TNativeObject : DisposableObject
        {
            if (objects is null)
                throw new ArgumentNullException(nameof(objects));

            foreach (var obj in objects)
            {
                T(obj);
            }

            return objects;
        }

        /// <summary>
        /// Dispose all traced objects
        /// </summary>
        public void Dispose()
        {
            lock (_asyncLock)
            {
                foreach (var obj in _trackedObjects)
                {
                    if (obj.IsDisposed == false)
                    {
                        obj.Dispose();
                    }
                }

                _trackedObjects.Clear();
            }
        }
    }
}
