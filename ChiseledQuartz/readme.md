# ParaParty Util ChiselQuartz

## Features

1. Add Quartz.NET task scheduler into ASP.NET Hosted Service.

## Examples

1. Define a job
```cs
[SimpleCronTabJob("0/5 * * * * ?")]
public class PrintLogEveryFiveSeconds : IJob
{
    private readonly ILogger<PrintLogEveryFiveSeconds> _logger;

    public PrintLogEveryFiveSeconds(ILogger<PrintLogEveryFiveSeconds> logger)
    {
        _logger = logger;
    }

    public Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Now time: {}", DateTime.Now.ToString(CultureInfo.InvariantCulture));
        return Task.CompletedTask;
    }
}
```

2. Add it to ASP.NET

```cs
    services.AddQuartzIntegrationConfiguration();
    services.AddQuartzJob<PrintLogEveryFiveSeconds>();
```
