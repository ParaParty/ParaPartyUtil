using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Tracks native owners strongly and disposes all of them in registration order.
    /// </summary>
    public sealed class ResourcesTracker : IDisposable
    {
        private sealed class ReferenceComparer : IEqualityComparer<DisposableObject>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            public bool Equals(DisposableObject left, DisposableObject right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(DisposableObject value)
            {
                return RuntimeHelpers.GetHashCode(value);
            }
        }

        private sealed class TrackerAttempt
        {
            public TrackerAttempt(int ownerThreadId)
            {
                OwnerThreadId = ownerThreadId;
            }

            public int OwnerThreadId { get; }

            public bool IsComplete { get; set; }

            public AggregateException Failure { get; set; }
        }

        private readonly object _trackerLock = new object();
        private List<DisposableObject> _trackedObjects = new List<DisposableObject>();
        private HashSet<DisposableObject> _trackedSet =
            new HashSet<DisposableObject>(ReferenceComparer.Instance);

        private bool _trackingClosed;
        private bool _isDisposed;
        private TrackerAttempt _currentAttempt;

        public TNativeObject T<TNativeObject>(TNativeObject obj)
            where TNativeObject : DisposableObject
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            lock (_trackerLock)
            {
                if (_trackingClosed)
                    throw new ObjectDisposedException(GetType().FullName, "Resource tracking is closed.");

                if (_trackedSet.Add(obj))
                    _trackedObjects.Add(obj);
            }

            return obj;
        }

        public TNativeObject[] T<TNativeObject>(TNativeObject[] objects)
            where TNativeObject : DisposableObject
        {
            if (objects == null)
                throw new ArgumentNullException(nameof(objects));

            foreach (TNativeObject obj in objects)
                T(obj);

            return objects;
        }

        public void Dispose()
        {
            TrackerAttempt attempt;
            List<DisposableObject> snapshot;
            var retained = new List<DisposableObject>();
            var retainedSet = new HashSet<DisposableObject>(ReferenceComparer.Instance);
            var failures = new List<Exception>();

            lock (_trackerLock)
            {
                if (_isDisposed)
                    return;

                if (_currentAttempt != null && !_currentAttempt.IsComplete)
                {
                    attempt = _currentAttempt;
                    if (attempt.OwnerThreadId == Thread.CurrentThread.ManagedThreadId)
                        return;

                    while (!attempt.IsComplete)
                        Monitor.Wait(_trackerLock);

                    if (attempt.Failure != null)
                        throw attempt.Failure;
                    return;
                }

                _trackingClosed = true;
                snapshot = new List<DisposableObject>(_trackedObjects);
                attempt = new TrackerAttempt(Thread.CurrentThread.ManagedThreadId);
                _currentAttempt = attempt;
            }

            try
            {
                foreach (DisposableObject obj in snapshot)
                {
                    try
                    {
                        obj.Dispose();
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }

                    if (!obj.IsDisposed && retainedSet.Add(obj))
                        retained.Add(obj);
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                foreach (DisposableObject obj in snapshot)
                {
                    if (!obj.IsDisposed && retainedSet.Add(obj))
                        retained.Add(obj);
                }
            }
            finally
            {
                AggregateException failure = null;

                if (failures.Count == 0 && retained.Count == 0)
                {
                    // The tracker becomes terminal below while holding its lock.
                }
                else
                {
                    if (failures.Count == 0)
                    {
                        failures.Add(new InvalidOperationException(
                            "One or more tracked owners remained non-terminal without reporting a cleanup failure."));
                    }

                    failure = new AggregateException(
                        "One or more tracked resources failed to dispose.",
                        failures);
                }

                lock (_trackerLock)
                {
                    try
                    {
                        _trackedObjects = retained;
                        _trackedSet = retainedSet;
                        _isDisposed = failure == null;
                        attempt.Failure = failure;
                    }
                    finally
                    {
                        attempt.IsComplete = true;
                        Monitor.PulseAll(_trackerLock);
                    }
                }
            }

            if (attempt.Failure != null)
                throw attempt.Failure;
        }
    }
}
