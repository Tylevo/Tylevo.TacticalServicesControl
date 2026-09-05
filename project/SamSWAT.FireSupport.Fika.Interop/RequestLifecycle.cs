#nullable disable

using System;
using System.Collections.Generic;

namespace SamSWAT.FireSupport.ArysReloaded.Fika;

internal sealed class FirstResult<T>
{
	private readonly object _gate = new();
	private bool _completed;
	private T _result = default;

	public bool IsCompleted
	{
		get
		{
			lock (_gate)
			{
				return _completed;
			}
		}
	}

	public bool TrySet(T result)
	{
		lock (_gate)
		{
			if (_completed)
			{
				return false;
			}

			_completed = true;
			_result = result;
			return true;
		}
	}

	public bool TryGet(out T result)
	{
		lock (_gate)
		{
			result = _result;
			return _completed;
		}
	}
}

internal enum PendingRequestRegistration
{
	Created,
	Existing,
	PayloadMismatch,
	CapacityReached,
	InvalidRequestId
}

internal sealed class PendingRequestTable<TFingerprint, TEntry>
	where TEntry : class
{
	private sealed class Record
	{
		public Record(TFingerprint fingerprint, TEntry entry)
		{
			Fingerprint = fingerprint;
			Entry = entry;
		}

		public TFingerprint Fingerprint { get; }
		public TEntry Entry { get; }
	}

	private readonly object _gate = new();
	private readonly Dictionary<string, Record> _entries =
		new(StringComparer.Ordinal);
	private readonly IEqualityComparer<TFingerprint> _fingerprintComparer;

	public PendingRequestTable(
		IEqualityComparer<TFingerprint> fingerprintComparer = null)
	{
		_fingerprintComparer =
			fingerprintComparer ?? EqualityComparer<TFingerprint>.Default;
	}

	public int Count
	{
		get
		{
			lock (_gate)
			{
				return _entries.Count;
			}
		}
	}

	public PendingRequestRegistration GetOrAdd(
		string requestId,
		TFingerprint fingerprint,
		int capacity,
		Func<TFingerprint, TEntry> factory,
		out TEntry entry)
	{
		entry = null;
		if (string.IsNullOrWhiteSpace(requestId))
		{
			return PendingRequestRegistration.InvalidRequestId;
		}

		if (factory == null)
		{
			throw new ArgumentNullException(nameof(factory));
		}

		lock (_gate)
		{
			if (_entries.TryGetValue(requestId, out Record existing))
			{
				entry = existing.Entry;
				return _fingerprintComparer.Equals(
					existing.Fingerprint,
					fingerprint)
					? PendingRequestRegistration.Existing
					: PendingRequestRegistration.PayloadMismatch;
			}

			if (_entries.Count >= Math.Max(0, capacity))
			{
				return PendingRequestRegistration.CapacityReached;
			}

			entry = factory(fingerprint);
			if (entry == null)
			{
				throw new InvalidOperationException(
					"Pending request factory returned null.");
			}

			_entries.Add(requestId, new Record(fingerprint, entry));
			return PendingRequestRegistration.Created;
		}
	}

	public bool TryGetValue(string requestId, out TEntry entry)
	{
		lock (_gate)
		{
			if (_entries.TryGetValue(
				    requestId ?? string.Empty,
				    out Record record))
			{
				entry = record.Entry;
				return true;
			}
		}

		entry = null;
		return false;
	}

	public bool RemoveIfSame(string requestId, TEntry expected)
	{
		lock (_gate)
		{
			if (!_entries.TryGetValue(
				    requestId ?? string.Empty,
				    out Record record) ||
			    !ReferenceEquals(record.Entry, expected))
			{
				return false;
			}

			return _entries.Remove(requestId);
		}
	}

	public List<TEntry> ClearAndGetValues()
	{
		lock (_gate)
		{
			var values = new List<TEntry>(_entries.Count);
			foreach (Record record in _entries.Values)
			{
				values.Add(record.Entry);
			}

			_entries.Clear();
			return values;
		}
	}
}

internal enum AcceptedEventRegistration
{
	First,
	Duplicate,
	PayloadMismatch,
	InvalidRequestId
}

internal sealed class AcceptedEventRegistry<TFingerprint>
{
	private readonly object _gate = new();
	private readonly Dictionary<string, TFingerprint> _entries =
		new(StringComparer.Ordinal);
	private readonly IEqualityComparer<TFingerprint> _fingerprintComparer;

	public AcceptedEventRegistry(
		IEqualityComparer<TFingerprint> fingerprintComparer = null)
	{
		_fingerprintComparer =
			fingerprintComparer ?? EqualityComparer<TFingerprint>.Default;
	}

	public AcceptedEventRegistration Register(
		string requestId,
		TFingerprint fingerprint)
	{
		if (string.IsNullOrWhiteSpace(requestId))
		{
			return AcceptedEventRegistration.InvalidRequestId;
		}

		lock (_gate)
		{
			if (_entries.TryGetValue(requestId, out TFingerprint existing))
			{
				return _fingerprintComparer.Equals(existing, fingerprint)
					? AcceptedEventRegistration.Duplicate
					: AcceptedEventRegistration.PayloadMismatch;
			}

			_entries.Add(requestId, fingerprint);
			return AcceptedEventRegistration.First;
		}
	}

	public bool TryGetValue(string requestId, out TFingerprint fingerprint)
	{
		lock (_gate)
		{
			return _entries.TryGetValue(
				requestId ?? string.Empty,
				out fingerprint);
		}
	}

	public void Clear()
	{
		lock (_gate)
		{
			_entries.Clear();
		}
	}
}

internal enum AuthorityExecutionPhase
{
	Pending,
	ExecutionStarted,
	Completed,
	Abandoned
}

internal sealed class AuthorityExecutionTransition<T>
{
	private readonly object _gate = new();
	private AuthorityExecutionPhase _phase;
	private bool _executionStarted;
	private bool _hasResult;
	private T _result = default;

	public AuthorityExecutionPhase Phase
	{
		get
		{
			lock (_gate)
			{
				return _phase;
			}
		}
	}

	public bool IsCompleted
	{
		get
		{
			lock (_gate)
			{
				return _hasResult;
			}
		}
	}

	public bool IsAbandoned => Phase == AuthorityExecutionPhase.Abandoned;
	public bool ExecutionStarted
	{
		get
		{
			lock (_gate)
			{
				return _executionStarted;
			}
		}
	}

	public bool TryBeginExecution()
	{
		lock (_gate)
		{
			if (_phase != AuthorityExecutionPhase.Pending || _hasResult)
			{
				return false;
			}

			_executionStarted = true;
			_phase = AuthorityExecutionPhase.ExecutionStarted;
			return true;
		}
	}

	public bool TryComplete(T result)
	{
		lock (_gate)
		{
			if (_hasResult || _phase == AuthorityExecutionPhase.Abandoned)
			{
				return false;
			}

			_result = result;
			_hasResult = true;
			_phase = AuthorityExecutionPhase.Completed;
			return true;
		}
	}

	public bool TryCancelBeforeExecution(T result)
	{
		lock (_gate)
		{
			if (_phase != AuthorityExecutionPhase.Pending || _hasResult)
			{
				return false;
			}

			_result = result;
			_hasResult = true;
			_phase = AuthorityExecutionPhase.Completed;
			return true;
		}
	}

	public bool Abandon(T result, out bool completedWaiter)
	{
		lock (_gate)
		{
			if (_phase == AuthorityExecutionPhase.Abandoned)
			{
				completedWaiter = false;
				return false;
			}

			completedWaiter = !_hasResult;
			if (completedWaiter)
			{
				_result = result;
				_hasResult = true;
			}

			_phase = AuthorityExecutionPhase.Abandoned;
			return true;
		}
	}

	public bool TryGetResult(out T result)
	{
		lock (_gate)
		{
			result = _result;
			return _hasResult;
		}
	}
}
