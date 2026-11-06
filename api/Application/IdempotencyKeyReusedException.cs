namespace AtmApp.Api.Application;

public sealed class IdempotencyKeyReusedException() : Exception("The idempotency key was already used for a different request.");
