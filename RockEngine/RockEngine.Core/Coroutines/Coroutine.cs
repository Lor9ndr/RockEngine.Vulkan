using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace RockEngine.Core.Coroutines
{
    public enum CoroutineStatus
    {
        Running,
        Completed,
        Canceled,
        Errored
    }

    public class Coroutine
    {
        private readonly IEnumerator _enumerator;
        private CoroutineStatus _status = CoroutineStatus.Running;
        private Exception _error;
        private readonly Stopwatch _timer = new Stopwatch();

        public CoroutineStatus Status => _status;
        public Exception Error => _error;
        public TimeSpan RunTime => _timer.Elapsed;
        public string Name { get; set; }

        public event Action<Coroutine> OnCompleted;
        public event Action<Coroutine, Exception> OnError;

        public Coroutine(IEnumerator enumerator, string name = null)
        {
            _enumerator = enumerator;
            Name = name ?? enumerator.GetType().Name;
        }

        public bool MoveNext()
        {
            if (_status != CoroutineStatus.Running)
                return false;

            if (!_timer.IsRunning)
                _timer.Start();

            try
            {
                bool hasNext = _enumerator.MoveNext();

                if (!hasNext)
                {
                    Complete();
                }

                return hasNext;
            }
            catch (Exception ex)
            {
                Fail(ex);
                return false;
            }
        }

        public void Cancel()
        {
            if (_status == CoroutineStatus.Running)
            {
                _status = CoroutineStatus.Canceled;
                _timer.Stop();
            }
        }

        private void Complete()
        {
            _status = CoroutineStatus.Completed;
            _timer.Stop();
            OnCompleted?.Invoke(this);
        }

        private void Fail(Exception ex)
        {
            _status = CoroutineStatus.Errored;
            _error = ex;
            _timer.Stop();
            OnError?.Invoke(this, ex);
        }

        public object Current => _enumerator.Current;
    }

    // Yield instructions
    public class WaitForSeconds
    {
        public float Seconds { get; }
        private readonly Stopwatch _timer = Stopwatch.StartNew();

        public WaitForSeconds(float seconds)
        {
            Seconds = seconds;
        }

        public bool IsDone => _timer.Elapsed.TotalSeconds >= Seconds;
    }

    public class WaitForNextFrame { }

    public class WaitForTask<T>
    {
        public Task<T> Task { get; }
        public T Result => Task.Result;

        public WaitForTask(Task<T> task)
        {
            Task = task;
        }

        public bool IsDone => Task.IsCompleted;
    }

    public class WaitForTask
    {
        public Task Task { get; }

        public WaitForTask(Task task)
        {
            Task = task;
        }

        public bool IsDone => Task.IsCompleted;
    }

    public class WaitForCondition
    {
        private readonly Func<bool> _condition;

        public WaitForCondition(Func<bool> condition)
        {
            _condition = condition;
        }

        public bool IsDone => _condition();
    }

    // Coroutine scheduler
    public class CoroutineScheduler : IDisposable
    {
        private readonly List<Coroutine> _activeCoroutines = new List<Coroutine>();
        private readonly List<Coroutine> _coroutinesToAdd = new List<Coroutine>();
        private readonly List<Coroutine> _coroutinesToRemove = new List<Coroutine>();
        private readonly ConcurrentQueue<Coroutine> _readyCoroutines = new ConcurrentQueue<Coroutine>();
        private bool _isUpdating;

        public Coroutine StartCoroutine(IEnumerator routine, string name = null)
        {
            var coroutine = new Coroutine(routine, name);

            if (_isUpdating)
            {
                _coroutinesToAdd.Add(coroutine);
            }
            else
            {
                _activeCoroutines.Add(coroutine);
            }

            return coroutine;
        }

        public void StopCoroutine(Coroutine coroutine)
        {
            if (coroutine == null) return;

            if (_isUpdating)
            {
                _coroutinesToRemove.Add(coroutine);
            }
            else
            {
                _activeCoroutines.Remove(coroutine);
                coroutine.Cancel();
            }
        }

        public void StopAllCoroutines()
        {
            foreach (var coroutine in _activeCoroutines)
            {
                coroutine.Cancel();
            }
            _activeCoroutines.Clear();
            _coroutinesToAdd.Clear();
            _coroutinesToRemove.Clear();
        }

        public void Update()
        {
            _isUpdating = true;

            // Process active coroutines
            foreach (var coroutine in _activeCoroutines)
            {
                if (coroutine.Status != CoroutineStatus.Running)
                {
                    _coroutinesToRemove.Add(coroutine);
                    continue;
                }

                var current = coroutine.Current;

                // Handle yield instructions
                if (current is WaitForSeconds waitForSeconds)
                {
                    if (waitForSeconds.IsDone)
                    {
                        if (!coroutine.MoveNext())
                        {
                            _coroutinesToRemove.Add(coroutine);
                        }
                    }
                }
                else if (current is WaitForNextFrame)
                {
                    // Always continue on next frame
                    if (!coroutine.MoveNext())
                    {
                        _coroutinesToRemove.Add(coroutine);
                    }
                }
                else if (current is WaitForTask waitForTask)
                {
                    if (waitForTask.IsDone)
                    {
                        if (!coroutine.MoveNext())
                        {
                            _coroutinesToRemove.Add(coroutine);
                        }
                    }
                }
                else if (current is WaitForTask<object> waitForTaskObj)
                {
                    if (waitForTaskObj.IsDone)
                    {
                        if (!coroutine.MoveNext())
                        {
                            _coroutinesToRemove.Add(coroutine);
                        }
                    }
                }
                else if (current is WaitForCondition waitForCondition)
                {
                    if (waitForCondition.IsDone)
                    {
                        if (!coroutine.MoveNext())
                        {
                            _coroutinesToRemove.Add(coroutine);
                        }
                    }
                }
                else if (current == null)
                {
                    // null means wait one frame (like yield return null in Unity)
                    if (!coroutine.MoveNext())
                    {
                        _coroutinesToRemove.Add(coroutine);
                    }
                }
                else
                {
                    // Unknown yield type, just continue
                    if (!coroutine.MoveNext())
                    {
                        _coroutinesToRemove.Add(coroutine);
                    }
                }
            }

            _isUpdating = false;

            // Clean up completed coroutines
            foreach (var coroutine in _coroutinesToRemove)
            {
                _activeCoroutines.Remove(coroutine);
            }
            _coroutinesToRemove.Clear();

            // Add new coroutines
            _activeCoroutines.AddRange(_coroutinesToAdd);
            _coroutinesToAdd.Clear();
        }

        public int ActiveCoroutineCount => _activeCoroutines.Count;

        public void Dispose()
        {
            StopAllCoroutines();
        }
    }
}