namespace AtmApp.Api.Application;

public sealed class AccountNotFoundException() : Exception("No account exists with the given id.");
