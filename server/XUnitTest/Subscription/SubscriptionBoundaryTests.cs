using System.Reflection;
using FluentAssertions;
using Payment.DomainService.Entities;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// Subscriptions depend on payments; payments must never depend on subscriptions.
/// </summary>
/// <remarks>
/// The direction is what keeps the payment module able to change. Once payment code reaches
/// back into subscriptions the two stop being separable, and a cycle between two projects in
/// one solution is caught by the compiler only when the reference is added — not when a type
/// quietly starts flowing the wrong way through an interface both already share.
/// </remarks>
public sealed class SubscriptionBoundaryTests
{
    private const string SubscriptionRoot = "Subscription.DomainService";

    private static readonly Assembly PaymentAssembly =
        typeof(PaymentDetail).Assembly;

    private static readonly Assembly SubscriptionAssembly =
        typeof(SubscriptionOptions).Assembly;

    [Fact]
    public void The_two_modules_are_separate_assemblies()
    {
        SubscriptionAssembly.Should().NotBeSameAs(PaymentAssembly);
    }

    [Fact]
    public void No_payment_type_names_a_subscription_type_in_its_signatures()
    {
        var offenders = PaymentAssembly
            .GetTypes()
            .SelectMany(SignatureTypesOf)
            .Where(IsSubscriptionType)
            .Select(type => type.FullName!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty(
            "payment must not depend on subscriptions; every collaboration in this codebase " +
            "is constructor-injected, so a type appearing in a signature is a real dependency");
    }

    [Fact]
    public void The_payment_assembly_does_not_reference_the_subscription_assembly()
    {
        var referenced = PaymentAssembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .ToArray();

        referenced.Should().NotContain(SubscriptionRoot);
    }

    private static IEnumerable<Type> SignatureTypesOf(Type type)
    {
        const BindingFlags members =
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.Instance |
            BindingFlags.Static |
            BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(members))
        {
            yield return field.FieldType;
        }

        foreach (var property in type.GetProperties(members))
        {
            yield return property.PropertyType;
        }

        foreach (var constructor in type.GetConstructors(members))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var method in type.GetMethods(members))
        {
            yield return method.ReturnType;

            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }

    private static bool IsSubscriptionType(Type type)
    {
        var candidate = type.IsByRef || type.IsArray || type.IsPointer
            ? type.GetElementType() ?? type
            : type;

        if (candidate.IsGenericType)
        {
            return candidate
                .GetGenericArguments()
                .Append(candidate.GetGenericTypeDefinition())
                .Any(NamespaceIsSubscription);
        }

        return NamespaceIsSubscription(candidate);
    }

    private static bool NamespaceIsSubscription(Type type) =>
        type.Namespace is not null &&
        (type.Namespace.Equals(SubscriptionRoot, StringComparison.Ordinal) ||
         type.Namespace.StartsWith($"{SubscriptionRoot}.", StringComparison.Ordinal));
}
