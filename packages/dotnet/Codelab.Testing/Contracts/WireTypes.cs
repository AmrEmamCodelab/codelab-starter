using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace Codelab.Testing.Contracts;

/// <summary>Type helpers for the wire contracts: id-shaped member names and unwrapping of results and collections.</summary>
public static class WireTypes
{
    private const string IdName = "Id";
    private const string IdsSuffix = "Ids";

    /// <summary>True for a member named <c>Id</c> or ending in <c>Id</c> / <c>Ids</c> (PascalCase, as declared in C#).</summary>
    public static bool IsIdName(string memberName)
    {
        ArgumentNullException.ThrowIfNull(memberName);
        return memberName.EndsWith(IdName, StringComparison.Ordinal) || memberName.EndsWith(IdsSuffix, StringComparison.Ordinal);
    }

    /// <summary>
    /// The element type of a value: <c>T?</c>, <c>T[]</c> and <c>IEnumerable&lt;T&gt;</c> give <c>T</c> (strings are not
    /// collections); anything else is returned as is.
    /// </summary>
    public static Type ElementOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
        {
            return ElementOf(nullable);
        }

        if (type.IsArray)
        {
            return ElementOf(type.GetElementType()!);
        }

        var enumerable = type == typeof(string) ? null : EnumerableArgument(type);
        return enumerable is null ? type : ElementOf(enumerable);
    }

    /// <summary>
    /// The body type an action returns: <c>Task&lt;T&gt;</c>, <c>ValueTask&lt;T&gt;</c> and <c>ActionResult&lt;T&gt;</c>
    /// give <c>T</c>; <c>Task</c>, <c>IActionResult</c> and <c>IResult</c> give null (no declared body).
    /// </summary>
    public static Type? UnwrapResult(Type returnType)
    {
        ArgumentNullException.ThrowIfNull(returnType);
        if (returnType.IsGenericType)
        {
            var definition = returnType.GetGenericTypeDefinition();
            if (definition == typeof(Task<>) || definition == typeof(ValueTask<>) || definition == typeof(ActionResult<>))
            {
                return UnwrapResult(returnType.GetGenericArguments()[0]);
            }
        }

        var isUntyped = returnType == typeof(Task) || returnType == typeof(ValueTask)
            || typeof(IActionResult).IsAssignableFrom(returnType) || typeof(Microsoft.AspNetCore.Http.IResult).IsAssignableFrom(returnType);
        return isUntyped ? null : returnType;
    }

    /// <summary>The public instance properties of a type, as serialized.</summary>
    internal static IEnumerable<PropertyInfo> WireProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.GetIndexParameters().Length == 0);

    private static Type? EnumerableArgument(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return type.GetGenericArguments()[0];
        }

        return type.GetInterfaces()
            .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }
}
