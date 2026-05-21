using System.ComponentModel.DataAnnotations;

namespace ScamAlert.Api.Services.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class ValidEmailAddressAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var text = value as string;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ValidationResult("Email is required.");
        }

        return EmailAddressValidator.TryValidate(text, out _, out var error)
            ? ValidationResult.Success
            : new ValidationResult(error ?? EmailAddressValidator.InvalidMessage);
    }
}
