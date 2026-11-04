namespace Codelab.Integration.Outbound;

/// <summary>
/// A host-supplied <see cref="DelegatingHandler"/> placed outermost in every Codelab outbound client, before the retry
/// and timeout policy, so it runs once per call (not once per attempt). This is the correlation hook: the
/// observability package or the host registers a handler that copies its correlation id onto outbound requests.
/// </summary>
internal sealed record OutboundHandlerSlot(Type HandlerType);
