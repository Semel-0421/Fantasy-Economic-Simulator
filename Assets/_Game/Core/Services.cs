using System;
using System.Collections.Generic;

namespace FES.Core
{
    /// <summary>
    /// Build-time registry for shared, stateless-or-read-only objects (definitions, policies, models).
    /// Systems resolve services in Initialize; nothing is looked up per entity.
    /// </summary>
    public sealed class ServiceRegistry
    {
        readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        /// <summary>Registers or replaces a service.</summary>
        public void Set<T>(T service) where T : class => _services[typeof(T)] = service ?? throw new ArgumentNullException(nameof(service));

        /// <summary>Registers a service only if none is registered yet (lets callers override module defaults).</summary>
        public void SetDefault<T>(T service) where T : class
        {
            if (!_services.ContainsKey(typeof(T))) Set(service);
        }

        public T Get<T>() where T : class
        {
            if (_services.TryGetValue(typeof(T), out var s)) return (T)s;
            throw new KeyNotFoundException($"Service {typeof(T).Name} is not registered.");
        }
    }
}
