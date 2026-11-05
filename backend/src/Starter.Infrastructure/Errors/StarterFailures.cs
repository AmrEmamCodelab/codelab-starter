using Codelab.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Starter.Domain.Exceptions;

namespace Starter.Infrastructure.Errors;

/// <summary>
/// The classifiers of the kernel domain exceptions (ERROR-HANDLING rows 1 and 4), appended to the Codelab.Errors
/// table in this order: not found (404), access denied (403), business rule (422 with the exception's own code).
/// </summary>
public static class StarterFailures
{
    /// <summary>Maps the kernel domain exceptions.</summary>
    public static CodelabErrorsOptions AddStarterFailures(this CodelabErrorsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options
            .Map<ResourceNotFoundException>(StatusCodes.Status404NotFound, ErrorCodes.NotFound, LogLevel.Information)
            .Map<AccessDeniedException>(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, LogLevel.Warning)
            .Map<BusinessRuleException>(DescribeBusinessRule);
    }

    private static Failure DescribeBusinessRule(BusinessRuleException exception) =>
        new(
            Status: StatusCodes.Status422UnprocessableEntity,
            Code: exception.Code,
            Detail: FrameworkStatusCodes.Describe(StatusCodes.Status422UnprocessableEntity).Detail,
            Level: LogLevel.Information);
}
