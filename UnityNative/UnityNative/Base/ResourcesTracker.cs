using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Tracks native owners strongly and disposes all of them in registration order.
    /// </summary>
    /// <remarks>
    /// Disposal closes registration, snapshots the ordered registry, and calls owners without holding
    /// the tracker lock. Every owner is attempted even after an earlier failure. Terminal owners are
    /// removed; nonterminal owners remain strongly referenced for an explicit later attempt. Concurrent
    /// callers wait for and observe the same aggregate result, while reentrant disposal on the attempt
    /// thread returns to avoid deadlock.
    /// </remarks>
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

        /// <summary>Registers one owner by reference identity and returns it unchanged.</summary>
        /// <typeparam name="TNativeObject">The disposable owner type.</typeparam>
        /// <param name="obj">The owner to retain until terminal cleanup.</param>
        /// <returns><paramref name="obj"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="obj"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Registration has closed.</exception>
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

        /// <summary>Registers each owner in array order and returns the array unchanged.</summary>
        /// <typeparam name="TNativeObject">The disposable owner type.</typeparam>
        /// <param name="objects">The owners to retain until terminal cleanup.</param>
        /// <returns><paramref name="objects"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="objects"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Registration closes before every owner is registered.</exception>
        public TNativeObject[] T<TNativeObject>(TNativeObject[] objects)
            where TNativeObject : DisposableObject
        {
            if (objects == null)
                throw new ArgumentNullException(nameof(objects));

            foreach (TNativeObject obj in objects)
                T(obj);

            return objects;
        }

        /// <summary>Attempts every retained owner and removes only owners that reach a terminal state.</summary>
        /// <exception cref="AggregateException">One or more owners remain nonterminal after cleanup.</exception>
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
