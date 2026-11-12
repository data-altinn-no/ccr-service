using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using System;
using Polly;

namespace Dan.CcrService.Extensions;

public static class HttpResilience
{
    public const string PipelineName = "per-host-breaker";

    public static IHttpClientBuilder AddPerHostResilience(this IHttpClientBuilder builder, TimeSpan attemptTimeout)
    {
        builder
            .AddResilienceHandler(PipelineName, pipeline =>
            {
                pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,                          // open when >=50% of calls fail...
                    MinimumThroughput = 8,                       // ...but only with enough samples (no tripping on a single blip)
                    SamplingDuration = TimeSpan.FromSeconds(30), // failure ratio measured over this window
                    BreakDuration = TimeSpan.FromSeconds(20),    // fail fast for 20s, then probe again
                });
                pipeline.AddTimeout(attemptTimeout);             // throws TimeoutRejectedException, counted by the breaker
            })
            .SelectPipelineByAuthority();

        return builder;
    }
}
