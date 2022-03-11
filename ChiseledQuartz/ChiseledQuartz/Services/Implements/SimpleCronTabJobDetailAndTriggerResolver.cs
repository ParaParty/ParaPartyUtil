using System.Reflection;
using Paraparty.ChiseledQuartz.Attributes;
using Quartz;

namespace Paraparty.ChiseledQuartz.Services.Implements
{

    /// <summary>
    /// <see cref="SimpleCronTabJobDetailAndTriggerResolver"/> which is used for resolving the <see cref="IJob"/> which is decorated by <see cref="SimpleCronTabJobAttribute"/>.
    /// </summary>
    public class SimpleCronTabJobDetailAndTriggerResolver : IJobDetailAndTriggerResolver
    {
        /// <inheritdoc />
        public (IJobDetail, ITrigger) ResolveJobDetailAndTrigger(IJob target)
        {
            var type = target.GetType();
            var attr = type.GetCustomAttribute<SimpleCronTabJobAttribute>(false);

            var jobName = attr.JobName ?? type.Name;

            var trigger = TriggerBuilder.Create()
                .WithIdentity(jobName)
                .WithCronSchedule(attr.CronExpression)
                .Build();

            var jobDetail = JobBuilder
                .Create(type)
                .WithIdentity(jobName)
                .Build();

            return (jobDetail, trigger);
        }
    }
}
