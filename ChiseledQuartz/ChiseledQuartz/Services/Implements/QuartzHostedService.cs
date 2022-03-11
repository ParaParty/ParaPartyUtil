using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Spi;

namespace Paraparty.ChiseledQuartz.Services.Implements
{
    /// <summary>
    /// Quartz Hosted Service for register Quartz.Net scheduler to ASP.NET
    /// </summary>
    public class QuartzHostedService : IHostedService
    {
        private readonly ILogger<QuartzHostedService> _logger;

        private readonly ISchedulerFactory _schedulerFactory;

        private readonly IJobFactory _jobFactory;

        private readonly IEnumerable<IJob> _jobList;

        private readonly IEnumerable<IJobDetailAndTriggerResolver> _jobDetailAndTriggerResolver;

        /// <summary>
        /// Constructor of Quartz Hosted Service
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="schedulerFactory"></param>
        /// <param name="jobFactory"></param>
        /// <param name="serviceProvider"></param>
        public QuartzHostedService(
            ILogger<QuartzHostedService> logger,
            ISchedulerFactory schedulerFactory,
            IJobFactory jobFactory, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _schedulerFactory = schedulerFactory;
            _jobFactory = jobFactory;
            _jobList = serviceProvider.GetServices<IJob>();
            _jobDetailAndTriggerResolver = serviceProvider.GetServices<IJobDetailAndTriggerResolver>();
        }


        private IScheduler? Scheduler { get; set; }

        /// <summary>
        /// Start Quartz.NET scheduler.
        /// </summary>
        /// <param name="cancellationToken"></param>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
            Scheduler.JobFactory = _jobFactory;

            foreach (var item in _jobList)
            {
                try
                {
                    var (job, trigger) = CreateJobDetailAndTrigger(item);
                    await Scheduler.ScheduleJob(job, trigger, cancellationToken);
                    _logger.LogDebug("IJob added to scheduler: {}", item.GetType().Name);
                }
                catch (NotSupportedException)
                {
                    _logger.LogDebug("Not supported IJob found, ignored: {}", item.GetType().Name);
                }
            }
            await Scheduler.Start(cancellationToken);
        }

        /// <summary>
        /// Stop Quartz.NET scheduler.
        /// </summary>
        /// <param name="cancellationToken"></param>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await Scheduler?.Shutdown(cancellationToken)!;
        }

        private (IJobDetail, ITrigger) CreateJobDetailAndTrigger(IJob item)
        {
            foreach (var resolver in _jobDetailAndTriggerResolver)
            {
                try
                {
                    return resolver.ResolveJobDetailAndTrigger(item);
                }
                catch (NotSupportedException)
                {
                    _logger.LogDebug("Not support to resolve IJobDetail and ITrigger. IJob: {}, Resolver: {},", item.GetType().Name, resolver.GetType().Name);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to resolve IJobDetail and ITrigger, Ignored. IJob: {}, Resolver: {},", item.GetType().Name, resolver.GetType().Name);
                }
            }
            throw new NotSupportedException();
        }
    }
}
