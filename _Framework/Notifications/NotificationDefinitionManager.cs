using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace TKWF.Ext.Notifications;

/// <summary>
/// 通知定义管理器实现——懒加载收集 Provider 定义（Singleton，m2）。
/// <para>首次访问时调用所有 <see cref="INotificationDefinitionProvider"/> 的 Define()，重复定义名抛异常。</para>
/// </summary>
internal sealed class NotificationDefinitionManager : INotificationDefinitionManager
{
    private readonly IEnumerable<INotificationDefinitionProvider> _providers;
    private readonly Lazy<ConcurrentDictionary<string, NotificationDefinition>> _definitions;

    public NotificationDefinitionManager(IEnumerable<INotificationDefinitionProvider> providers)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _definitions = new Lazy<ConcurrentDictionary<string, NotificationDefinition>>(CollectDefinitions);
    }

    private ConcurrentDictionary<string, NotificationDefinition> CollectDefinitions()
    {
        var dict = new ConcurrentDictionary<string, NotificationDefinition>(StringComparer.Ordinal);
        var context = new NotificationDefinitionContext(dict);
        foreach (var provider in _providers)
            provider.Define(context);
        return dict;
    }

    public NotificationDefinition Get(string name)
    {
        if (_definitions.Value.TryGetValue(name, out var definition))
            return definition;
        throw new KeyNotFoundException($"通知定义不存在：{name}");
    }

    public bool Exists(string name)
        => _definitions.Value.ContainsKey(name);

    public IReadOnlyList<NotificationDefinition> GetAll()
        => _definitions.Value.Values.ToList();

    /// <summary>注册上下文——添加定义时检测重名（并发安全）。</summary>
    private sealed class NotificationDefinitionContext : INotificationDefinitionContext
    {
        private readonly ConcurrentDictionary<string, NotificationDefinition> _dict;

        public NotificationDefinitionContext(ConcurrentDictionary<string, NotificationDefinition> dict)
            => _dict = dict;

        public void Add(NotificationDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (!_dict.TryAdd(definition.Name, definition))
                throw new InvalidOperationException($"通知定义重复注册：{definition.Name}");
        }
    }
}