namespace Paraparty.UnityNative.Base.Internal.LifecycleKernel
{
    internal sealed class KernelTransition
    {
        private KernelTransition(KernelTransitionCode code, string detail, object value)
        {
            Code = code;
            Detail = detail;
            Value = value;
        }

        internal bool Accepted => Code == KernelTransitionCode.Accepted;
        internal KernelTransitionCode Code { get; }
        internal string Detail { get; }
        internal object Value { get; }

        internal static KernelTransition Accept(object value = null)
            => new KernelTransition(KernelTransitionCode.Accepted, string.Empty, value);

        internal static KernelTransition Reject(KernelTransitionCode code, string detail)
            => new KernelTransition(code, detail, null);
    }
}
