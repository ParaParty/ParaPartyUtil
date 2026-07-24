using System;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Staged cleanup base for a wrapper around a native pointer.
    /// </summary>
    public abstract class DisposableNativeObject : DisposableObject, INativePtrHolder
    {
        protected IntPtr ptr;

        protected DisposableNativeObject()
            : this(IntPtr.Zero, NativeOwnershipKind.Owned)
        {
        }

        protected DisposableNativeObject(IntPtr ptr)
            : this(ptr, NativeOwnershipKind.Owned)
        {
        }

        protected DisposableNativeObject(NativeOwnershipKind ownership)
            : this(IntPtr.Zero, ownership)
        {
        }

        protected DisposableNativeObject(IntPtr ptr, NativeOwnershipKind ownership)
            : base(ownership)
        {
            this.ptr = ptr;
        }

        public IntPtr NativePtr
        {
            get
            {
                ThrowIfDisposed();
                return ptr;
            }
        }

        protected sealed override NativeCleanupResult CleanupNativeResource()
        {
            return CleanupNativeResource(ptr);
        }

        protected abstract NativeCleanupResult CleanupNativeResource(IntPtr nativePointer);

        protected override CleanupStageResult UnpublishOwner()
        {
            ptr = IntPtr.Zero;
            return CleanupStageResult.Succeeded();
        }
    }
}
