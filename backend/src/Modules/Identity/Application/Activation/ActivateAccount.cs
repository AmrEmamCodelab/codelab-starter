using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Modules.Identity.Application.Accounts;
using Starter.Modules.Identity.Application.Auditing;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application.Activation;

/// <summary>The body of <c>POST /api/auth/activate</c>.</summary>
/// <param name="Ticket">The sealed ticket from the activation link's fragment.</param>
/// <param name="Password">The password the user chooses.</param>
public sealed record ActivateAccountCommand(string? Ticket, string? Password)
{
    /// <summary>A log-safe description: neither the ticket nor the password is printed.</summary>
    public override string ToString() => nameof(ActivateAccountCommand);
}

/// <summary>
/// Validates <see cref="ActivateAccountCommand"/>: a ticket within its limit and a password that meets
/// <see cref="PasswordPolicy"/> (and is at most <see cref="IdentityLimits.PasswordMaxLength"/>, the longest a login accepts).
/// </summary>
public sealed class ActivateAccountCommandValidator(PasswordPolicy policy) : IValidator<ActivateAccountCommand>
{
    /// <inheritdoc />
    public ValidationErrors Validate(ActivateAccountCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors()
            .RequireText(FieldNames.Of(nameof(ActivateAccountCommand.Ticket)), input.Ticket, IdentityLimits.ActivationTicketMaxLength)
            .RequireText(FieldNames.Of(nameof(ActivateAccountCommand.Password)), input.Password, IdentityLimits.PasswordMaxLength);
        if (string.IsNullOrWhiteSpace(input.Password) || input.Password.Length > IdentityLimits.PasswordMaxLength)
        {
            return errors;
        }

        var field = FieldNames.Of(nameof(ActivateAccountCommand.Password));
        if (input.Password.Length < policy.MinLength)
        {
            errors.Add(field, IdentityValidationMessages.PasswordTooShort);
        }

        if (input.Password.Distinct().Count() < policy.MinDistinctCharacters)
        {
            errors.Add(field, IdentityValidationMessages.PasswordTooSimple);
        }

        return errors;
    }
}

/// <summary>
/// <c>POST /api/auth/activate</c>: redeems an activation link and sets the account's password. The ticket opens once
/// (the Codelab.Security replay guard), must be the account's one pending link (a newer link supersedes it), and the
/// account must still be active and password bound. Success ends every older session of the user (session version
/// bump) and records an auth event; every refusal is the same <see cref="IdentityErrorCodes.ActivationRejected"/>.
/// </summary>
public sealed class ActivateAccountUseCase(
    IValidator<ActivateAccountCommand> validator,
    IActivationTickets tickets,
    IUserAccountRepository accounts,
    AccountWrites writes,
    IAuthEventSink audit)
{
    /// <summary>Done, or <see cref="PlatformErrorCodes.Validation"/> / <see cref="IdentityErrorCodes.ActivationRejected"/>.</summary>
    public async Task<Outcome<Done>> ExecuteAsync(ActivateAccountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        var opened = await tickets.OpenAsync(command.Ticket!, cancellationToken);
        var account = opened is null ? null : await accounts.FindBySubjectAsync(opened.SubjectId, cancellationToken);
        if (opened is null || account is null || !account.CanRedeemActivation(opened.TicketId))
        {
            await audit.RecordAsync(
                new AuthEventRecord(AuthEventType.ActivationRejected, UserId: account?.Id, Reason: AuthFailureReason.ActivationTicketRejected),
                cancellationToken);
            return Refusal.Invalid(IdentityErrorCodes.ActivationRejected);
        }

        account.CompleteActivation(writes.HashPassword(command.Password!), writes.Now());
        await writes.SaveAsync(account, SessionImpact.EndSessions, cancellationToken);
        await audit.RecordAsync(new AuthEventRecord(AuthEventType.AccountActivated, UserId: account.Id), cancellationToken);
        return Done.Value;
    }
}
