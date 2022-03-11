using System;
using Paraparty.ChiseledQuartz.Services.Implements;
using Quartz;

namespace Paraparty.ChiseledQuartz.Attributes
{
    /// <summary>
    /// Simple Cron Tab Job. <br/>
    /// Decorate it to your <see cref="IJob"/>, <see cref="SimpleCronTabJobDetailAndTriggerResolver"/> will resolve it and add to Quartz.NET scheduler.
    /// See Quartz.NET document: https://www.quartz-scheduler.net/documentation/quartz-3.x/tutorial/crontriggers.html
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class SimpleCronTabJobAttribute : Attribute
    {
        /// <summary>
        /// Job name. <br/>
        /// If null, it will replaced by the full name of being decorated class.
        /// </summary>
        public string? JobName { get; set; }

        /// <summary>
        /// Cron Expression.
        /// </summary>
        public string CronExpression { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="cronExpression"></param>
        public SimpleCronTabJobAttribute(string cronExpression)
        {
            CronExpression = cronExpression;
        }
    }
}
