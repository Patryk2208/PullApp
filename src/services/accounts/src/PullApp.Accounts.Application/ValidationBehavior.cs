using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PullApp.Accounts.Application.Metrics;

namespace PullApp.Accounts.Application;

public class ValidationBehavior<TRequest, TResponse>(
	IEnumerable<IValidator<TRequest>> validators,
	AccountsMetrics metrics,
	ILogger<ValidationBehavior<TRequest, TResponse>> logger)
	: IPipelineBehavior<TRequest, TResponse>
	where TRequest : IRequest<TResponse>
{
	public async Task<TResponse> Handle(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken ct)
	{
		if (!validators.Any())
			return await next(ct);

		logger.LogDebug("Validating {RequestType}", typeof(TRequest).Name);

		var context = new ValidationContext<TRequest>(request);

		var results = await Task.WhenAll(
			validators.Select(v => v.ValidateAsync(context, ct)));

		var failures = results
			.SelectMany(r => r.Errors)
			.Where(f => f != null)
			.ToList();

		if (failures.Count != 0)
		{
			metrics.ValidationFailed();
			logger.LogWarning("Validation failed for {RequestType}: {Errors}",
				typeof(TRequest).Name,
				string.Join(", ", failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}")));
			throw new ValidationException(failures);
		}

		return await next(ct);
	}
}
