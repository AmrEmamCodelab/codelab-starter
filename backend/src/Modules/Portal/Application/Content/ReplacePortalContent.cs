using System.Globalization;
using Codelab.Security.PublicIds;
using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Modules.Portal.Domain;

namespace Starter.Modules.Portal.Application.Content;

/// <summary>
/// The body of <c>PUT /api/admin/portal/content/{page}</c>: the page's complete list of sections. A section with an
/// id updates that section, one without an id is added, and a section of the page that the list leaves out is removed.
/// </summary>
/// <param name="Sections">Every section the page should hold (at most <see cref="PortalLimits.MaxSectionsPerPage"/>; may be empty).</param>
public sealed record ReplacePortalContentCommand(IReadOnlyList<SectionInput?>? Sections);

/// <summary>One section of a replace.</summary>
/// <param name="Id">The section's PublicId to update it, or null to add a new section.</param>
/// <param name="TitleAr">The Arabic title (plain text).</param>
/// <param name="TitleEn">The English title (plain text).</param>
/// <param name="BodyAr">The Arabic body (plain text, paragraphs separated by a blank line).</param>
/// <param name="BodyEn">The English body.</param>
/// <param name="SortOrder">The display order, <see cref="PortalLimits.SortOrderMin"/> to <see cref="PortalLimits.SortOrderMax"/>.</param>
public sealed record SectionInput(
    [property: WireId] PublicId<PortalSectionIds>? Id,
    string? TitleAr,
    string? TitleEn,
    string? BodyAr,
    string? BodyEn,
    int? SortOrder)
{
    /// <summary>The trimmed texts and the order (call only after validation).</summary>
    public SectionText ToText() => new(
        TitleAr: TitleAr!.Trim(),
        TitleEn: TitleEn!.Trim(),
        BodyAr: BodyAr!.Trim(),
        BodyEn: BodyEn!.Trim(),
        SortOrder: SortOrder!.Value);
}

/// <summary>
/// Validates <see cref="ReplacePortalContentCommand"/>, every field error at once. Field names are indexed
/// (<c>sections[2].titleEn</c>) so the editor can point at the section.
/// </summary>
public sealed class ReplacePortalContentValidator : IValidator<ReplacePortalContentCommand>
{
    private const string IndexOpen = "[";
    private const string IndexClose = "]";
    private const string MemberSeparator = ".";

    private static readonly string SectionsField = FieldNames.Of(nameof(ReplacePortalContentCommand.Sections));

    /// <inheritdoc />
    public ValidationErrors Validate(ReplacePortalContentCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors();
        if (input.Sections is null)
        {
            return errors.Add(field: SectionsField, message: ValidationMessages.Required);
        }

        if (input.Sections.Count > PortalLimits.MaxSectionsPerPage)
        {
            return errors.Add(field: SectionsField, message: ValidationMessages.TooLong);
        }

        for (var index = 0; index < input.Sections.Count; index++)
        {
            CheckSection(errors, index, input.Sections[index]);
        }

        CheckDuplicateIds(errors, input.Sections);
        return errors;
    }

    /// <summary>The wire name of <paramref name="member"/> of the section at <paramref name="index"/>.</summary>
    public static string SectionField(int index, string member) => string.Concat(SectionItemField(index), MemberSeparator, FieldNames.Of(member));

    /// <summary>The wire name of the section at <paramref name="index"/> itself (<c>sections[2]</c>).</summary>
    public static string SectionItemField(int index) =>
        string.Concat(SectionsField, IndexOpen, index.ToString(CultureInfo.InvariantCulture), IndexClose);

    private static void CheckSection(ValidationErrors errors, int index, SectionInput? section)
    {
        if (section is null)
        {
            errors.Add(field: SectionItemField(index), message: ValidationMessages.Required);
            return;
        }

        errors
            .RequireText(SectionField(index, nameof(SectionInput.TitleAr)), section.TitleAr?.Trim(), PortalLimits.SectionTitleMaxLength)
            .RequireText(SectionField(index, nameof(SectionInput.TitleEn)), section.TitleEn?.Trim(), PortalLimits.SectionTitleMaxLength)
            .RequireText(SectionField(index, nameof(SectionInput.BodyAr)), section.BodyAr?.Trim(), PortalLimits.SectionBodyMaxLength)
            .RequireText(SectionField(index, nameof(SectionInput.BodyEn)), section.BodyEn?.Trim(), PortalLimits.SectionBodyMaxLength);
        var sortField = SectionField(index, nameof(SectionInput.SortOrder));
        if (section.SortOrder is null)
        {
            errors.Add(field: sortField, message: ValidationMessages.Required);
        }
        else if (section.SortOrder is < PortalLimits.SortOrderMin or > PortalLimits.SortOrderMax)
        {
            errors.Add(field: sortField, message: ValidationMessages.InvalidFormat);
        }
    }

    private static void CheckDuplicateIds(ValidationErrors errors, IReadOnlyList<SectionInput?> sections)
    {
        var seen = new HashSet<long>();
        for (var index = 0; index < sections.Count; index++)
        {
            if (sections[index]?.Id is { } id && !seen.Add(id.Value))
            {
                errors.Add(field: SectionField(index, nameof(SectionInput.Id)), message: PortalValidationMessages.Duplicate);
            }
        }
    }
}

/// <summary>
/// <c>PUT /api/admin/portal/content/{page}</c>: replaces the page's sections (update by id, add without id, remove
/// what the list leaves out), stamps the page when anything changed and drops its cached public content. Every
/// added, changed and removed section is recorded by the audit trail (the kernel interceptor).
/// </summary>
public sealed class ReplacePortalContent(
    IValidator<ReplacePortalContentCommand> validator,
    IPortalContentStore store,
    IPortalContentCache cache,
    TimeProvider timeProvider)
{
    /// <summary>
    /// The page as stored, or a refusal: <see cref="PortalErrorCodes.PageNotFound"/> (404),
    /// <see cref="PlatformErrorCodes.Validation"/> (400), or <see cref="PortalErrorCodes.SectionNotOnPage"/> (409) when
    /// an id is not a section of this page (for example one another editor removed).
    /// </summary>
    public async Task<Outcome<PortalContentView>> ExecuteAsync(string? pageKey, ReplacePortalContentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (PortalPages.Find(pageKey) is not { } page)
        {
            return Refusal.NotFound(PortalErrorCodes.PageNotFound);
        }

        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        var inputs = command.Sections!.Select(section => section!).ToList();
        var existing = await store.ListSectionsAsync(page, forUpdate: true, cancellationToken);
        var byId = existing.ToDictionary(section => section.Id);
        if (inputs.Any(input => input.Id is { } id && !byId.ContainsKey(id.Value)))
        {
            return Refusal.Conflict(PortalErrorCodes.SectionNotOnPage);
        }

        var kept = Apply(page, inputs, byId);
        var removed = existing.Where(section => !kept.Contains(section)).ToList();
        removed.ForEach(store.Remove);
        var header = await store.FindPageAsync(page, cancellationToken);
        if (kept.Changed || removed.Count > 0)
        {
            header?.Touch(timeProvider.GetUtcNow());
            await store.SaveChangesAsync(cancellationToken);
            cache.Invalidate(page);
        }

        return PortalContentView.Of(page, header, kept.Sections);
    }

    private SectionChanges Apply(PortalPage page, IReadOnlyList<SectionInput> inputs, Dictionary<long, ContentSection> byId)
    {
        var changes = new SectionChanges();
        foreach (var input in inputs)
        {
            if (input.Id is { } id)
            {
                var section = byId[id.Value];
                changes.Keep(section, changed: section.Edit(input.ToText()));
                continue;
            }

            var added = ContentSection.Create(page, input.ToText());
            store.Add(added);
            changes.Keep(added, changed: true);
        }

        return changes;
    }

    private sealed class SectionChanges
    {
        private readonly List<ContentSection> _sections = [];

        public IReadOnlyList<ContentSection> Sections => _sections;

        public bool Changed { get; private set; }

        public void Keep(ContentSection section, bool changed)
        {
            _sections.Add(section);
            Changed |= changed;
        }

        public bool Contains(ContentSection section) => _sections.Contains(section);
    }
}
