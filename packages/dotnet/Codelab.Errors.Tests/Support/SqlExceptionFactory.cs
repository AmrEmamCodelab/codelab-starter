using System.Collections;
using System.Reflection;
using Microsoft.Data.SqlClient;

namespace Codelab.Errors.Tests.Support;

/// <summary>
/// Builds a <see cref="SqlException"/> with a given error number through the driver's non-public factory (the type has
/// no public constructor). A driver update that moves the shape fails these tests loudly rather than skipping them.
/// </summary>
internal static class SqlExceptionFactory
{
    public const int DeadlockVictim = 1205;
    public const int CommandTimeout = -2;
    public const int LoginFailed = 18456;
    public const int PrimaryKeyViolation = 2627;

    private const BindingFlags NonPublic = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string ServerVersion = "16.0";

    public static SqlException Create(int number)
    {
        var errors = (IEnumerable)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection).GetMethod("Add", NonPublic)!.Invoke(errors, [CreateError(number)]);

        var factory = typeof(SqlException).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name == "CreateException")
            .Single(m => m.GetParameters() is [var first, var second] && first.ParameterType == typeof(SqlErrorCollection) && second.ParameterType == typeof(string));
        return (SqlException)factory.Invoke(null, [errors, ServerVersion])!;
    }

    private static SqlError CreateError(int number)
    {
        var constructor = typeof(SqlError).GetConstructors(NonPublic).OrderByDescending(c => c.GetParameters().Length).First();
        var numberAssigned = false;
        var arguments = constructor.GetParameters().Select(parameter =>
        {
            if (parameter.ParameterType == typeof(int) && !numberAssigned)
            {
                numberAssigned = true;
                return (object?)number;
            }

            return DefaultFor(parameter.ParameterType);
        }).ToArray();
        return (SqlError)constructor.Invoke(arguments);
    }

    private static object? DefaultFor(Type type) =>
        type == typeof(string) ? "test" : type.IsValueType ? Activator.CreateInstance(type) : null;
}
