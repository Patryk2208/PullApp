using System;
using System.Collections.Generic;
using System.Text;
using TripPlanner.Application.Features.DTO;
using TripPlanner.Application.Features.Extensions;
using TripPlanner.Domain;

namespace TripPlanner.Application.Features.Routes
{
    public class GetRouteHandler
    {
        private readonly IRouteJobRepository _repo;
        private readonly IRouteCalculator _calculator;

        public GetRouteHandler(
            IRouteJobRepository repo,
            IRouteCalculator calculator)
        {
            _repo = repo;
            _calculator = calculator;
        }

        public async Task<object> Handle(GetRouteQuery query)
        {
            var job = await _repo.Get(query.JobId);

            if (job == null)
                throw new Exception("Not found");

            if (job.Status == JobStatus.Completed)
            {
                return new RouteResponseDto
                {
                    Status = "completed",
                    Route = job.Route
                };
            }

            // sprawdzamy RouteCalc
            var result = await _calculator.TryGetResultAsync(job.ExternalJobId);

            if (result != null)
            {
                job.Status = JobStatus.Completed;
                job.Route = "FAKE_ROUTE"; // na razie

                await _repo.Update(job);
            }

            return new RouteResponseDto
            {
                Status = JobStatusExtensions.ToApiString(job.Status),
                Route = job.Route
            };
        }
    }
}
