using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AidlyPms.Api.Common;

public class FlexibleDateTimeModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueProviderResult == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        var value = valueProviderResult.FirstValue;
        if (string.IsNullOrWhiteSpace(value))
        {
            if (bindingContext.ModelType == typeof(DateTime?))
            {
                bindingContext.Result = ModelBindingResult.Success(null);
            }
            return Task.CompletedTask;
        }

        if (DateHelper.TryParse(value, out var parsedDate))
        {
            var paramName = bindingContext.ModelName.ToLowerInvariant();
            // If parameter represents an end/to date range filter and has no explicit time, normalize to end-of-day
            if ((paramName.Contains("to") || paramName.Contains("end") || paramName.Contains("max")) && parsedDate.TimeOfDay == TimeSpan.Zero)
            {
                parsedDate = DateHelper.ToEndOfDayUtc(parsedDate);
            }
            else if ((paramName.Contains("from") || paramName.Contains("start") || paramName.Contains("min")) && parsedDate.TimeOfDay == TimeSpan.Zero)
            {
                parsedDate = DateHelper.ToStartOfDayUtc(parsedDate);
            }

            bindingContext.Result = ModelBindingResult.Success(parsedDate);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, $"Invalid date format '{value}'. Accepted formats include DD-MM-YYYY, DD/MM/YYYY, and YYYY-MM-DD.");
        }

        return Task.CompletedTask;
    }
}

public class FlexibleDateTimeModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        if (context.Metadata.ModelType == typeof(DateTime) || context.Metadata.ModelType == typeof(DateTime?))
        {
            return new FlexibleDateTimeModelBinder();
        }

        return null;
    }
}
