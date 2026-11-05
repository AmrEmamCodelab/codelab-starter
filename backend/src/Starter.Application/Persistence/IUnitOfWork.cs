namespace Starter.Application.Persistence;

/// <summary>
/// The persistence boundary of one module: everything its repositories staged is written in one call. Each module
/// declares its own port deriving from this one (for example <c>IIdentityUnitOfWork</c>), implemented by its persistence context in Infrastructure.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Writes every staged change in one database transaction; returns the number of rows written.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
