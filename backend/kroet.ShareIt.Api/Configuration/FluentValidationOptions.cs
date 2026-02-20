using FluentValidation;
using Microsoft.Extensions.Options;

namespace kroet.ShareIt.Api.Configuration;

public class FluentValidateOptions<TOptions> : IValidateOptions<TOptions> where TOptions : class
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string? _name;

    public FluentValidateOptions(string? name, IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _name = name;
    }

    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        // Null name is used for default options. Skip if names don't match.
        if (_name != null && _name != name) return ValidateOptionsResult.Skip;

        // Ensure the object isn't null
        ArgumentNullException.ThrowIfNull(options);

        using var scope = _serviceProvider.CreateScope();
        var validator = scope.ServiceProvider.GetService<IValidator<TOptions>>();
        if (validator is null)
        {
            return ValidateOptionsResult.Fail($"No validator found for {name}");
        }
        
        var result = validator.Validate(options);
        
        if (result.IsValid) return ValidateOptionsResult.Success;

        var errors = result.Errors.Select(e => e.ToString());

        return ValidateOptionsResult.Fail(errors);
    }
}