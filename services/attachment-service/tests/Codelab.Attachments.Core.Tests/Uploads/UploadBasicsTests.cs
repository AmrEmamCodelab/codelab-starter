using Codelab.Attachments.Core.Errors;
using Codelab.Attachments.Core.Storage;
using Codelab.Attachments.Core.Tests.TestSupport;
using Codelab.Attachments.Core.Uploads;
using Codelab.Security;
using Codelab.Security.Uploads;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Codelab.Attachments.Core.Tests.Uploads;

public sealed class UploadRefusalTests
{
    [Theory]
    [InlineData(UploadRefusalReason.NotMultipart, AttachmentServiceErrorCodes.TypeNotAllowed, StatusCodes.Status415UnsupportedMediaType)]
    [InlineData(UploadRefusalReason.UnexpectedPart, AttachmentServiceErrorCodes.TypeNotAllowed, StatusCodes.Status415UnsupportedMediaType)]
    [InlineData(UploadRefusalReason.TypeNotAllowed, AttachmentServiceErrorCodes.TypeNotAllowed, StatusCodes.Status415UnsupportedMediaType)]
    [InlineData(UploadRefusalReason.SignatureMismatch, AttachmentServiceErrorCodes.SignatureMismatch, StatusCodes.Status415UnsupportedMediaType)]
    [InlineData(UploadRefusalReason.NoFile, AttachmentServiceErrorCodes.EmptyFile, StatusCodes.Status400BadRequest)]
    [InlineData(UploadRefusalReason.EmptyFile, AttachmentServiceErrorCodes.EmptyFile, StatusCodes.Status400BadRequest)]
    [InlineData(UploadRefusalReason.MalformedBody, AttachmentServiceErrorCodes.EmptyFile, StatusCodes.Status400BadRequest)]
    [InlineData(UploadRefusalReason.TooManyFiles, AttachmentServiceErrorCodes.TooManyFiles, StatusCodes.Status413PayloadTooLarge)]
    [InlineData(UploadRefusalReason.FileTooLarge, AttachmentServiceErrorCodes.FileTooLarge, StatusCodes.Status413PayloadTooLarge)]
    [InlineData(UploadRefusalReason.TotalTooLarge, AttachmentServiceErrorCodes.FileTooLarge, StatusCodes.Status413PayloadTooLarge)]
    public void Every_reason_maps_to_its_code_and_status(UploadRefusalReason reason, string code, int status)
    {
        var refusal = UploadRefusal.For(reason);

        Assert.Equal((reason, code, status), (refusal.Reason, refusal.Code, refusal.StatusCode));
    }

    [Fact]
    public void Every_defined_reason_is_mapped_and_an_undefined_one_throws()
    {
        Assert.All(Enum.GetValues<UploadRefusalReason>(), reason => Assert.NotNull(UploadRefusal.For(reason)));
        Assert.Throws<ArgumentOutOfRangeException>(() => UploadRefusal.For((UploadRefusalReason)999));
    }

    [Fact]
    public void The_error_codes_are_the_contract_values()
    {
        Assert.Equal(
            ["ATTACHMENTS_TICKET_INVALID", "ATTACHMENTS_FILE_TOO_LARGE", "ATTACHMENTS_TOO_MANY_FILES", "ATTACHMENTS_TYPE_NOT_ALLOWED", "ATTACHMENTS_SIGNATURE_MISMATCH", "ATTACHMENTS_EMPTY_FILE", "ATTACHMENTS_FILE_NOT_FOUND"],
            [AttachmentServiceErrorCodes.TicketInvalid, AttachmentServiceErrorCodes.FileTooLarge, AttachmentServiceErrorCodes.TooManyFiles, AttachmentServiceErrorCodes.TypeNotAllowed, AttachmentServiceErrorCodes.SignatureMismatch, AttachmentServiceErrorCodes.EmptyFile, AttachmentServiceErrorCodes.FileNotFound]);
    }
}

public sealed class MultipartPartsTests
{
    [Theory]
    [InlineData("multipart/form-data; boundary=abc", "abc")]
    [InlineData("Multipart/Form-Data; boundary=\"a b\"", "a b")]
    public void The_boundary_of_a_form_data_request_is_read(string contentType, string expected)
    {
        Assert.True(MultipartParts.TryReadBoundary(contentType, out var boundary));
        Assert.Equal(expected, boundary);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("text/plain; boundary=abc")]
    [InlineData("multipart/form-data")]
    [InlineData("multipart/form-data; boundary=")]
    public void Any_other_content_type_has_no_boundary(string? contentType)
    {
        Assert.False(MultipartParts.TryReadBoundary(contentType, out _));
    }

    [Fact]
    public void A_boundary_longer_than_the_rfc_limit_is_refused()
    {
        Assert.False(MultipartParts.TryReadBoundary("multipart/form-data; boundary=" + new string('b', MultipartParts.MaxBoundaryLength + 1), out _));
    }

    [Theory]
    [InlineData("form-data; name=\"file\"; filename=\"a.pdf\"", true, "a.pdf")]
    [InlineData("form-data; name=\"file\"; filename=\"a.pdf\"; filename*=UTF-8''%D9%85.pdf", true, "م.pdf")]
    [InlineData("form-data; name=\"files\"; filename=\"a.pdf\"", false, "")]
    [InlineData("form-data; name=\"file\"", false, "")]
    [InlineData("attachment; filename=\"a.pdf\"", false, "")]
    [InlineData(null, false, "")]
    public void Only_a_file_part_named_file_yields_a_client_name(string? disposition, bool expected, string expectedName)
    {
        var section = new MultipartSection { Headers = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>() };
        if (disposition is not null)
        {
            section.Headers["Content-Disposition"] = disposition;
        }

        Assert.Equal(expected, MultipartParts.TryReadFileName(section, out var fileName));
        Assert.Equal(expectedName, fileName);
    }
}

public sealed class AttachmentsCoreExtensionsTests
{
    [Fact]
    public void The_core_registers_the_store_and_the_pipeline_over_the_storage_section()
    {
        using var root = new TempStorageRoot();
        using var provider = BuildProvider(root.Path);

        Assert.IsType<FileShareAttachmentStore>(provider.GetRequiredService<IAttachmentFileStore>());
        Assert.IsType<UploadPipeline>(provider.GetRequiredService<IUploadPipeline>());
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void A_relative_storage_root_fails_validation()
    {
        using var provider = BuildProvider("relative-folder");

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AttachmentStorageOptions>>().Value);
    }

    private static ServiceProvider BuildProvider(string rootPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{AttachmentStorageOptions.SectionName}:RootPath"] = rootPath })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IUploadReceiptSigner>(new UploadReceiptSigner(UploadFixture.NewReceiptRing(), SecurityProtocol.Create(SecurityProtocol.TestPrefix)));
        services.AddSingleton(Options.Create(new UploadSafetyOptions()));
        services.AddAttachmentsCore(configuration);
        return services.BuildServiceProvider();
    }
}
