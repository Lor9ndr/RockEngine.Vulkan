using System.Reflection;
using MethodDecorator.Fody.Interfaces;
using RockEngine.Core.Diagnostics;

namespace RockEngine.Core.Attributes
{
    /// <summary>
    /// CPU tracing attribute that supports property‑chain expressions like {camera.Entity.Name}.
    /// Formatting is performed by a source‑generated delegate with zero runtime reflection.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor)]
    public sealed class TraceAttribute : Attribute, IMethodDecorator
    {
        private readonly string? _format;

        // Per‑invocation state (async‑safe)
        private static readonly AsyncLocal<Stack<TraceState>> _scopeStack = new();

        // Hook that the source generator sets via a module initializer.
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Стили именования", Justification = "<Ожидание>")]
        internal static Func<int, Func<object[], string>>? FormatterLookup;

        public TraceAttribute() { }
        public TraceAttribute(string format) => _format = format;

        public void Init(object instance, MethodBase method, object[] args)
        {
            string name;

            // Try to use the source‑generated formatter (zero reflection)
            var lookup = FormatterLookup;
            if (lookup is not null)
            {
                var formatter = lookup(method.MetadataToken);
                if (formatter is not null)
                {
                    name = formatter(args);
                    goto PushState;
                }
            }

            // Fallback: plain method name (no format string, or generator not available)
            name = method.Name;

        PushState:
            var stack = _scopeStack.Value;
            if (stack is null)
            {
                stack = new Stack<TraceState>();
                _scopeStack.Value = stack;
            }
            stack.Push(new TraceState { FormattedName = name });
        }

        public void OnEntry()
        {
            var state = _scopeStack.Value!.Peek();
            state.Tracker = PerformanceTracer.BeginSection(state.FormattedName);
        }

        public void OnExit()
        {
            var stack = _scopeStack.Value;
            if (stack is not null && stack.Count > 0)
            {
                var state = stack.Pop();
                state.Tracker?.Dispose();
            }
        }

        public void OnException(Exception exception) { /* OnExit is always called */ }
        public void OnTaskContinuation(Task task) { }

        private sealed class TraceState
        {
            public string FormattedName { get; set; } = string.Empty;
            public PerformanceTracer.CpuSectionTracker? Tracker { get; set; }
        }
    }
}