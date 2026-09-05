using Comfort.Common;
using EFT;
using SamSWAT.FireSupport.ArysReloaded.Utils;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Requester-local UH-60 cargo interaction zone. This component intentionally
/// has no countdown, extraction override, or session-stop capability.
/// </summary>
public class HeliCargoTransferPoint
	: MonoBehaviour, IPhysicsTrigger, IInteractive
{
	private readonly HashSet<Collider> _localColliders = new();
	private GameWorld _gameWorld;
	private bool _initialized;
	private bool _itemTransferOpen;
	private bool _successfulTransferObserved;
	private bool _successfulTransferPending;
	private bool _successfulTransferCompleted;
	private string _completedRequesterProfileId = string.Empty;
	private CancellationToken _cancellationToken;

	public string Description => "HeliCargoTransferPoint";
	internal bool IsItemTransferOpen => _itemTransferOpen;
	internal bool IsSuccessfulTransferPending => _successfulTransferPending;
	internal bool HasCompletedTransfer => _successfulTransferCompleted;
	internal string CompletedRequesterProfileId =>
		_completedRequesterProfileId;

	public void Initialize(CancellationToken cancellationToken)
	{
		_cancellationToken = cancellationToken;
		_initialized = true;
	}

	private void Start()
	{
		if (!_initialized)
		{
			Initialize(CancellationToken.None);
		}

		_gameWorld = Singleton<GameWorld>.Instance;
	}

	public void OnTriggerEnter(Collider collider)
	{
		if (_cancellationToken.IsCancellationRequested)
		{
			return;
		}

		Player player = _gameWorld?.GetPlayerByCollider(collider);
		if (player == null || !player.IsYourPlayer)
		{
			return;
		}

		PruneDestroyedColliders();
		if (!_localColliders.Add(collider) || _localColliders.Count > 1)
		{
			return;
		}

		FireSupportItemTransfer.EnterZone(this, player);
	}

	public void OnTriggerExit(Collider collider)
	{
		_localColliders.Remove(collider);
		PruneDestroyedColliders();
		if (_localColliders.Count > 0)
		{
			return;
		}

		FireSupportItemTransfer.LeaveZone(this, _gameWorld?.MainPlayer);
	}

	private void OnDestroy()
	{
		FireSupportItemTransfer.PointDestroyed(this);
		_itemTransferOpen = false;
		_successfulTransferPending = false;
		_localColliders.Clear();
	}

	internal bool CanOpenItemTransfer(Player player)
	{
		return !_itemTransferOpen &&
		       !_successfulTransferObserved &&
		       !_successfulTransferCompleted &&
		       _localColliders.Count > 0 &&
		       !_cancellationToken.IsCancellationRequested &&
		       player != null &&
		       player.IsYourPlayer &&
		       _gameWorld?.IsMainPlayerAlive() == true;
	}

	internal bool TryBeginItemTransfer(Player player)
	{
		if (!CanOpenItemTransfer(player))
		{
			return false;
		}

		_itemTransferOpen = true;
		player.SearchForInteractions();
		return true;
	}

	internal void EndItemTransfer(Player player)
	{
		if (!_itemTransferOpen)
		{
			return;
		}

		_itemTransferOpen = false;
		player?.SearchForInteractions();
	}

	internal void BeginSuccessfulTransfer(Player player)
	{
		if (_successfulTransferPending ||
		    _successfulTransferObserved ||
		    _successfulTransferCompleted ||
		    _cancellationToken.IsCancellationRequested)
		{
			return;
		}

		_successfulTransferObserved = true;
		_successfulTransferPending = true;
		_itemTransferOpen = false;
		_completedRequesterProfileId =
			player?.ProfileId?.Trim() ?? string.Empty;
		player?.SearchForInteractions();
	}

	internal void CompleteSuccessfulTransfer(Player player)
	{
		if (!_successfulTransferPending ||
		    _successfulTransferCompleted ||
		    _cancellationToken.IsCancellationRequested)
		{
			return;
		}

		_successfulTransferCompleted = true;
		_successfulTransferPending = false;
		_itemTransferOpen = false;
		if (string.IsNullOrWhiteSpace(_completedRequesterProfileId))
		{
			_completedRequesterProfileId =
				player?.ProfileId?.Trim() ?? string.Empty;
		}

		player?.SearchForInteractions();
	}

	internal void EndSuccessfulTransferVerification()
	{
		_successfulTransferPending = false;
	}

	private void PruneDestroyedColliders()
	{
		_localColliders.RemoveWhere(collider => collider == null);
	}
}
