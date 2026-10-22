namespace AtmApp.Api.Domain;

public sealed class DomainException(DomainError error) : Exception($"Domain rule violated: {error}.")
{
    public DomainError Error { get; } = error;
}
