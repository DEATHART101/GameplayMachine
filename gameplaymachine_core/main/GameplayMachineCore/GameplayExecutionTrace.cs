using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace GMCore
{
    public partial class GameplayMachine
    {
        private sealed class ExecutionTraceFrame
        {
            public GameplayExecutionTraceEvent Started;
            public ODDebugInterfaceMeta InterfaceMeta;
            public Stopwatch Stopwatch;
            public bool Ended;
        }

        [NonSerialized] private Stack<ExecutionTraceFrame> m_executionTraceStack;
        [NonSerialized] private Queue<GameplayExecutionTraceEvent> m_executionTraceHistory;
        [NonSerialized] private long m_nextExecutionSpanID;
        [NonSerialized] private string m_executionTraceOrigin;

        public event Action<GameplayExecutionTraceEvent> ExecutionTrace;

        public int ExecutionTraceCapacity { get; set; } = 2048;

        public IReadOnlyList<GameplayExecutionTraceEvent> GetExecutionTraceHistory()
        {
            EnsureExecutionTraceState();
            return m_executionTraceHistory.ToArray();
        }

        private T TraceExecution<T>(object param, Func<T> action, string origin = null)
        {
            ExecutionTraceFrame frame = BeginExecutionTrace(param, origin);
            try
            {
                T result = action();
                EndExecutionTrace(frame, result, null);
                return result;
            }
            catch (Exception exception)
            {
                EndExecutionTrace(frame, null, exception);
                throw;
            }
        }

        private void TraceExecution(object param, Action action, string origin = null)
        {
            TraceExecution<object>(param, () =>
            {
                action();
                return null;
            }, origin);
        }

        internal T WithExecutionTraceOrigin<T>(string origin, Func<T> action)
        {
            string previous = m_executionTraceOrigin;
            m_executionTraceOrigin = origin;
            try
            {
                return action();
            }
            finally
            {
                m_executionTraceOrigin = previous;
            }
        }

        private ExecutionTraceFrame BeginExecutionTrace(object param, string origin)
        {
            EnsureExecutionTraceState();
            ODDebugInterfaceMeta interfaceMeta = ResolveDebugInterfaceMeta(param);
            long spanID = Interlocked.Increment(ref m_nextExecutionSpanID);
            ExecutionTraceFrame parent = m_executionTraceStack.Count == 0 ? null : m_executionTraceStack.Peek();
            var started = new GameplayExecutionTraceEvent
            {
                Stage = GameplayExecutionTraceStage.Started,
                SpanID = spanID,
                ParentSpanID = parent?.Started.SpanID ?? 0,
                TraceID = parent?.Started.TraceID ?? spanID,
                Depth = m_executionTraceStack.Count,
                InterfaceName = interfaceMeta?.DisplayName ?? interfaceMeta?.StableName ??
                    param?.GetType().FullName ?? param?.GetType().Name ?? "Unknown",
                RpcMode = param is IReplicatedInterface replicated ? replicated.RpcMode : GameplayRpcMode.None,
                Origin = origin ?? m_executionTraceOrigin ?? "Local",
                TimestampUtc = DateTime.UtcNow,
                Input = CaptureInterfaceValue(param, interfaceMeta?.Inputs),
            };
            var frame = new ExecutionTraceFrame
            {
                Started = started,
                InterfaceMeta = interfaceMeta,
                Stopwatch = Stopwatch.StartNew(),
            };
            m_executionTraceStack.Push(frame);
            PublishExecutionTrace(started);
            return frame;
        }

        private void EndExecutionTrace(ExecutionTraceFrame frame, object result, Exception exception)
        {
            if (frame.Ended)
                return;
            frame.Ended = true;
            frame.Stopwatch.Stop();
            if (m_executionTraceStack.Count > 0 && ReferenceEquals(m_executionTraceStack.Peek(), frame))
                m_executionTraceStack.Pop();
            else
                RemoveExecutionTraceFrame(frame);

            bool succeeded = exception == null && ReadSucceeded(result);
            var completed = new GameplayExecutionTraceEvent
            {
                Stage = GameplayExecutionTraceStage.Completed,
                TraceID = frame.Started.TraceID,
                SpanID = frame.Started.SpanID,
                ParentSpanID = frame.Started.ParentSpanID,
                Depth = frame.Started.Depth,
                InterfaceName = frame.Started.InterfaceName,
                RpcMode = frame.Started.RpcMode,
                Origin = frame.Started.Origin,
                TimestampUtc = DateTime.UtcNow,
                DurationMilliseconds = frame.Stopwatch.Elapsed.TotalMilliseconds,
                Succeeded = succeeded,
                Error = exception?.ToString() ?? ReadError(result),
                Input = frame.Started.Input,
                Output = ReadOutput(result, frame.InterfaceMeta?.Outputs),
            };
            PublishExecutionTrace(completed);
        }

        private IEnumerator TraceRoutineExecution(object param, Func<IEnumerator> createRoutine)
        {
            ExecutionTraceFrame frame = BeginExecutionTrace(param, null);
            IEnumerator routine = null;
            try
            {
                routine = createRoutine();
                while (routine != null && MoveNextTracedRoutine(routine, frame, out object current))
                    yield return current;
                EndExecutionTrace(frame, null, null);
            }
            finally
            {
                if (!frame.Ended)
                    EndExecutionTrace(frame, null, new OperationCanceledException("Routine execution was stopped"));
                (routine as IDisposable)?.Dispose();
            }
        }

        private bool MoveNextTracedRoutine(IEnumerator routine, ExecutionTraceFrame frame, out object current)
        {
            try
            {
                bool result = routine.MoveNext();
                current = result ? routine.Current : null;
                return result;
            }
            catch (Exception exception)
            {
                EndExecutionTrace(frame, null, exception);
                throw;
            }
        }

        private void PublishExecutionTrace(GameplayExecutionTraceEvent traceEvent)
        {
            EnsureExecutionTraceState();
            m_executionTraceHistory.Enqueue(traceEvent);
            while (m_executionTraceHistory.Count > Math.Max(16, ExecutionTraceCapacity))
                m_executionTraceHistory.Dequeue();

            Action<GameplayExecutionTraceEvent> handlers = ExecutionTrace;
            if (handlers == null)
                return;

            foreach (Action<GameplayExecutionTraceEvent> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(traceEvent);
                }
                catch
                {
                    // Diagnostics must never alter gameplay execution semantics.
                }
            }
        }

        private void EnsureExecutionTraceState()
        {
            m_executionTraceStack = m_executionTraceStack ?? new Stack<ExecutionTraceFrame>();
            m_executionTraceHistory = m_executionTraceHistory ?? new Queue<GameplayExecutionTraceEvent>();
        }

        private void RemoveExecutionTraceFrame(ExecutionTraceFrame frame)
        {
            var retained = new Stack<ExecutionTraceFrame>();
            while (m_executionTraceStack.Count > 0)
            {
                ExecutionTraceFrame current = m_executionTraceStack.Pop();
                if (!ReferenceEquals(current, frame))
                    retained.Push(current);
            }
            while (retained.Count > 0)
                m_executionTraceStack.Push(retained.Pop());
        }

        private ODDebugInterfaceMeta ResolveDebugInterfaceMeta(object param)
        {
            if (param == null)
                return null;
            Type type = param.GetType();
            return Modules.OfType<IODDebugModule>()
                .SelectMany(item => item.GetAllODDebugInterfaceMetas() ?? Enumerable.Empty<ODDebugInterfaceMeta>())
                .FirstOrDefault(item => item.ParamType == type);
        }

        private static GameplayDebugValue CaptureInterfaceValue(object value,
            IReadOnlyList<ODDebugInterfaceFieldMeta> fields)
        {
            if (value == null)
                return GameplayDebugValue.Null();
            if (fields == null)
                return GameplayDebugValueConverter.FromObject(value);

            Type type = value.GetType();
            var result = new GameplayDebugValue
            {
                Kind = GameplayDebugValueKind.Object,
                TypeName = type.FullName ?? type.Name,
            };
            foreach (ODDebugInterfaceFieldMeta metadata in fields)
            {
                object memberValue = type.GetField(metadata.Name, BindingFlags.Instance | BindingFlags.Public)
                                         ?.GetValue(value) ??
                                     type.GetProperty(metadata.Name, BindingFlags.Instance | BindingFlags.Public)
                                         ?.GetValue(value);
                result.Members.Add(new GameplayDebugNamedValue
                {
                    Name = metadata.DisplayName ?? metadata.Name,
                    Value = GameplayDebugValueConverter.FromObject(memberValue,
                        metadata.ResourceOptions, metadata.ResourceKeyOptions),
                });
            }
            return result;
        }

        private static bool ReadSucceeded(object result)
        {
            if (result == null)
                return true;
            PropertyInfo property = result.GetType().GetProperty("Sussceeded", BindingFlags.Instance | BindingFlags.Public);
            return property == null || (bool)property.GetValue(result);
        }

        private static string ReadError(object result)
        {
            if (result == null)
                return null;
            Type type = result.GetType();
            object error = type.GetProperty("ErrorMessage", BindingFlags.Instance | BindingFlags.Public)
                               ?.GetValue(result) ??
                           type.GetField("ErrorMessage", BindingFlags.Instance | BindingFlags.Public)
                               ?.GetValue(result);
            return ReadSucceeded(result) ? null : error?.ToString();
        }

        private static GameplayDebugValue ReadOutput(object result,
            IReadOnlyList<ODDebugInterfaceFieldMeta> outputFields)
        {
            if (result == null)
                return GameplayDebugValue.Null();
            PropertyInfo outputProperty = result.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
            if (outputProperty != null)
            {
                try
                {
                    return CaptureInterfaceValue(outputProperty.GetValue(result), outputFields);
                }
                catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException)
                {
                    return GameplayDebugValue.Null();
                }
            }
            FieldInfo outputField = result.GetType().GetField("Result", BindingFlags.Instance | BindingFlags.Public);
            return CaptureInterfaceValue(outputField == null ? null : outputField.GetValue(result), outputFields);
        }
    }
}
