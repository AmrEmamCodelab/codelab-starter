using Codelab.Attachments.Core.Storage;
using Codelab.Attachments.Core.Uploads;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Codelab.Attachments.Core;

/// <summary>Registration of the attachment service core.</summary>
public static class AttachmentsCoreExtensions
{
    /// <summary>
    /// Registers the file-share store (<c>Attachments:Storage</c>, validated on start) and the upload pipeline, plus
    /// <see cref="TimeProvider.System"/> when none is registered. The pipeline also needs what
    /// <c>AddCodelabSecurity</c> registers from <c>Codelab:Security:Uploads</c> (the options and, with
    /// <c>ReceiptsEnabled</c>, the receipt signer); the host calls both.
    /// </summary>
    public static IServiceCollection AddAttachmentsCore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<AttachmentStorageOptions>()
            .Bind(configuration.GetSection(AttachmentStorageOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AttachmentStorageOptions>, AttachmentStorageOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAttachmentFileStore, FileShareAttachmentStore>();
        services.TryAddSingleton<IUploadPipeline, UploadPipeline>();
        return services;
    }
}
