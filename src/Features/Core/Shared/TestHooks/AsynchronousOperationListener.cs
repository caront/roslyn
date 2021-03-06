// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.CodeAnalysis.Shared.TestHooks
{
    internal abstract partial class AsynchronousOperationListener : IAsynchronousOperationListener, IAsynchronousOperationWaiter
    {
        private readonly object _gate = new object();

        private readonly HashSet<TaskCompletionSource<bool>> _pendingTasks = new HashSet<TaskCompletionSource<bool>>();

        private int _counter;
        private bool _trackActiveTokens = false;
        private HashSet<DiagnosticAsyncToken> _activeDiagnosticTokens = new HashSet<DiagnosticAsyncToken>();

        public IAsyncToken BeginAsyncOperation(string name, object tag = null)
        {
            lock (_gate)
            {
                if (_trackActiveTokens)
                {
                    var token = new DiagnosticAsyncToken(this, name, tag);
                    _activeDiagnosticTokens.Add(token);
                    return token;
                }
                else
                {
                    return new AsyncToken(this);
                }
            }
        }

        private void Increment()
        {
            lock (_gate)
            {
                _counter++;
            }
        }

        private void Decrement(AsyncToken token)
        {
            lock (_gate)
            {
                _counter--;
                if (_counter == 0)
                {
                    foreach (var task in _pendingTasks)
                    {
                        task.SetResult(true);
                    }

                    _pendingTasks.Clear();
                }

                if (_trackActiveTokens)
                {
                    var diagnosticAsyncToken = token as DiagnosticAsyncToken;

                    if (diagnosticAsyncToken != null)
                    {
                        _activeDiagnosticTokens.Remove(diagnosticAsyncToken);
                    }
                }
            }
        }

        public virtual Task CreateWaitTask()
        {
            lock (_gate)
            {
                var source = new TaskCompletionSource<bool>();
                if (_counter == 0)
                {
                    // There is nothing to wait for, so we are immediately done
                    source.SetResult(true);
                }
                else
                {
                    _pendingTasks.Add(source);
                }

                return source.Task;
            }
        }

        public bool TrackActiveTokens
        {
            get
            {
                return _trackActiveTokens;
            }

            set
            {
                lock (_gate)
                {
                    if (_trackActiveTokens == value)
                    {
                        return;
                    }

                    _trackActiveTokens = value;

                    if (_trackActiveTokens)
                    {
                        _activeDiagnosticTokens = new HashSet<DiagnosticAsyncToken>();
                    }
                    else
                    {
                        _activeDiagnosticTokens = null;
                    }
                }
            }
        }

        public bool HasPendingWork
        {
            get
            {
                return _counter != 0;
            }
        }

        public ImmutableArray<DiagnosticAsyncToken> ActiveDiagnosticTokens
        {
            get
            {
                lock (_gate)
                {
                    if (_activeDiagnosticTokens == null)
                    {
                        return ImmutableArray<DiagnosticAsyncToken>.Empty;
                    }

                    return _activeDiagnosticTokens.ToImmutableArray();
                }
            }
        }
    }
}
