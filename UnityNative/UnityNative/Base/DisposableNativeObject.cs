using System;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// DisposableObject + INativePtrHolder
    /// </summary>
    public abstract class DisposableNativeObject : DisposableObject, INativePtrHolder
    {
        /// <summary>
        /// Data pointer
        /// </summary>
        protected IntPtr ptr;

        /// <summary>
        /// Default constructor
        /// </summary>
        protected DisposableNativeObject()
            : this(true)
        {
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="ptr"></param>
        protected DisposableNativeObject(IntPtr ptr)
            : this(ptr, true)
        {
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="isEnabledDispose"></param>
        protected DisposableNativeObject(bool isEnabledDispose)
            : this(IntPtr.Zero, isEnabledDispose)
        {
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="ptr"></param>
        /// <param name="isEnabledDispose"></param>
        protected DisposableNativeObject(IntPtr ptr, bool isEnabledDispose)
            : base(isEnabledDispose)
        {
            this.ptr = ptr;
        }

        /// <summary>
        /// releases unmanaged resources
        /// </summary>
        protected override void DisposeUnmanaged()
        {
            ptr = IntPtr.Zero;
            base.DisposeUnmanaged();
        }

        /// <summary>
        /// Native pointer
        /// </summary>
        public IntPtr NativePtr
        {
            get
            {
                ThrowIfDisposed();
                return ptr;
            }
        }
    }
}
