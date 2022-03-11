using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Paraparty.ChiseledQuartz.Attributes;
using Quartz;

namespace Paraparty.Demo.Services.Implements;

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

[SimpleCronTabJob("0/6 * * * * ?")]
public class PrintLogEverySixSeconds : IJob
{
    private readonly ILogger<PrintLogEveryFiveSeconds> _logger;

    public PrintLogEverySixSeconds(ILogger<PrintLogEveryFiveSeconds> logger)
    {
        _logger = logger;
    }

    public Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Now time: {}", DateTime.Now.ToString(CultureInfo.InvariantCulture));
        return Task.CompletedTask;
    }
}
