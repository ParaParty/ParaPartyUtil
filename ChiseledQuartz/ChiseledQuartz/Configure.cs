using Microsoft.Extensions.DependencyInjection;
using Paraparty.ChiseledQuartz.Services;
using Paraparty.ChiseledQuartz.Services.Implements;
using Quartz;
using Quartz.Impl;
using Quartz.Spi;

namespace Paraparty.ChiseledQuartz
{
    /// <summary>
    /// Extension methods to simplify registering of Quartz.NET.
    /// </summary>
    public static class Configure
    {
        /// <summary>
        /// Register Quartz.NET to ASP.NET Dependency Inject.
        /// </summary>
        /// <param name="services"></param>
        public static void AddQuartzIntegrationConfiguration(this IServiceCollection services)
        {
            services.AddSingleton<IJobFactory, QuartzJobFactory>();
            services.AddSingleton<ISchedulerFactory, StdSchedulerFactory>();
            services.AddHostedService<QuartzHostedService>();

            services.AddSingleton<IJobDetailAndTriggerResolver, SimpleCronTabJobDetailAndTriggerResolver>();
        }

        /// <summary>
        /// Add Quartz.Net Job to <see cref="IServiceCollection"/>.
        /// </summary>
        /// <param name="services"></param>
        /// <typeparam name="T"></typeparam>
        public static void AddQuartzJob<T>(this IServiceCollection services) where T : class, IJob

        {
            services.AddSingleton<T, T>();
            services.AddSingleton<IJob, T>(x => x.GetRequiredService<T>());
        }
    }
}
