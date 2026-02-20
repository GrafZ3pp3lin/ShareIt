using FluentValidation;
using Microsoft.Extensions.Options;

namespace kroet.ShareIt.Api.Configuration;

public static class OptionsBuilderExtensions
{
    extension<TOptions>(OptionsBuilder<TOptions> builder) where TOptions : class
    {
        public OptionsBuilder<TOptions> ValidateWithFluentValidator<TValidator>() where TValidator : class, IValidator<TOptions>
        {
            builder.Services.AddScoped<IValidator<TOptions>, TValidator>();
            builder.Services.AddSingleton<IValidateOptions<TOptions>>(sp => new FluentValidateOptions<TOptions>(builder.Name, sp));
            
            return builder;
        }
        
        public OptionsBuilder<TOptions> RegisterAsPlain()
        {
            builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<TOptions>>().Value);

            return builder;
        }
    }
}