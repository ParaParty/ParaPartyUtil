using System;
using System.Threading;

namespace Paraparty.UnityNative.Base
{
    internal static class NativeOperationExecutionContext
    {
        private static readonly AsyncLocal<NativeOperationExecutionMarker[]> CurrentMarkers =
            new AsyncLocal<NativeOperationExecutionMarker[]>();

        public static void Enter(NativeOperationExecutionMarker marker)
        {
            if (marker == null)
                throw new ArgumentNullException(nameof(marker));

            Attach(marker);
        }

        public static void Attach(NativeOperationExecutionMarker marker)
        {
            if (marker == null)
                throw new ArgumentNullException(nameof(marker));
            if (!marker.IsActive)
                return;

            NativeOperationExecutionMarker[] markers = Compact(CurrentMarkers.Value);
            for (int i = 0; i < markers.Length; i++)
            {
                if (ReferenceEquals(markers[i], marker))
                {
                    CurrentMarkers.Value = markers;
                    return;
                }
            }

            var next = new NativeOperationExecutionMarker[markers.Length + 1];
            Array.Copy(markers, next, markers.Length);
            next[markers.Length] = marker;
            CurrentMarkers.Value = next;
        }

        public static bool ContainsOwner(long ownerId)
        {
            NativeOperationExecutionMarker[] markers = Compact(CurrentMarkers.Value);
            CurrentMarkers.Value = markers.Length == 0 ? null : markers;

            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i].OwnerId == ownerId)
                    return true;
            }

            return false;
        }

        public static bool ContainsOwner(long ownerId, NativeOperationExecutionKind kind)
        {
            NativeOperationExecutionMarker[] markers = Compact(CurrentMarkers.Value);
            CurrentMarkers.Value = markers.Length == 0 ? null : markers;

            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i].OwnerId == ownerId && markers[i].Kind == kind)
                    return true;
            }

            return false;
        }

        public static void Exit(NativeOperationExecutionMarker marker)
        {
            if (marker == null)
                return;

            marker.Deactivate();
            NativeOperationExecutionMarker[] markers = Compact(CurrentMarkers.Value);
            CurrentMarkers.Value = markers.Length == 0 ? null : markers;
        }

        private static NativeOperationExecutionMarker[] Compact(
            NativeOperationExecutionMarker[] markers)
        {
            if (markers == null || markers.Length == 0)
                return Array.Empty<NativeOperationExecutionMarker>();

            int activeCount = 0;
            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i] != null && markers[i].IsActive)
                    activeCount++;
            }

            if (activeCount == markers.Length)
                return markers;
            if (activeCount == 0)
                return Array.Empty<NativeOperationExecutionMarker>();

            var activeMarkers = new NativeOperationExecutionMarker[activeCount];
            int destination = 0;
            for (int i = 0; i < markers.Length; i++)
            {
                NativeOperationExecutionMarker marker = markers[i];
                if (marker != null && marker.IsActive)
                    activeMarkers[destination++] = marker;
            }

            return activeMarkers;
        }
    }
}
