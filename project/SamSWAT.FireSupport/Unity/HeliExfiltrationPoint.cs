using Comfort.Common;
using EFT;
using EFT.UI;
using SamSWAT.FireSupport.ArysReloaded.Utils;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Standard UH-60 extraction zone. Cargo transfer uses a separate component
/// and never instantiates this one.
/// </summary>
public class HeliExfiltrationPoint : MonoBehaviour, IPhysicsTrigger
{
	private readonly ExtractionCountdownClock _countdown = new();
	private readonly HashSet<Collider> _localColliders = new();
	private Coroutine _coroutine;
	private BattleUIPanelExitTrigger _battleUIPanelExitTrigger;
	private GameWorld _gameWorld;
	private bool _initialized;
	private bool _completed;
	private CancellationToken _cancellationToken;

	public string Description => "HeliExfiltrationPoint";

	public void Initialize(
		float extractTimeSeconds,
		CancellationToken cancellationToken)
	{
		_countdown.Initialize(extractTimeSeconds);
		_cancellationToken = cancellationToken;
		_initialized = true;
	}

	private void Start()
	{
		if (!_initialized)
		{
			Initialize(
				FireSupportTuningSettings.GetHelicopterExtractTime(),
				CancellationToken.None);
		}

		_gameWorld = Singleton<GameWorld>.Instance;
		_battleUIPanelExitTrigger =
			Singleton<GameUI>.Instance?.BattleUiPanelExitTrigger;
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
		_battleUIPanelExitTrigger?.Show(_countdown.RemainingSeconds);

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
		StopCoroutine(_coroutine);
		_coroutine = null;
	}

	private void OnDestroy()
	{
		if (_coroutine != null)
		{
			StopCoroutine(_coroutine);
			_coroutine = null;
		}

		_localColliders.Clear();
		if (Singleton<GameUI>.Instantiated &&
		    _battleUIPanelExitTrigger != null)
		{
			_battleUIPanelExitTrigger.Close();
		}
	}

	private void ResetTimer()
	{
		_countdown.Reset();
	}

	private IEnumerator Timer(Player player)
	{
		while (!_countdown.IsComplete)
		{
			PruneDestroyedColliders();
			if (!CanCompleteExtraction(player))
			{
				CloseAndStopTimer();
				yield break;
			}

			yield return null;
			_countdown.Advance(Time.deltaTime);
		}

		if (!CanCompleteExtraction(player))
		{
			CloseAndStopTimer();
			yield break;
		}

		_completed = true;
		CloseAndStopTimer();

		// Fika owns multiplayer extraction so the host can keep the raid alive
		// and clients despawn cleanly. Solo SPT uses the ordinary session stop.
		if (FireSupportExtraction.TryOverrideExtract(
			    player,
			    "UH-60 Black Hawk"))
		{
			yield break;
		}

		if (Singleton<AbstractGame>.Instance is
		    ISessionStopper sessionStopper)
		{
			sessionStopper.StopSession(
				player.ProfileId,
				ExitStatus.Survived,
				"UH-60 Black Hawk");
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
