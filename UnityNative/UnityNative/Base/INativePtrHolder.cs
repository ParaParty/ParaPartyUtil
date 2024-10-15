using System;

namespace Paraparty.UnityNative.Base
{
    public interface INativePtrHolder
    {
        IntPtr NativePtr { get; }
    }
}
