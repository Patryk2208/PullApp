using System;
using System.Collections.Generic;
using System.Text;
using TripPlanner.Application.Features.Validation;
using TripPlanner.Domain;

namespace TripPlanner.Application.Features.Routes
{
    public class CreateRouteHandler
    {
        private readonly IRouteCalculator _routeCalculator;
        private readonly IRouteJobRepository _repo;
        private readonly ValidatorChain<CreateRouteCommand> _validator;

        public CreateRouteHandler(
        IRouteCalculator routeCalculator,
        IRouteJobRepository repo,
        ValidatorChain<CreateRouteCommand> validator)
        {
            _routeCalculator = routeCalculator;
            _repo = repo;
            _validator = validator;
        }

        public async Task<Guid> Handle(CreateRouteCommand cmd)
        {
            // Validation
            await _validator.Validate(cmd);

            // Dispatched to route calculator
            var externalId = await DispatchRoute(cmd);

            // Create a job to track the status
            var job = CreateJob(externalId, cmd);

            await _repo.Add(job);

            return job.Id;
        }

        public async Task<Guid> DispatchRoute(CreateRouteCommand cmd)
        {
            return await _routeCalculator.SendComputeAsync(
                new ComputePayload()
            );
        }

        private RouteJob CreateJob(Guid externalId, CreateRouteCommand cmd)
        {
            return new RouteJob
            {
                Id = Guid.NewGuid(),
                ExternalJobId = externalId,
                Status = JobStatus.Pending
            };
        }
    }
}
