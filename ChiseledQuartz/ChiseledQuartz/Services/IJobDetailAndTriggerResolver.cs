using System;
using Quartz;

namespace Paraparty.ChiseledQuartz.Services
{
    /// <summary>
    /// <see cref="IJobDetailAndTriggerResolver"/> is used for creating <see cref="IJobDetail"/> and <see cref="ITrigger"/>
    /// object for Quartz.NET task scheduler.  
    /// </summary>
    public interface IJobDetailAndTriggerResolver
    {
        /// <summary>
        /// Resolve <see cref="IJobDetail"/> and <see cref="ITrigger"/> from given <see cref="IJob"/>.
        /// <br/>
        /// The object return will immediately add to Quartz.NET scheduler.
        /// <br/>
        /// If failed to resolve. Please feel free to throw a <see cref="NotSupportedException"/>, this <see cref="IJob"/> will be ignored.
        /// </summary>
        /// <exception cref="NotSupportedException">Failed to resolve data.</exception>
        /// <param name="target"></param>
        (IJobDetail, ITrigger) ResolveJobDetailAndTrigger(IJob target);
    }
}
