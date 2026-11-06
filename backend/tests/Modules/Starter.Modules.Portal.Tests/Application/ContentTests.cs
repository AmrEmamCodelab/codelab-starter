using Codelab.Security.PublicIds;
using Microsoft.Extensions.Time.Testing;
using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Modules.Portal.Application;
using Starter.Modules.Portal.Application.Content;
using Starter.Modules.Portal.Domain;
using Starter.Modules.Portal.Tests.Fakes;

namespace Starter.Modules.Portal.Tests.Application;

public sealed class ReplacePortalContentValidatorTests
{
    private static readonly ReplacePortalContentValidator Validator = new();

    [Fact]
    public void A_complete_list_is_valid_and_an_empty_list_empties_the_page()
    {
        Assert.True(Validator.Validate(new ReplacePortalContentCommand([ContentSamples.New(10)])).IsValid);
        Assert.True(Validator.Validate(new ReplacePortalContentCommand([])).IsValid);
    }

    [Fact]
    public void A_missing_list_is_required()
    {
        var errors = Validator.Validate(new ReplacePortalContentCommand(null)).ToDictionary();

        Assert.Equal([ValidationMessages.Required], errors["sections"]);
    }

    [Fact]
    public void More_sections_than_a_page_holds_is_too_long()
    {
        var sections = Enumerable.Range(0, PortalLimits.MaxSectionsPerPage + 1).Select(index => (SectionInput?)ContentSamples.New(index)).ToList();

        var errors = Validator.Validate(new ReplacePortalContentCommand(sections)).ToDictionary();

        Assert.Equal([ValidationMessages.TooLong], errors["sections"]);
    }

    [Fact]
    public void Field_errors_name_the_section_by_its_index()
    {
        var command = new ReplacePortalContentCommand(
        [
            ContentSamples.New(10),
            new SectionInput(null, " ", new string('t', PortalLimits.SectionTitleMaxLength + 1), null, "body", null),
            null,
            ContentSamples.New(PortalLimits.SortOrderMax + 1),
        ]);

        var errors = Validator.Validate(command).ToDictionary();

        Assert.Equal([ValidationMessages.Required], errors["sections[1].titleAr"]);
        Assert.Equal([ValidationMessages.TooLong], errors["sections[1].titleEn"]);
        Assert.Equal([ValidationMessages.Required], errors["sections[1].bodyAr"]);
        Assert.Equal([ValidationMessages.Required], errors["sections[1].sortOrder"]);
        Assert.Equal([ValidationMessages.Required], errors["sections[2]"]);
        Assert.Equal([ValidationMessages.InvalidFormat], errors["sections[3].sortOrder"]);
        Assert.False(errors.ContainsKey("sections[0].titleAr"));
    }

    [Fact]
    public void A_body_over_the_limit_is_too_long()
    {
        var command = new ReplacePortalContentCommand([ContentSamples.New(10) with { BodyEn = new string('b', PortalLimits.SectionBodyMaxLength + 1) }]);

        Assert.Equal([ValidationMessages.TooLong], Validator.Validate(command).ToDictionary()["sections[0].bodyEn"]);
    }

    [Fact]
    public void The_same_id_twice_is_a_duplicate()
    {
        var id = PublicId<PortalSectionIds>.From(5);
        var command = new ReplacePortalContentCommand([ContentSamples.New(10) with { Id = id }, ContentSamples.New(20) with { Id = id }]);

        var errors = Validator.Validate(command).ToDictionary();

        Assert.Equal([PortalValidationMessages.Duplicate], errors["sections[1].id"]);
        Assert.False(errors.ContainsKey("sections[0].id"));
    }
}

public sealed class ReplacePortalContentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);

    private readonly FakePortalContentStore _store = new();
    private readonly FakeContentCache _cache = new();
    private readonly ReplacePortalContent _replace;

    public ReplacePortalContentTests()
    {
        _replace = new ReplacePortalContent(new ReplacePortalContentValidator(), _store, _cache, new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task Sections_are_updated_by_id_added_without_id_and_removed_when_left_out()
    {
        var kept = _store.Seed(PortalPage.Faq, ContentSamples.Text("Old question", 10));
        var dropped = _store.Seed(PortalPage.Faq, ContentSamples.Text("Gone", 20));
        var otherPage = _store.Seed(PortalPage.Terms, ContentSamples.Text("Terms", 10));

        var outcome = await _replace.ExecuteAsync(
            PortalPages.FaqKey,
            new ReplacePortalContentCommand(
            [
                ContentSamples.New(30) with { Id = PublicId<PortalSectionIds>.From(kept.Id), TitleEn = "  New question  " },
                ContentSamples.New(5) with { TitleEn = "Added" },
            ]),
            CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(["Added", "New question"], outcome.Value.Sections.Select(section => section.TitleEn));
        Assert.All(outcome.Value.Sections, section => Assert.True(section.Id > 0));
        Assert.Equal(Now, outcome.Value.UpdatedAt);
        Assert.Equal(Now, _store.Pages[PortalPage.Faq].UpdatedAt);
        Assert.DoesNotContain(dropped, _store.Sections);
        Assert.Contains(otherPage, _store.Sections);
        Assert.Equal(1, _store.Saves);
    }

    [Fact]
    public async Task A_change_drops_the_pages_cached_content_after_the_save()
    {
        var section = _store.Seed(PortalPage.Privacy, ContentSamples.Text("Old", 10));
        _cache.Store(new PortalContentView(PortalPage.Privacy, UpdatedAt: null, []));

        await _replace.ExecuteAsync(
            PortalPages.PrivacyKey,
            new ReplacePortalContentCommand([ContentSamples.New(10) with { Id = PublicId<PortalSectionIds>.From(section.Id) }]),
            CancellationToken.None);

        Assert.Equal([PortalPage.Privacy], _cache.Invalidated);
        Assert.Null(_cache.Find(PortalPage.Privacy));
    }

    [Fact]
    public async Task A_replace_that_changes_nothing_neither_saves_nor_stamps_nor_invalidates()
    {
        var section = _store.Seed(PortalPage.Faq, ContentSamples.Text("Same", 10));
        var same = ContentSamples.New(10) with { Id = PublicId<PortalSectionIds>.From(section.Id), TitleEn = "Same" };

        var outcome = await _replace.ExecuteAsync(PortalPages.FaqKey, new ReplacePortalContentCommand([same]), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(0, _store.Saves);
        Assert.Null(_store.Pages[PortalPage.Faq].UpdatedAt);
        Assert.Empty(_cache.Invalidated);
    }

    [Fact]
    public async Task An_id_of_another_page_or_a_removed_section_is_a_conflict_and_nothing_changes()
    {
        var foreign = _store.Seed(PortalPage.Terms, ContentSamples.Text("Terms", 10));
        _store.Seed(PortalPage.Faq, ContentSamples.Text("Faq", 10));

        var outcome = await _replace.ExecuteAsync(
            PortalPages.FaqKey,
            new ReplacePortalContentCommand([ContentSamples.New(10) with { Id = PublicId<PortalSectionIds>.From(foreign.Id) }]),
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(PortalErrorCodes.SectionNotOnPage, outcome.Refusal!.Code);
        Assert.Equal(RefusalCategory.Conflict, outcome.Refusal.Category);
        Assert.Equal(0, _store.Saves);
        Assert.Empty(_store.Removed);
        Assert.Empty(_cache.Invalidated);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("FAQ")]
    [InlineData(null)]
    public async Task An_unknown_page_is_not_found(string? page)
    {
        var outcome = await _replace.ExecuteAsync(page, new ReplacePortalContentCommand([]), CancellationToken.None);

        Assert.Equal(PortalErrorCodes.PageNotFound, outcome.Refusal!.Code);
        Assert.Equal(RefusalCategory.NotFound, outcome.Refusal.Category);
    }

    [Fact]
    public async Task An_invalid_body_is_refused_with_its_field_errors()
    {
        var outcome = await _replace.ExecuteAsync(PortalPages.FaqKey, new ReplacePortalContentCommand(null), CancellationToken.None);

        Assert.Equal(PlatformErrorCodes.Validation, outcome.Refusal!.Code);
        Assert.Contains("sections", outcome.Refusal.FieldErrors.Keys);
        Assert.Equal(0, _store.Saves);
    }
}

public sealed class PortalContentReadTests
{
    private readonly FakePortalContentStore _store = new();
    private readonly FakeContentCache _cache = new();

    [Fact]
    public async Task The_public_read_is_served_from_the_cache_after_the_first_read()
    {
        _store.Seed(PortalPage.ServiceGuide, ContentSamples.Text("Second", 20));
        _store.Seed(PortalPage.ServiceGuide, ContentSamples.Text("First", 10));
        var read = new GetPublishedPortalContent(new PortalContentReader(_store), _cache);

        var first = await read.ExecuteAsync(PortalPages.ServiceGuideKey, CancellationToken.None);
        var second = await read.ExecuteAsync(PortalPages.ServiceGuideKey, CancellationToken.None);

        Assert.Equal(["First", "Second"], first.Value.Sections.Select(section => section.TitleEn));
        Assert.Same(first.Value, second.Value);
        Assert.Equal(1, _store.Reads);
    }

    [Fact]
    public async Task The_public_read_rereads_after_an_invalidation()
    {
        var read = new GetPublishedPortalContent(new PortalContentReader(_store), _cache);
        await read.ExecuteAsync(PortalPages.TermsKey, CancellationToken.None);

        _cache.Invalidate(PortalPage.Terms);
        await read.ExecuteAsync(PortalPages.TermsKey, CancellationToken.None);

        Assert.Equal(2, _store.Reads);
    }

    [Fact]
    public async Task The_admin_read_is_never_cached()
    {
        var read = new GetPortalContent(new PortalContentReader(_store));

        await read.ExecuteAsync(PortalPages.FaqKey, CancellationToken.None);
        await read.ExecuteAsync(PortalPages.FaqKey, CancellationToken.None);

        Assert.Equal(2, _store.Reads);
        Assert.Empty(_cache.Entries);
    }

    [Fact]
    public async Task Both_reads_refuse_an_unknown_page()
    {
        var published = await new GetPublishedPortalContent(new PortalContentReader(_store), _cache).ExecuteAsync("home", CancellationToken.None);
        var admin = await new GetPortalContent(new PortalContentReader(_store)).ExecuteAsync("home", CancellationToken.None);

        Assert.Equal(PortalErrorCodes.PageNotFound, published.Refusal!.Code);
        Assert.Equal(PortalErrorCodes.PageNotFound, admin.Refusal!.Code);
        Assert.Equal(0, _store.Reads);
    }

    [Fact]
    public async Task A_seeded_page_has_no_update_time()
    {
        var content = await new PortalContentReader(_store).ReadAsync(PortalPage.Faq, CancellationToken.None);

        Assert.Null(content.UpdatedAt);
        Assert.Equal(PortalPage.Faq, content.Page);
    }
}

internal static class ContentSamples
{
    public static SectionInput New(int sortOrder) => new(null, "سؤال", "Question", "جواب", "Answer", sortOrder);

    public static SectionText Text(string titleEn, int sortOrder) => new("سؤال", titleEn, "جواب", "Answer", sortOrder);
}
