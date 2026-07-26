using System;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Paraparty.UnityNative.Base;

namespace Paraparty.Tests.UnityNative;

internal sealed class LifecycleKernelHarness
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Assembly Assembly = typeof(DisposableNativeObject).Assembly;
    private static readonly string Namespace =
        "Paraparty.UnityNative.Base.Internal.LifecycleKernel.";

    private readonly object _kernel;
    private readonly Type _kernelType;

    private LifecycleKernelHarness(object kernel)
    {
        _kernel = kernel;
        _kernelType = kernel.GetType();
    }

    internal static LifecycleKernelHarness New(string ownership = "Owned", bool published = true)
    {
        var kernelType = RequiredType("LifecycleKernel");
        var owner = Enum.Parse(RequiredType("KernelOwnership"), ownership);
        var kernel = Activator.CreateInstance(
            kernelType,
            InstanceFlags,
            binder: null,
            args: new[] { owner, (object)published },
            culture: null);
        Assert.IsNotNull(kernel, "Dormant LifecycleKernel must be directly constructible by the harness.");
        return new LifecycleKernelHarness(kernel);
    }

    internal object EnumValue(string type, string value)
        => Enum.Parse(RequiredType(type), value);

    internal object Call(string method, params object[] args)
    {
        var candidates = _kernelType.GetMethods(InstanceFlags)
            .Where(candidate => candidate.Name == method && candidate.GetParameters().Length == args.Length)
            .ToArray();
        Assert.AreEqual(1, candidates.Length, $"Expected one internal method {method}/{args.Length}.");
        try
        {
            return candidates[0].Invoke(_kernel, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            throw exception.InnerException;
        }
    }

    internal object Accept(string method, params object[] args)
    {
        var transition = Call(method, args);
        Assert.IsTrue(Bool(transition, "Accepted"), $"{method} rejected: {Text(transition, "Code")} {Text(transition, "Detail")}");
        return transition;
    }

    internal object Reject(string expectedCode, string method, params object[] args)
    {
        var transition = Call(method, args);
        Assert.IsFalse(Bool(transition, "Accepted"), $"{method} unexpectedly accepted.");
        Assert.AreEqual(expectedCode, Text(transition, "Code"), $"Unexpected rejection from {method}.");
        return transition;
    }

    internal object Value(object transition) => Property(transition, "Value");

    internal string State(string property)
        => Text(Call("Snapshot"), property);

    internal long Number(string property)
        => Convert.ToInt64(Property(Call("Snapshot"), property));

    internal bool Flag(string property)
        => Bool(Call("Snapshot"), property);

    internal void AssertValid()
    {
        var errors = (string[])Call("InvariantErrors");
        Assert.AreEqual(0, errors.Length, string.Join(" | ", errors));
    }

    internal static object Property(object target, string property)
    {
        Assert.IsNotNull(target);
        var info = target.GetType().GetProperty(property, InstanceFlags);
        Assert.IsNotNull(info, $"Missing property {target.GetType().FullName}.{property}.");
        return info.GetValue(target);
    }

    internal static string Text(object target, string property)
        => Property(target, property)?.ToString();

    internal static bool Bool(object target, string property)
        => Convert.ToBoolean(Property(target, property));

    internal static long NestedNumber(object target, params string[] properties)
    {
        object value = target;
        foreach (var property in properties)
            value = Property(value, property);
        return Convert.ToInt64(value);
    }

    internal object CompleteDisposal(string causalToken = "dispose")
    {
        Accept("StartDispose", causalToken);
        if (State("Admission") == "Closing") Accept("CloseAdmission");
        Accept("BeginStage", EnumValue("KernelStage", "Managed"));
        Accept("CompleteManaged");
        Accept("BeginStage", EnumValue("KernelStage", "NativeQuiesce"));
        var quiescence = Value(Accept("RecordQuiescence"));
        Accept("BeginStage", EnumValue("KernelStage", "CallbackFence"));
        Accept("CompleteCallbackFence");
        Accept("BeginStage", EnumValue("KernelStage", "NativeAuthority"));
        Accept("CommitNativeAuthority", quiescence);
        Accept("BeginStage", EnumValue("KernelStage", "LegacyUnpublishNotification"));
        Accept("CompleteLegacyNotification");
        Accept("BeginStage", EnumValue("KernelStage", "BaseResources"));
        Accept("CompleteBaseResources");
        return Accept("FinishDispose");
    }

    internal object BeginAndCommitTransfer(string causalToken = "transfer")
    {
        var preparation = Value(Accept("BeginTransfer", causalToken));
        return Accept("CommitTransfer", preparation);
    }

    private static Type RequiredType(string name)
    {
        var type = Assembly.GetType(Namespace + name, throwOnError: false);
        Assert.IsNotNull(type, $"Missing dormant kernel type {name}.");
        return type;
    }
}
