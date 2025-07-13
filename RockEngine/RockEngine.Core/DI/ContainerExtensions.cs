using RockEngine.Core.Rendering.Passes;
using RockEngine.Core.Rendering.PipelineRenderers;

using SimpleInjector;

using System;
using System.Collections.Generic;
using System.Linq;

namespace RockEngine.Core.DI
{
    public static class ContainerExtensions
    {
        private static readonly Dictionary<Type, List<Type>> _strategySubPassMap = new();
        private static readonly List<StrategyRegistration> _strategyRegistrations = new();
        private static readonly List<Type> _subPasses = new();

        public static StrategyRegistrationBuilder RegisterRenderPassStrategy<TStrategy>(this Container container)
            where TStrategy : class, IRenderPassStrategy
        {
            var type = typeof(TStrategy);
            var existing = _strategyRegistrations.FirstOrDefault(s => s.StrategyType == type);
            if (existing != null)
            {
                return new StrategyRegistrationBuilder(existing);
            }

            var registration = new StrategyRegistration(type);
            _strategyRegistrations.Add(registration);
            container.Register<TStrategy>(Lifestyle.Scoped);

            return new StrategyRegistrationBuilder(registration);
        }

        public static void RegisterRenderSubPass<TSubPass, TStrategy>(this Container container)
            where TSubPass : class, IRenderSubPass
            where TStrategy : class, IRenderPassStrategy
        {
            var subPassType = typeof(TSubPass);
            var strategyType = typeof(TStrategy);

            if (!_strategyRegistrations.Any(s => s.StrategyType == strategyType))
            {
                container.RegisterRenderPassStrategy<TStrategy>();
            }

            if (!_strategySubPassMap.TryGetValue(strategyType, out var subPasses))
            {
                subPasses = new List<Type>();
                _strategySubPassMap[strategyType] = subPasses;
            }

            if (!subPasses.Contains(subPassType))
            {
                subPasses.Add(subPassType);
            }

            if (!_subPasses.Contains(subPassType))
            {
                _subPasses.Add(subPassType);
                container.Register<TSubPass>(Lifestyle.Scoped);
            }
        }

        public static void BuildRenderPassSystem(this Container container)
        {
            var sortedStrategyTypes = GetSortedStrategies();

            foreach (var strategyType in sortedStrategyTypes)
            {
                if (_strategySubPassMap.ContainsKey(strategyType))
                {
                    var collectionType = typeof(StrategySubPassCollection<>).MakeGenericType(strategyType);
                    container.Register(collectionType, collectionType, Lifestyle.Scoped);
                }
            }

            container.Collection.Register<IRenderSubPass>(Array.Empty<IRenderSubPass>());

            container.RegisterConditional(
                typeof(IEnumerable<IRenderSubPass>),
                context =>
                {
                    var strategyType = context.Consumer?.ImplementationType;
                    return strategyType != null && _strategySubPassMap.ContainsKey(strategyType)
                        ? typeof(StrategySubPassCollection<>).MakeGenericType(strategyType)
                        : typeof(EmptySubPassCollection);
                },
                Lifestyle.Scoped,
                context => context.HasConsumer &&
                           typeof(IRenderPassStrategy).IsAssignableFrom(context.Consumer.ImplementationType) &&
                           context.ServiceType == typeof(IEnumerable<IRenderSubPass>));

            container.Collection.Register<IRenderPassStrategy>(sortedStrategyTypes);
        }

        private static List<Type> GetSortedStrategies()
        {
            var beforeAll = _strategyRegistrations.Where(r => r.IsBeforeAll).ToList();
            var normal = _strategyRegistrations.Where(r => !r.IsBeforeAll && !r.IsAfterAll).ToList();
            var afterAll = _strategyRegistrations.Where(r => r.IsAfterAll).ToList();

            return new[]
            {
                TopologicalSort(beforeAll, "BeforeAll"),
                TopologicalSort(normal, "Normal"),
                TopologicalSort(afterAll, "AfterAll")
            }
            .SelectMany(group => group)
            .Select(reg => reg.StrategyType)
            .ToList();
        }

        private static List<StrategyRegistration> TopologicalSort(List<StrategyRegistration> registrations, string groupName)
        {
            if (registrations.Count == 0) return registrations;

            var regDict = registrations.ToDictionary(r => r.StrategyType);
            var graph = new Dictionary<StrategyRegistration, List<StrategyRegistration>>();
            var inDegree = new Dictionary<StrategyRegistration, int>();

            foreach (var reg in registrations)
            {
                graph[reg] = new List<StrategyRegistration>();
                inDegree[reg] = 0;
            }

            foreach (var reg in registrations)
            {
                foreach (var afterType in reg.After)
                {
                    if (regDict.TryGetValue(afterType, out var dependency))
                    {
                        graph[dependency].Add(reg);
                        inDegree[reg]++;
                    }
                }
                foreach (var beforeType in reg.Before)
                {
                    if (regDict.TryGetValue(beforeType, out var dependent))
                    {
                        graph[reg].Add(dependent);
                        inDegree[dependent]++;
                    }
                }
            }

            var queue = new Queue<StrategyRegistration>(
                registrations.Where(reg => inDegree[reg] == 0));

            var sorted = new List<StrategyRegistration>();
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                sorted.Add(current);
                foreach (var neighbor in graph[current])
                {
                    if (--inDegree[neighbor] == 0) queue.Enqueue(neighbor);
                }
            }

            if (sorted.Count != registrations.Count)
                throw new InvalidOperationException($"Cycle detected in {groupName} strategy group");

            return sorted;
        }

        public class StrategyRegistration
        {
            public Type StrategyType { get; }
            public bool IsBeforeAll { get; set; }
            public bool IsAfterAll { get; set; }
            public List<Type> Before { get; } = new List<Type>();
            public List<Type> After { get; } = new List<Type>();

            public StrategyRegistration(Type strategyType)
            {
                StrategyType = strategyType;
            }
        }

        public class StrategyRegistrationBuilder
        {
            private readonly StrategyRegistration _registration;

            public StrategyRegistrationBuilder(StrategyRegistration registration)
            {
                _registration = registration;
            }

            public StrategyRegistrationBuilder After<T>() where T : IRenderPassStrategy
            {
                _registration.After.Add(typeof(T));
                return this;
            }

            public StrategyRegistrationBuilder Before<T>() where T : IRenderPassStrategy
            {
                _registration.Before.Add(typeof(T));
                return this;
            }

            public StrategyRegistrationBuilder BeforeAll()
            {
                if (_registration.IsAfterAll)
                    throw new InvalidOperationException("Cannot set both BeforeAll and AfterAll");
                _registration.IsBeforeAll = true;
                return this;
            }

            public StrategyRegistrationBuilder AfterAll()
            {
                if (_registration.IsBeforeAll)
                    throw new InvalidOperationException("Cannot set both BeforeAll and AfterAll");
                _registration.IsAfterAll = true;
                return this;
            }
        }

        private class StrategySubPassCollection<TStrategy> : IEnumerable<IRenderSubPass>
            where TStrategy : IRenderPassStrategy
        {
            private readonly Container _container;
            private List<IRenderSubPass>? _resolvedSubPasses;

            public StrategySubPassCollection(Container container) => _container = container;

            public IEnumerator<IRenderSubPass> GetEnumerator()
            {
                _resolvedSubPasses ??= _strategySubPassMap.TryGetValue(typeof(TStrategy), out var types)
                    ? types.Select(t => (IRenderSubPass)_container.GetInstance(t))
                           .OrderBy(p => p.Order)
                           .ToList()
                    : new List<IRenderSubPass>();
                return _resolvedSubPasses.GetEnumerator();
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private class EmptySubPassCollection : IEnumerable<IRenderSubPass>
        {
            public IEnumerator<IRenderSubPass> GetEnumerator() =>
                Enumerable.Empty<IRenderSubPass>().GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}