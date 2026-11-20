using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace DataMigrator.Monitor;

/// <summary>
/// --monitor 模式不运行 Hangfire 服务端，注册/触发接口用空实现即可：
/// 定时执行由 MonitorCronWorker 负责，手动触发不适用。
/// </summary>
public sealed class NoopRecurringJobManager : IRecurringJobManager
{
    public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options)
    {
    }

    public void Trigger(string recurringJobId)
    {
    }

    public void RemoveIfExists(string recurringJobId)
    {
    }
}

public sealed class NoopBackgroundJobClient : IBackgroundJobClient
{
    public string Create(Job job, IState state) => Guid.NewGuid().ToString("N");

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}
