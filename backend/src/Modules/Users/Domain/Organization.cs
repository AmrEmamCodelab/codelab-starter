namespace Starter.Modules.Users.Domain;

/// <summary>
/// An organization users belong to: a unit of the portal's own organization (internal) or an outside entity
/// (external). The code is unique (normalized by <see cref="OrganizationCodes"/>). A deactivated organization keeps its
/// users but accepts no new member.
/// </summary>
public sealed class Organization
{
    private Organization()
    {
    }

    /// <summary>The internal id (the wire carries its PublicId).</summary>
    public long Id { get; private set; }

    /// <summary>The unique code, normalized (trimmed, upper-case).</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The English name.</summary>
    public string NameEn { get; private set; } = string.Empty;

    /// <summary>The Arabic name.</summary>
    public string NameAr { get; private set; } = string.Empty;

    /// <summary>Internal or external (stored as the integer value).</summary>
    public OrganizationKind Kind { get; private set; }

    /// <summary>False once deactivated.</summary>
    public bool IsActive { get; private set; }

    /// <summary>When it was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When it last changed, or null when never.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>A new, active organization. The caller has validated the values (see the Application validators).</summary>
    public static Organization Create(OrganizationDetails details, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        var organization = new Organization { IsActive = true, CreatedAt = now };
        organization.Apply(details);
        return organization;
    }

    /// <summary>Replaces the editable values.</summary>
    public void Change(OrganizationDetails details, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        Apply(details);
        UpdatedAt = now;
    }

    /// <summary>Activates or deactivates; true when the state changed.</summary>
    public bool SetActive(bool isActive, DateTimeOffset now)
    {
        if (IsActive == isActive)
        {
            return false;
        }

        IsActive = isActive;
        UpdatedAt = now;
        return true;
    }

    /// <summary>The name in Arabic when <paramref name="arabic"/> is true, else in English.</summary>
    public string NameIn(bool arabic) => arabic ? NameAr : NameEn;

    private void Apply(OrganizationDetails details)
    {
        Code = OrganizationCodes.Normalize(details.Code);
        NameEn = details.NameEn.Trim();
        NameAr = details.NameAr.Trim();
        Kind = details.Kind;
    }
}

/// <summary>The editable values of an organization.</summary>
/// <param name="Code">The code (normalized on write).</param>
/// <param name="NameEn">The English name.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="Kind">Internal or external.</param>
public sealed record OrganizationDetails(string Code, string NameEn, string NameAr, OrganizationKind Kind);
