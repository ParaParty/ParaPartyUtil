using System;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Quartz.Spi;

namespace Paraparty.ChiseledQuartz.Services.Implements
{
    /// <inheritdoc />
    public class QuartzJobFactory : IJobFactory
    {
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// Constructor of Quartz Job Factory.
        /// </summary>
        /// <param name="serviceProvider"></param>
        public QuartzJobFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        public IJob NewJob(TriggerFiredBundle triggerFiredBundle, IScheduler scheduler)
        {
            var jobDetail = triggerFiredBundle.JobDetail;
            return (IJob)_serviceProvider.GetRequiredService(jobDetail.JobType);
        }

        /// <inheritdoc />
        public void ReturnJob(IJob job) { }
    }
}
