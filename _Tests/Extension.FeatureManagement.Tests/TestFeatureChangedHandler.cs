using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Events;

namespace TKWF.Ext.FeatureManagement.Tests;

/// <summary>测试变更事件 handler（记录收到的 FeatureValueChangedEvent——手动注册，宿主不走 SG 自动收集）。</summary>
internal sealed class TestFeatureChangedHandler : ILocalEventHandler<FeatureValueChangedEvent>
{
    /// <summary>收到的全部事件（跨用例静态——测试开始清空）。</summary>
    public static readonly ConcurrentQueue<FeatureValueChangedEvent> Received = new();

    public static void Reset() => Received.Clear();

    public Task HandleEventAsync(FeatureValueChangedEvent eventData)
    {
        Received.Enqueue(eventData);
        return Task.CompletedTask;
    }
}
