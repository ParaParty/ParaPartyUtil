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
    private readonly object _registry;
    private readonly object _adapter;

    private LifecycleKernelHarness(object kernel, object registry = null)
    {
        _kernel = kernel;
        _kernelType = kernel.GetType();
        _registry = registry;
        _adapter = Activator.CreateInstance(
            RequiredType("DormantLifecycleKernelAdapter"),
            InstanceFlags,
            binder: null,
            args: new[] { kernel },
            culture: null);
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

    internal static LifecycleKernelHarness NewWithRegistry(
        int capacity = 8,
        bool forcePrimaryPublishFailure = false,
        string ownership = "Owned",
        bool published = true)
    {
        var kernelType = RequiredType("LifecycleKernel");
        var registryType = RequiredType("LifecycleOrphanRegistry");
        var registry = Activator.CreateInstance(
            registryType,
            InstanceFlags,
            binder: null,
            args: new object[] { capacity, forcePrimaryPublishFailure },
            culture: null);
        var owner = Enum.Parse(RequiredType("KernelOwnership"), ownership);
        var kernel = Activator.CreateInstance(
            kernelType,
            InstanceFlags,
            binder: null,
            args: new[] { owner, (object)published, registry },
            culture: null);
        Assert.IsNotNull(kernel);
        return new LifecycleKernelHarness(kernel, registry);
    }

    internal static object[] CreateFinalizedOwnerWithRegistry(bool forcePrimaryPublishFailure = false)
    {
        var harness = NewWithRegistry(forcePrimaryPublishFailure: forcePrimaryPublishFailure);
        var registry = harness._registry;
        var weak = new WeakReference(harness._kernel);
        harness.Accept("FinalizeWrapper");
        return new object[] { registry, weak };
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

    internal object ReservationReceipt(object transition)
        => Property(Value(transition), "Receipt");

    internal object ReservationCapability(object transition)
        => Property(Value(transition), "Capability");

    internal WeakReference WeakKernel() => new WeakReference(_kernel);

    internal object RegistryCall(string method, params object[] args)
    {
        Assert.IsNotNull(_registry, "This harness does not expose a custom orphan registry.");
        var candidates = _registry.GetType().GetMethods(InstanceFlags)
            .Where(candidate => candidate.Name == method && candidate.GetParameters().Length == args.Length)
            .ToArray();
        Assert.AreEqual(1, candidates.Length, $"Expected one orphan-registry method {method}/{args.Length}.");
        return candidates[0].Invoke(_registry, args);
    }

    internal long RegistryCount()
        => Convert.ToInt64(Property(_registry, "Count"));

    internal bool RegistryFlag(string property)
        => Convert.ToBoolean(Property(_registry, property));

    internal object RegistrySnapshot()
        => RegistryCall("Snapshot");

    internal object AdapterCall(string method, params object[] args)
        => CallOn(_adapter, method, args);

    internal object AdapterTransition(object request)
        => Property(request, "Transition");

    internal object AdapterReceipt(object request)
        => Property(request, "Receipt");

    internal object AdapterCapability(object request)
        => Property(request, "Capability");

    internal object AcceptAdapter(object request)
    {
        var transition = AdapterTransition(request);
        Assert.IsTrue(Bool(transition, "Accepted"),
            $"Adapter rejected: {Text(transition, "Code")} {Text(transition, "Detail")}");
        return transition;
    }

    internal object RejectAdapter(string expectedCode, object request)
    {
        var transition = AdapterTransition(request);
        Assert.IsFalse(Bool(transition, "Accepted"), "Adapter unexpectedly accepted.");
        Assert.AreEqual(expectedCode, Text(transition, "Code"));
        return transition;
    }

    internal IDisposable EnterAdapterScope(object request)
        => (IDisposable)CallOn(request, "EnterScope");

    internal string ObserveAdapterRequest(object request)
        => (string)CallOn(request, "Observe");

    internal object CreateCalloutCapability(string kind)
        => AdapterCall("CreateCalloutCapability", EnumValue("KernelCalloutKind", kind));

    internal IDisposable EnterCallout(string kind)
        => (IDisposable)AdapterCall("EnterCallout", EnumValue("KernelCalloutKind", kind));

    internal static object TaskResult(object task)
        => Property(task, "Result");

    internal static object CallOn(object target, string method, params object[] args)
    {
        var candidates = target.GetType().GetMethods(InstanceFlags)
            .Where(candidate => candidate.Name == method && ParametersMatch(candidate, args))
            .ToArray();
        Assert.AreEqual(1, candidates.Length, $"Expected one method {method}/{args.Length}.");
        return candidates[0].Invoke(target, args);
    }

    internal static object Identity(long value)
        => Activator.CreateInstance(
            RequiredType("KernelIdentity"),
            InstanceFlags,
            binder: null,
            args: new object[] { value },
            culture: null);

    internal string State(string property)
        => Text(Call("Snapshot"), property);

    internal long Number(string property)
        => Convert.ToInt64(Property(Call("Snapshot"), property));

    internal bool Flag(string property)
        => Bool(Call("Snapshot"), property);

    internal string SnapshotFingerprint()
    {
        var snapshot = Call("Snapshot");
        return string.Join(
            "|",
            snapshot.GetType().GetProperties(InstanceFlags)
                .OrderBy(property => property.Name)
                .Select(property => property.Name + "=" + property.GetValue(snapshot)));
    }

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

    internal object CompleteDisposal()
    {
        var dispose = ReservationReceipt(Accept("StartDispose", null, null));
        return CompleteStartedDisposal(dispose);
    }

    internal object CompleteStartedDisposal(object dispose)
    {
        if (State("Admission") == "Closing") Accept("CloseAdmission", dispose);
        var managed = Value(Accept("BeginStage", EnumValue("KernelStage", "Managed"), dispose));
        Accept("CompleteManaged", managed);
        var quiesce = Value(Accept("BeginStage", EnumValue("KernelStage", "NativeQuiesce"), dispose));
        var quiescence = Value(Accept("RecordQuiescence", quiesce));
        var callback = Value(Accept("BeginStage", EnumValue("KernelStage", "CallbackFence"), dispose));
        Accept("CompleteCallbackFence", callback);
        var native = Value(Accept("BeginStage", EnumValue("KernelStage", "NativeAuthority"), dispose));
        Accept("CommitNativeAuthority", native, quiescence);
        var legacy = Value(Accept(
            "BeginStage", EnumValue("KernelStage", "LegacyUnpublishNotification"), dispose));
        Accept("CompleteLegacyNotification", legacy);
        var resources = Value(Accept("BeginStage", EnumValue("KernelStage", "BaseResources"), dispose));
        Accept("CompleteBaseResources", resources);
        return Accept("FinishDispose", dispose);
    }

    internal object BeginAndCommitTransfer()
    {
        var preparation = ReservationReceipt(Accept("BeginTransfer", null, null));
        return Value(Accept("CommitTransfer", preparation));
    }

    internal object[] BeginTransferPreparation()
    {
        var transition = Accept("BeginTransfer", null, null);
        return new[] { ReservationReceipt(transition), ReservationCapability(transition) };
    }

    internal object[] StartRollbackAttempt(object ticket)
    {
        var transition = Accept("StartRollback", ticket, null, null);
        return new[] { ReservationReceipt(transition), ReservationCapability(transition) };
    }

    internal object[] ReserveDisposalStage(string stage)
    {
        var dispose = ReservationReceipt(Accept("StartDispose", null, null));
        if (State("Admission") == "Closing") Accept("CloseAdmission", dispose);
        var order = new[]
        {
            "Managed", "NativeQuiesce", "CallbackFence", "NativeAuthority",
            "LegacyUnpublishNotification", "BaseResources"
        };
        object quiescence = null;
        for (var index = 0; index < order.Length; index++)
        {
            var current = order[index];
            if (current == stage)
            {
                var reservation = Value(Accept("BeginStage", EnumValue("KernelStage", current), dispose));
                return new[] { dispose, reservation, quiescence };
            }
            if (Number("CompletedStageCount") > index) continue;
            var prior = Value(Accept("BeginStage", EnumValue("KernelStage", current), dispose));
            switch (current)
            {
                case "Managed":
                    Accept("CompleteManaged", prior);
                    break;
                case "NativeQuiesce":
                    quiescence = Value(Accept("RecordQuiescence", prior));
                    break;
                case "CallbackFence":
                    Accept("CompleteCallbackFence", prior);
                    break;
                case "NativeAuthority":
                    Accept("CommitNativeAuthority", prior, quiescence);
                    break;
                case "LegacyUnpublishNotification":
                    Accept("CompleteLegacyNotification", prior);
                    break;
            }
        }
        Assert.Fail("Unknown cleanup stage " + stage);
        return null;
    }

    private static Type RequiredType(string name)
    {
        var type = Assembly.GetType(Namespace + name, throwOnError: false);
        Assert.IsNotNull(type, $"Missing dormant kernel type {name}.");
        return type;
    }

    private static bool ParametersMatch(MethodInfo method, object[] args)
    {
        var parameters = method.GetParameters();
        if (parameters.Length != args.Length) return false;
        for (var index = 0; index < parameters.Length; index++)
        {
            if (args[index] == null)
            {
                if (parameters[index].ParameterType.IsValueType) return false;
            }
            else if (!parameters[index].ParameterType.IsInstanceOfType(args[index]))
            {
                return false;
            }
        }
        return true;
    }
}
