using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace PullApp.Accounts.Api.Middleware;

public class GlobalExceptionHandler : IExceptionHandler
{
	private readonly ILogger<GlobalExceptionHandler> _logger;

	public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) =>
		_logger = logger;

	public async ValueTask<bool> TryHandleAsync(
		HttpContext httpContext,
		Exception exception,
		CancellationToken cancellationToken)
	{
		// TODO MAYBE localization?
		
		_logger.LogError(exception, "Wystąpił nieobsłużony błąd: {Message}", exception.Message);

		var (statusCode, title) = exception switch
		{
			ValidationException => (StatusCodes.Status400BadRequest, "Błąd walidacji"),
			UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Brak dostępu"), // To dodajemy
			_ => (StatusCodes.Status500InternalServerError, "Błąd serwera"),
		};

		var problemDetails = new ProblemDetails
		{
			Status = statusCode,
			Title = title,
			Detail = exception.Message,
			Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}",
		};

		if (exception is ValidationException validationException)
		{
			problemDetails.Extensions["errors"] = validationException.Errors
				.Select(e => new { e.PropertyName, e.ErrorMessage });
		}

		httpContext.Response.StatusCode = statusCode;
		await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

		return true; // caught successfully
	}
}
