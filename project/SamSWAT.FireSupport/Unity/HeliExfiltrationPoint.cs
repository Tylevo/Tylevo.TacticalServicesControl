using Comfort.Common;
using EFT;
using EFT.UI;
using SamSWAT.FireSupport.ArysReloaded.Utils;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public class HeliExfiltrationPoint : MonoBehaviour, IPhysicsTrigger
{
	private float _timer;
	private Coroutine _coroutine;
	private BattleUIPanelExitTrigger _battleUIPanelExitTrigger;
	private GameWorld _gameWorld;
	private ESupportType _supportType = ESupportType.Extract;
	private float _extractTimeSeconds;
	private bool _initialized;
	private bool _completed;
	private CancellationToken _cancellationToken;
	private readonly HashSet<Collider> _localColliders = new();

	public string Description => "HeliExfiltrationPoint";

	public void Initialize(
		ESupportType supportType,
		float extractTimeSeconds,
		CancellationToken cancellationToken)
	{
		_supportType = supportType == ESupportType.PriorityExfil
			? ESupportType.PriorityExfil
			: ESupportType.Extract;
		_extractTimeSeconds = Mathf.Max(0.1f, extractTimeSeconds);
		_cancellationToken = cancellationToken;
		_initialized = true;
	}

	private void Start()
	{
		if (!_initialized)
		{
			Initialize(
				_supportType,
				FireSupportTuningSettings.GetHelicopterExtractTime(_supportType),
				CancellationToken.None);
		}

		_gameWorld = Singleton<GameWorld>.Instance;
		_battleUIPanelExitTrigger = Singleton<GameUI>.Instance?.BattleUiPanelExitTrigger;
	}

	public void OnTriggerEnter(Collider collider)
	{
		if (_completed || _cancellationToken.IsCancellationRequested)
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

		ResetTimer();
		_battleUIPanelExitTrigger?.Show(_timer);

		if (_coroutine == null)
		{
			_coroutine = StartCoroutine(Timer(player));
		}
	}

	public void OnTriggerExit(Collider collider)
	{
		_localColliders.Remove(collider);
		PruneDestroyedColliders();
		if (_localColliders.Count > 0 || _coroutine == null)
		{
			return;
		}

		ResetTimer();
		_battleUIPanelExitTrigger?.Close();

		if (_coroutine != null)
		{
			StopCoroutine(_coroutine);
			_coroutine = null;
		}
	}

	private void OnDestroy()
	{
		if (_coroutine != null)
		{
			StopCoroutine(_coroutine);
			_coroutine = null;
		}

		_localColliders.Clear();
		if (Singleton<GameUI>.Instantiated && _battleUIPanelExitTrigger != null)
		{
			_battleUIPanelExitTrigger.Close();
		}
	}

	private void ResetTimer()
	{
		_timer = _extractTimeSeconds;
	}

	private IEnumerator Timer(Player player)
	{
		while (_timer > 0)
		{
			PruneDestroyedColliders();
			if (!CanCompleteExtraction(player))
			{
				CloseAndStopTimer();
				yield break;
			}

			yield return null;
			_timer -= Time.deltaTime;
		}

		if (!CanCompleteExtraction(player))
		{
			CloseAndStopTimer();
			yield break;
		}

		_completed = true;
		CloseAndStopTimer();

		// In a Fika session the extraction must go through Fika's extract flow
		// (host stays to keep the session alive, clients despawn cleanly).
		// Stopping the session directly here put the lobby into limbo when the
		// host extracted before other players.
		if (FireSupportExtraction.TryOverrideExtract(player, "UH-60 Black Hawk"))
		{
			yield break;
		}

		if (Singleton<AbstractGame>.Instance is ISessionStopper sessionStopper)
		{
			sessionStopper.StopSession(player.ProfileId, ExitStatus.Survived, "UH-60 Black Hawk");
		}
	}

	private bool CanCompleteExtraction(Player player)
	{
		return !_completed &&
		       _localColliders.Count > 0 &&
		       !_cancellationToken.IsCancellationRequested &&
		       player != null &&
		       player.IsYourPlayer &&
		       _gameWorld?.IsMainPlayerAlive() == true;
	}

	private void CloseAndStopTimer()
	{
		_battleUIPanelExitTrigger?.Close();
		_coroutine = null;
	}

	private void PruneDestroyedColliders()
	{
		_localColliders.RemoveWhere(collider => collider == null);
	}
}
